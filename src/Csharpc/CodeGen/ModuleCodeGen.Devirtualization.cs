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
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || !DerivesFromOrEquals(type, receiver)) continue;
            if (type.IsGenericType) return null;
            var implementation = ResolveSlot(type, root);
            if (implementation is null) return null;
            if (unique is null) unique = implementation;
            else if (!SymbolEqualityComparer.Default.Equals(unique, implementation)) return null;
        }
        return unique;
    }

    /// <summary>The most-derived non-abstract method on <paramref name="type"/>'s base chain filling <paramref name="root"/>'s slot.</summary>
    private static IMethodSymbol? ResolveSlot(INamedTypeSymbol type, IMethodSymbol root)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers(root.Name).OfType<IMethodSymbol>())
            {
                if (m.IsAbstract || !(m.IsVirtual || m.IsOverride)) continue;
                if (SymbolEqualityComparer.Default.Equals(ClassLayout.RootOf(m), root)) return m.OriginalDefinition;
            }
        }
        return null;
    }

    private static bool DerivesFromOrEquals(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType.OriginalDefinition)) return true;
        return false;
    }
}
