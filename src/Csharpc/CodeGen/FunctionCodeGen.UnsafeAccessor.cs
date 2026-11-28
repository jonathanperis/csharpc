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
        // upstream slice's System.Number, corelib/NumberUpstream.cs): bound at compile time, a direct call without the first (type-naming) argument.
        // The return type may be named too (EventSourceInitHelper.GetMetricsEventSource: `[return: UnsafeAccessorType("...MetricsEventSource, ...")]
        // static extern object GetInstance([UnsafeAccessorType("...MetricsEventSource, ...")] object? _)`, #455): an `object` the method's own
        // reference type converts to.
        var namedReturn = AccessorTypeName(target.GetReturnTypeAttributes()) is { } returnName ? ResolveAccessorType(returnName) : null;
        if (kind == 2 && target.Parameters.Length > 0 && AccessorTypeName(target.Parameters[0].GetAttributes()) is { } typeName
            && !target.Parameters.Skip(1).Any(p => HasAccessorTypeName(p.GetAttributes()))
            && (!HasAccessorTypeName(target.GetReturnTypeAttributes()) || namedReturn is { IsReferenceType: true } && target.ReturnType.SpecialType == SpecialType.System_Object)
            && ResolveAccessorType(typeName) is { } namedOwner)
        {
            var parameters = target.Parameters.Skip(1).ToArray();
            // A generic accessor (`static extern bool TryFormat<TChar>(object type, ..., Span<TChar> destination, ...)`, called as
            // TryFormat<char>) stands for the generic method of the same arity, instantiated with the accessor's type arguments.
            var typeArguments = target.TypeArguments.Select(a => S(a)).ToArray();
            var method = namedOwner.GetMembers(name).OfType<IMethodSymbol>()
                .Where(m => m.Arity == typeArguments.Length).Select(m => typeArguments.Length > 0 ? m.Construct(typeArguments) : m)
                .FirstOrDefault(m => m.IsStatic && m.Parameters.Length == parameters.Length
                    && m.Parameters.Zip(parameters).All(x => x.First.RefKind == x.Second.RefKind && SymbolEqualityComparer.Default.Equals(S(x.First.Type), S(x.Second.Type)))
                    && SymbolEqualityComparer.Default.Equals(S(m.ReturnType), namedReturn ?? S(target.ReturnType)))
                ?? throw CompileError.Unsupported($"[UnsafeAccessor] static method '{name}' not found on '{namedOwner}'", inv);
            return DirectCall(method, [.. args.Skip(1).Select(a => EmitArgument(a, target))]);
        }
        // An instance method (kind 1) of a type named by string that this compilation lowers from IL (the upstream slice's RuntimeEventSource,
        // whose LogAppContextSwitch event the corelib's AppContext.LogSwitchValues writes, #455): a non-virtual call on the first argument.
        if (kind == 1 && target.Parameters.Length > 0 && AccessorTypeName(target.Parameters[0].GetAttributes()) is { } instanceTypeName
            && !target.Parameters.Skip(1).Any(p => HasAccessorTypeName(p.GetAttributes())) && !HasAccessorTypeName(target.GetReturnTypeAttributes())
            && ResolveAccessorType(instanceTypeName) is { IsReferenceType: true } instanceOwner && _m.Il.OwnerOf(instanceOwner) is not null)
        {
            var instanceParameters = target.Parameters.Skip(1).ToArray();
            var instanceMethod = FindMember<IMethodSymbol>(instanceOwner, name, m => !m.IsStatic && m.Arity == 0 && m.Parameters.Length == instanceParameters.Length
                    && m.Parameters.Zip(instanceParameters).All(x => x.First.RefKind == x.Second.RefKind && SymbolEqualityComparer.Default.Equals(S(x.First.Type), S(x.Second.Type)))
                    && SymbolEqualityComparer.Default.Equals(S(m.ReturnType), S(target.ReturnType)))
                ?? throw CompileError.Unsupported($"[UnsafeAccessor] method '{name}' not found on '{instanceOwner}'", inv);
            var instanceValues = args.Select(a => EmitArgument(a, target)).ToArray();
            NullCheck(instanceValues[0]);
            return DirectCall(instanceMethod, instanceValues);
        }
        // A static field (kind 4) or static method (kind 2) of the type the first parameter names, by its own type (`CultureInfo? _`) or by
        // [UnsafeAccessorType]: the field's address, or a direct call without that first argument (the corelib's contract with the
        // framework's CultureInfo, corelib/UpstreamGlobalization.cs).
        if (kind is 2 or 4 && target.Parameters.Length > 0
            && (AccessorTypeName(target.Parameters[0].GetAttributes()) is { } ownerName ? ResolveAccessorType(ownerName) : S(target.Parameters[0].Type) as INamedTypeSymbol) is { } staticOwner
            && !target.Parameters.Skip(1).Any(p => HasAccessorTypeName(p.GetAttributes())) && !HasAccessorTypeName(target.GetReturnTypeAttributes()))
        {
            if (kind == 4)
            {
                var staticField = FindMember<IFieldSymbol>(staticOwner, name, f => f.IsStatic)
                    ?? throw CompileError.Unsupported($"[UnsafeAccessor] static field '{name}' not found on '{staticOwner}'", inv);
                return StaticFieldAddress(staticField);
            }
            var staticParameters = target.Parameters.Skip(1).ToArray();
            if (FindMember<IMethodSymbol>(staticOwner, name, m => m.IsStatic && m.Arity == 0 && m.Parameters.Length == staticParameters.Length
                    && m.Parameters.Zip(staticParameters).All(x => x.First.RefKind == x.Second.RefKind && SymbolEqualityComparer.Default.Equals(S(x.First.Type), S(x.Second.Type)))
                    && SymbolEqualityComparer.Default.Equals(S(m.ReturnType), S(target.ReturnType))) is { } staticMethod)
                return DirectCall(staticMethod, [.. args.Skip(1).Select(a => EmitArgument(a, target))]);
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
                if (S(target.ReturnType) is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } owner)
                    throw CompileError.Unsupported($"[UnsafeAccessor] constructor of '{target.ReturnType}'", inv);
                var parameterTypes = target.Parameters.Select(p => S(p.Type)).ToArray();
                // DateTime's private DateTime(ulong dateData) (CoreLib's own, absent from the reference assemblies): csharpc's DateTime
                // is that `_dateData` (corelib/DateTimeOps.cs), so the value is the argument itself (#99).
                if (BclValueTypes.IsDateTime(owner) && parameterTypes is [{ SpecialType: SpecialType.System_UInt64 }])
                    return EmitArgument(args[0], target);
                var ctor = owner.InstanceConstructors.FirstOrDefault(c => c.Parameters.Select(p => S(p.Type)).SequenceEqual(parameterTypes, SymbolEqualityComparer.Default))
                    ?? throw CompileError.Unsupported($"[UnsafeAccessor] constructor of '{owner}' not found", inv);
                // A library type (DateTime, a corelib shadow, a type with intrinsic constructors): created as `new T(...)` would be, so its
                // own representation applies (#99); a constructor csharpc has no implementation for stays that explicit diagnostic.
                if (!_m.IsUserType(owner))
                    return EmitObjectCreation(new IlObjectCreationOperation(inv.Syntax, ctor,
                        [.. args.Select((a, i) => (IArgumentOperation)new IlArgumentOperation(a.Syntax, ctor.Parameters[i], a.Value))]));
                if (owner.TypeKind == TypeKind.Struct)
                {
                    // A struct is created in place: a zeroed value whose address is the constructor's `this` (the corelib's
                    // string.EnumerateRunes builds CoreLib's StringRuneEnumerator through its internal constructor).
                    var slot = Spill(LlvmContext.Null(T.Storage(owner)));
                    DirectCall(ctor, [slot, .. args.Select(a => EmitArgument(a, target))]);
                    return LoadValue(slot, owner);
                }
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
            // Any accessibility: reaching a type its caller cannot name (an internal type of another assembly, the slice's DateTimeFormat) is
            // what the attribute is for.
            if (candidate.ContainingAssembly?.Name == assembly) return candidate;
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
