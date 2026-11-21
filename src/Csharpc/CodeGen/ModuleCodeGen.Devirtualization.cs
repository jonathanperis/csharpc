using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Closed-world devirtualization (class hierarchy analysis). csharpc compiles the whole program
/// and library classes cannot derive from user classes, so the complete set of classes that can
/// receive a virtual call through a given static type is known. When every instantiable class in
/// that subtree resolves the slot to the same method, the call becomes a direct call — which
/// LLVM can then inline. NativeAOT does the same for sealed/"effectively sealed" members.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<IMethodSymbol, IMethodSymbol?> _devirtualized = new(SymbolEqualityComparer.Default);

    /// <summary>The unique implementation a virtual call to <paramref name="target"/> can reach, or null.</summary>
    public IMethodSymbol? Devirtualize(IMethodSymbol target)
    {
        target = target.OriginalDefinition;
        if (_devirtualized.TryGetValue(target, out var cached)) return cached;
        return _devirtualized[target] = FindUniqueImplementation(target);
    }

    private IMethodSymbol? FindUniqueImplementation(IMethodSymbol target)
    {
        var receiver = target.ContainingType;
        if (!IsUserType(receiver) || receiver.TypeKind != TypeKind.Class || receiver.IsGenericType) return null;
        if (target.IsGenericMethod) return null;
        var root = ClassLayout.RootOf(target);

        IMethodSymbol? unique = null;
        foreach (var type in HierarchyTypes)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || !DerivesFromOrEqualsServed(type, receiver)) continue;
            if (type.IsGenericType || TypeSubstitution.IsGenericContext(type)) return null;
            var implementation = ResolveSlot(type, root);
            if (implementation is null) return null;
            if (unique is null) unique = implementation;
            else if (!SymbolEqualityComparer.Default.Equals(unique, implementation)) return null;
        }
        return unique;
    }

    /// <summary>
    /// The most-derived non-abstract method on <paramref name="type"/>'s base chain filling <paramref name="root"/>'s slot.
    /// <paramref name="type"/> is not generic, so its base chain is closed: an implementation inherited from a
    /// generic base class is that base's instantiation (Roslyn's CSharp.Emit.PEModuleBuilder derives from
    /// PEModuleBuilder&lt;CSharpCompilation, ...&gt;), not the open definition.
    /// </summary>
    internal static IMethodSymbol? ResolveSlot(INamedTypeSymbol type, IMethodSymbol root)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers(root.Name).OfType<IMethodSymbol>())
            {
                if (m.IsAbstract || !(m.IsVirtual || m.IsOverride)) continue;
                if (SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m), root))
                    return TypeSubstitution.IsOpen(m) ? null : m;
            }
        }
        return null;
    }

    private readonly Dictionary<(INamedTypeSymbol Type, INamedTypeSymbol Interface), bool> _reimplemented = [];

    /// <summary>
    /// Whether a class deriving from <paramref name="type"/> names <paramref name="interfaceType"/> again in its own interface list (an
    /// interface re-implementation, ECMA-335 II.12.2): its implementation of the interface's members then replaces the one
    /// <paramref name="type"/> provides, so a call through the interface on a receiver statically typed <paramref name="type"/> (even
    /// `((I)this).M()` inside it) must be dispatched on the object's run-time type. System.Net.Sockets' ReadOperation and WriteOperation
    /// re-implement IThreadPoolWorkItem over AsyncOperation's (sample 1460).
    /// </summary>
    internal bool InterfaceReimplementedBelow(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
    {
        if (type.IsSealed || type.TypeKind != TypeKind.Class) return false;
        var key = ((INamedTypeSymbol)type.OriginalDefinition, (INamedTypeSymbol)interfaceType.OriginalDefinition);
        if (_reimplemented.TryGetValue(key, out var known)) return known;
        var found = false;
        foreach (var candidate in HierarchyTypes)
        {
            if (candidate.TypeKind != TypeKind.Class || SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, key.Item1)
                || !DerivesFromOrEqualsServed(candidate, key.Item1)) continue;
            foreach (var declared in candidate.Interfaces)
                if (SymbolEqualityComparer.Default.Equals(declared.OriginalDefinition, key.Item2)
                    || declared.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, key.Item2)))
                {
                    found = true;
                    break;
                }
            if (found) break;
        }
        return _reimplemented[key] = found;
    }

    /// <summary>
    /// <see cref="DerivesFromOrEquals"/> through the types csharpc serves a library type with: a program class names the reference assembly's
    /// base (`class L : EventListener`), while the framework's IL that makes the virtual call names the type that serves it (the slice's
    /// EventListener). Without the mapping, the program's classes would be missing from the subtree, and a call whose only other override is
    /// the framework's own (XplatEventLogger.OnEventSourceCreated) would be devirtualized to it.
    /// </summary>
    private bool DerivesFromOrEqualsServed(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var wanted = baseType.OriginalDefinition;
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, wanted)
                || CoreLib.TryMap(t) is { } served && SymbolEqualityComparer.Default.Equals(served.OriginalDefinition, wanted))
                return true;
        return false;
    }

    internal static bool DerivesFromOrEquals(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType.OriginalDefinition)) return true;
        return false;
    }
}
