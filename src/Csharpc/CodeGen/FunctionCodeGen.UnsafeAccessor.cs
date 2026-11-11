using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// [UnsafeAccessor] extern methods (.NET 8+): accessors to otherwise inaccessible members, used by the
/// System.Text.Json source generator for init-only setters and [JsonInclude] private members. In a
/// closed world the target member is known at compile time, so a call is lowered as a direct access:
///
///   [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "secret")] static extern ref int F(Person p);
///   F(p) = 1;      →  store to &amp;p.secret
/// </summary>
public sealed partial class FunctionCodeGen
{
    // Roslyn reports `static extern` methods from metadata as not extern when they have neither a
    // [DllImport] nor an InternalCall flag: an [UnsafeAccessor] in framework IL is only a method with no RVA.
    private static AttributeData? UnsafeAccessorOf(IMethodSymbol method) =>
        method.IsExtern || (method.IsStatic && method.OriginalDefinition.MetadataToken != 0 && !method.IsAbstract && method.MethodKind == MethodKind.Ordinary
            && method.DeclaredAccessibility is Accessibility.Private or Accessibility.Internal)
            ? method.GetAttributes().FirstOrDefault(a => a.AttributeClass is
                { Name: "UnsafeAccessorAttribute", ContainingNamespace: { Name: "CompilerServices", ContainingNamespace.Name: "Runtime" } })
            : null;

