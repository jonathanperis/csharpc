using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Static abstract members of interfaces that the reference pack does not declare, on the built-in types (issue #221).
///
/// CoreLib's number and text code is written once over internal interfaces (<c>IUtfChar&lt;TChar&gt;</c>,
/// <c>IBinaryIntegerParseAndFormatInfo&lt;T&gt;</c>, <c>IBinaryFloatParseAndFormatInfo&lt;T&gt;</c>) that char, byte, the integers and the
/// floating-point types implement; the reference pack's <c>char</c> does not, so Roslyn finds no implementation for a
/// <c>constrained. TChar call IUtfChar&lt;TChar&gt;::CastFrom</c>. A slice of the framework's IL (tools/il-first/slicer) therefore carries the
/// primitives' own implementation methods in one class per built-in type, listed in the map file (<see cref="Frontend.UpstreamSlice.MemberClasses"/>),
/// and this is the lookup that finds them. It is general: any IL assembly generic over such an interface goes through it, nothing here names a library.
/// A member without an implementation is an explicit diagnostic; it used to be an external function and invalid IR.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private IMethodSymbol? BuiltinStaticInterfaceMember(IMethodSymbol target, ITypeSymbol concrete)
    {
        // Interfaces of the reference pack (INumber<T>...) keep their own paths (generic-math lowering).
        if (concrete is not INamedTypeSymbol named || !_m.IsUserType(target.ContainingType)) return null;
        var real = Mangler.MetadataName(named.OriginalDefinition);
        var expected = RealInterfaceName(target.ContainingType) + "." + target.Name;
        if (Frontend.UpstreamSlice.MemberClasses.TryGetValue(real, out var classes))
            foreach (var className in classes)
            {
                // Two slices in one compilation (the shipped one and a CSHARPC_UPSTREAM_MAP one) each carry a class of this name, with the
                // members for their own copy of the interface: the one of the interface's assembly first.
                var interfaceAssembly = target.ContainingType.ContainingAssembly;
                foreach (var holder in _m.Compilation.GetTypesByMetadataName(className)
                             .OrderBy(h => SymbolEqualityComparer.Default.Equals(h.ContainingAssembly, interfaceAssembly) ? 0 : 1))
                    foreach (var candidate in holder.GetMembers(expected).OfType<IMethodSymbol>())
                        if (candidate.IsStatic && SameSignature(candidate, target)) return candidate;
            }
        return ShadowStaticMember(concrete, target)
            ?? throw CompileError.Unsupported($"no implementation of the static abstract member '{target.ContainingType.OriginalDefinition}.{target.Name}' for the built-in type '{concrete}' "
                + "(an interface that the reference pack does not declare, implemented by the primitive inside System.Private.CoreLib; "
                + "the slice must carry the implementation: tools/il-first/slicer)", target);
    }

    /// <summary>
    /// A built-in type's static member csharpc has no lowering of its own for (no intrinsic, shadow or generic-math path), served from
    /// the type's own CoreLib IL in its host class (the slicer's `host:` roots, src/Csharpc/upstream-types/System.IntegerMembers.txt): called
    /// directly (`int.Log2(x)`) or through generic code (`T.Log2(x)` with T = int: the interface member is mapped to the type's
    /// implementation, `System.Numerics.INumberBase&lt;System.Int32&gt;.IsNegative` for an explicit one). Null when the slice carries none.
    /// </summary>
    internal IMethodSymbol? HostedBuiltinMember(IMethodSymbol target, ITypeSymbol? constrainedTo)
    {
        if (!target.IsStatic) return HostedBuiltinInstanceMember(target);
        var name = target.MetadataName;
        INamedTypeSymbol owner;
        if (target.ContainingType.TypeKind == TypeKind.Interface)
        {
            if (constrainedTo is null || S(constrainedTo) is not INamedTypeSymbol concrete) return null;
            var member = target.IsGenericMethod ? target.ConstructedFrom : target;
            // A protected member's explicit implementation (INumberBase<ulong>.TryConvertToChecked<TOther>, which CreateChecked's IL calls on
            // its TOther) is private, so the reference pack leaves it out: the host class has it under the name C# gives it.
            name = concrete.FindImplementationForInterfaceMember(member) is IMethodSymbol found ? found.MetadataName : ExplicitImplementationName(target);
            owner = concrete;
        }
        else owner = target.ContainingType;
        if (!Frontend.UpstreamSlice.MemberClasses.TryGetValue(Mangler.MetadataName(owner.OriginalDefinition), out var classes)) return null;
        var typeArguments = target.TypeArguments.Select(a => S(a)).ToArray();
        foreach (var className in classes)
            foreach (var holder in _m.Compilation.GetTypesByMetadataName(className))
                foreach (var candidate in holder.GetMembers(name).OfType<IMethodSymbol>())
                {
                    if (!candidate.IsStatic || candidate.TypeParameters.Length != typeArguments.Length) continue;
                    var constructed = typeArguments.Length > 0 ? candidate.Construct(typeArguments) : candidate;
                    if (SameSignature(constructed, target)) return constructed;
                    // A type argument the corelib redirects (KeyValuePair<K, V> -> its shadow) maps at the top of a type, not inside a constructed
                    // parameter type (string.Join<T>(string, IEnumerable<T>) over a Dictionary): compare with the call's own type arguments.
                    if (typeArguments.Length > 0 && SameSignature(candidate.Construct([.. target.TypeArguments]), target)) return constructed;
                }
        return null;
    }

    /// <summary>
    /// An instance member of a built-in type served from its own CoreLib IL (`this.` host roots): int.ToString(string, IFormatProvider), whose IL
    /// reads `this` as int&amp; and calls Number.FormatInt32, is hosted as a static whose first parameter is `ref T`, the receiver's address;
    /// string.Split, whose IL reads `this._stringLength` and `ref this._firstChar`, as a static whose first parameter is the string itself.
    /// Null when the slice carries none.
    /// </summary>
    private IMethodSymbol? HostedBuiltinInstanceMember(IMethodSymbol target)
    {
        var owner = target.ContainingType;
        if (target.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation or MethodKind.PropertyGet)) return null;
        if (!Frontend.UpstreamSlice.MemberClasses.TryGetValue(Mangler.MetadataName(owner.OriginalDefinition), out var classes)) return null;
        var typeArguments = target.TypeArguments.Select(a => S(a)).ToArray();
        foreach (var className in classes)
            foreach (var holder in _m.Compilation.GetTypesByMetadataName(className))
                foreach (var candidate in holder.GetMembers(target.MetadataName).OfType<IMethodSymbol>())
                {
                    if (!candidate.IsStatic || candidate.TypeParameters.Length != typeArguments.Length || candidate.Parameters.Length != target.Parameters.Length + 1) continue;
                    if (candidate.Parameters[0] is not { } self || self.RefKind != (owner.IsValueType ? RefKind.Ref : RefKind.None)
                        || !SymbolEqualityComparer.Default.Equals(S(self.Type), S(owner))) continue;
                    var constructed = typeArguments.Length > 0 ? candidate.Construct(typeArguments) : candidate;
                    if (constructed.Parameters.Skip(1).Zip(target.Parameters).All(p => p.First.RefKind == p.Second.RefKind
                            && SymbolEqualityComparer.Default.Equals(S(p.First.Type), S(p.Second.Type))))
                        return constructed;
                }
        return null;
    }

    /// <summary>
    /// A call of a hosted instance member (HostedBuiltinInstanceMember): the receiver's address (a value type) or the receiver itself (a string,
    /// whose hosted body starts with the callvirt's null check: tools/il-first/slicer), then the arguments.
    /// </summary>
    private LLVMValueRef EmitHostedInstanceCall(IMethodSymbol hosted, IMethodSymbol target, IOperation instance, ImmutableArray<IArgumentOperation> arguments)
    {
        var receiverType = S(target.ContainingType);
        var receiver = Coerce(EmitValue(instance), TypeOf(instance), receiverType);
        var self = receiverType.IsValueType ? Spill(receiver) : receiver;
        var args = new LLVMValueRef[hosted.Parameters.Length];
        args[0] = self;
        foreach (var argument in arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        return DirectCall(hosted, args);
    }

    /// <summary>
    /// The body of a static default member of a reference-pack interface (INumberBase&lt;TSelf&gt;.TryParse over UTF-8, which Decimal32 and
    /// user number types don't implement themselves): the reference pack declares the member without its IL, so the slice hosts CoreLib's
    /// in the interface's host class as a generic method over the interface's type arguments, then the member's own (the slicer's `host:`
    /// roots on a generic interface). Null when the slice carries none: the call is then an explicit diagnostic.
    /// </summary>
    private IMethodSymbol? HostedDefaultInterfaceMember(IMethodSymbol target)
    {
        var iface = target.ContainingType;
        if (!target.IsStatic || target.IsAbstract || iface.TypeKind != TypeKind.Interface || _m.IsUserType(iface)) return null;
        if (!Frontend.UpstreamSlice.MemberClasses.TryGetValue(Mangler.MetadataName(iface.OriginalDefinition), out var classes)) return null;
        ITypeSymbol[] typeArguments = [.. iface.TypeArguments.Select(a => S(a)), .. target.TypeArguments.Select(a => S(a))];
        if (typeArguments.Any(a => a is ITypeParameterSymbol)) return null;
        foreach (var className in classes)
            foreach (var holder in _m.Compilation.GetTypesByMetadataName(className))
                foreach (var candidate in holder.GetMembers(target.MetadataName).OfType<IMethodSymbol>())
                {
                    if (!candidate.IsStatic || candidate.TypeParameters.Length != typeArguments.Length) continue;
                    var constructed = candidate.Construct(typeArguments);
                    if (constructed.Parameters.Length == target.Parameters.Length
                        && constructed.Parameters.Zip(target.Parameters).All(p => p.First.RefKind == p.Second.RefKind && SymbolEqualityComparer.Default.Equals(S(p.First.Type), S(p.Second.Type))))
                        return constructed;
                }
        return null;
    }

    /// <summary>"System.Numerics.INumberBase&lt;System.UInt64&gt;.TryConvertToChecked": the metadata name of an explicit implementation of the member.</summary>
    private string ExplicitImplementationName(IMethodSymbol member)
    {
        var type = member.ContainingType;
        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() + "." : "";
        var arguments = type.TypeArguments.Select(a => S(a) is INamedTypeSymbol named ? Mangler.MetadataName(named) : a.ToDisplayString());
        return ns + type.Name + (type.TypeArguments.Length > 0 ? "<" + string.Join(",", arguments) + ">" : "") + "." + member.Name;
    }

    /// <summary>"System.IUtfChar`1": the interface's CoreLib metadata name, without the namespace a slice puts its copy under.</summary>
    private static string RealInterfaceName(INamedTypeSymbol type)
    {
        var name = Mangler.MetadataName(type.OriginalDefinition);
        return name.StartsWith(Frontend.UpstreamSlice.Prefix + ".", StringComparison.Ordinal) ? name[(Frontend.UpstreamSlice.Prefix.Length + 1)..] : name;
    }

    private bool SameSignature(IMethodSymbol candidate, IMethodSymbol target)
    {
        if (candidate.Parameters.Length != target.Parameters.Length || candidate.TypeParameters.Length != target.TypeParameters.Length) return false;
        for (var i = 0; i < candidate.Parameters.Length; i++)
            if (candidate.Parameters[i].RefKind != target.Parameters[i].RefKind
                || !SymbolEqualityComparer.Default.Equals(S(candidate.Parameters[i].Type), S(target.Parameters[i].Type))) return false;
        return true;
    }
}