    private LLVMValueRef EmitUnsafeAccessor(IMethodSymbol target, AttributeData attribute, IInvocationOperation inv)
    {
        // UnsafeAccessorKind: Constructor = 0, Method = 1, StaticMethod = 2, Field = 3, StaticField = 4.
        var kind = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int k ? k : -1;
        var name = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string ?? target.Name;
        var args = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).ToArray();
        // An accessor whose type is given by name ([UnsafeAccessorType("System.Net.Http.HttpClient, System.Net.Http")] on an `object`):
        // the framework's way to reach an assembly it must not reference (X509ResourceClient's certificate downloads over HttpClient,
        // inside a try/catch that gives up when the type can't be loaded). The target is resolved at run time there, and csharpc binds
        // accessors at compile time, so the call throws what .NET throws for a missing member and the caller's own fallback applies.
        // A static method of a type named by string that this compilation knows (a public type of an IL assembly it compiles, such as the
        // upstream slice's NumberBridge contract member): bound at compile time, a direct call without the first (type-naming) argument.
        if (kind == 2 && target.Parameters.Length > 0 && AccessorTypeName(target.Parameters[0].GetAttributes()) is { } typeName
            && !target.Parameters.Skip(1).Any(p => HasAccessorTypeName(p.GetAttributes())) && !HasAccessorTypeName(target.GetReturnTypeAttributes())
            && ResolveAccessorType(typeName) is { } namedOwner)
        {
            var parameters = target.Parameters.Skip(1).ToArray();
            var method = namedOwner.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == parameters.Length
                    && m.Parameters.Zip(parameters).All(x => x.First.RefKind == x.Second.RefKind && SymbolEqualityComparer.Default.Equals(S(x.First.Type), S(x.Second.Type)))
                    && SymbolEqualityComparer.Default.Equals(S(m.ReturnType), S(target.ReturnType)))
                ?? throw CompileError.Unsupported($"[UnsafeAccessor] static method '{name}' not found on '{namedOwner}'", inv);
            return DirectCall(method, [.. args.Skip(1).Select(a => EmitArgument(a, target))]);
        }
        if (kind is 0 or 1 or 2 && (HasAccessorTypeName(target.GetReturnTypeAttributes()) || target.Parameters.Any(p => HasAccessorTypeName(p.GetAttributes())))
            && !(kind == 2 && name == "CreateObjectArrayDelegate"))
        {
            ThrowAndContinue("System.MissingMethodException", $"[UnsafeAccessor] '{name}' names its type as a string, which csharpc does not bind yet.");
            return target.ReturnsVoid ? default : LlvmContext.Null(T.Value(S(target.ReturnType)));
        }
        switch (kind)
        {
            case 3: // instance field: the first parameter is the object (or `ref` struct)
            {
                var owner = S(target.Parameters[0].Type);
                var field = FindMember<IFieldSymbol>(owner, name, f => !f.IsStatic)
                    ?? throw CompileError.Unsupported($"[UnsafeAccessor] field '{name}' not found on '{owner}'", inv);
                var self = EmitArgument(args[0], target);
                if (!owner.IsValueType) NullCheck(self);
                return FieldAddressOn(self, field);
            }
            case 1: // instance method (non-virtual call, like the runtime's accessor)
            {
                var owner = S(target.Parameters[0].Type);
                var parameterTypes = target.Parameters.Skip(1).Select(p => S(p.Type)).ToArray();
                var method = FindMember<IMethodSymbol>(owner, name, m => !m.IsStatic && m.Parameters.Length == parameterTypes.Length
                        && m.Parameters.Select(p => S(p.Type)).SequenceEqual(parameterTypes, SymbolEqualityComparer.Default))
                    ?? throw CompileError.Unsupported($"[UnsafeAccessor] method '{name}' not found on '{owner}'", inv);
                if (!_m.IsUserMethod(method)) throw CompileError.Unsupported($"[UnsafeAccessor] to library method '{method}'", inv);
                var values = args.Select(a => EmitArgument(a, target)).ToArray();
                if (!owner.IsValueType) NullCheck(values[0]);
                return DirectCall(method, values);
            }
            case 0: // constructor: the return type is the type to create
            {
                if (S(target.ReturnType) is not INamedTypeSymbol { TypeKind: TypeKind.Class } owner || !_m.IsUserType(owner))
                    throw CompileError.Unsupported($"[UnsafeAccessor] constructor of '{target.ReturnType}'", inv);
                var parameterTypes = target.Parameters.Select(p => S(p.Type)).ToArray();
                var ctor = owner.InstanceConstructors.FirstOrDefault(c => c.Parameters.Select(p => S(p.Type)).SequenceEqual(parameterTypes, SymbolEqualityComparer.Default))
                    ?? throw CompileError.Unsupported($"[UnsafeAccessor] constructor of '{owner}' not found", inv);
                var layout = _m.GetLayout(owner);
                var obj = Allocate(layout.MethodTable, L.ConstInt(L.I64, (long)layout.InstanceSize));
                DirectCall(ctor, [obj, .. args.Select(a => EmitArgument(a, target))]);
                return obj;
            }
            // System.Linq.Expressions' interpreter: a delegate of any type whose Invoke packs its arguments
            // into an object[] (NativeAOT's DynamicDelegateAugments in System.Private.CoreLib).
            case 2 when name == "CreateObjectArrayDelegate" && target.Parameters.Length == 3
                && target.Parameters[0].GetAttributes().Any(a => a.AttributeClass?.Name == "UnsafeAccessorTypeAttribute"
                    && a.ConstructorArguments is [{ Value: string accessed }] && accessed.StartsWith("Internal.Runtime.Augments.DynamicDelegateAugments,", StringComparison.Ordinal)):
                return CallCoreLib("ObjectArrayDelegates", "Create", EmitArgument(args[1], target), EmitArgument(args[2], target));
            default:
                throw CompileError.Unsupported($"[UnsafeAccessor] of kind {kind}", inv);
        }
    }

    private static string? AccessorTypeName(IEnumerable<AttributeData> attributes) =>
        attributes.FirstOrDefault(a => a.AttributeClass?.Name == "UnsafeAccessorTypeAttribute")?.ConstructorArguments is [{ Value: string named }] ? named : null;

    /// <summary>"Namespace.Type, Assembly": the type of that name in that assembly of this compilation, or null (resolved at run time by .NET).</summary>
    private INamedTypeSymbol? ResolveAccessorType(string assemblyQualified)
    {
        var comma = assemblyQualified.IndexOf(',');
        if (comma <= 0) return null;
        var typeName = assemblyQualified[..comma].Trim();
        var assembly = assemblyQualified[(comma + 1)..].Split(',')[0].Trim();
        foreach (var candidate in _m.Compilation.GetTypesByMetadataName(typeName))
            if (candidate.ContainingAssembly?.Name == assembly && candidate.DeclaredAccessibility == Accessibility.Public) return candidate;
        return null;
    }

    private static bool HasAccessorTypeName(IEnumerable<AttributeData> attributes) =>
        attributes.Any(a => a.AttributeClass?.Name == "UnsafeAccessorTypeAttribute");

    private static T? FindMember<T>(ITypeSymbol owner, string name, Func<T, bool> predicate) where T : class, ISymbol
    {
        for (var t = owner; t is not null; t = t.BaseType)
            if (t.GetMembers(name).OfType<T>().FirstOrDefault(predicate) is { } found) return found;
        return null;
    }
}
