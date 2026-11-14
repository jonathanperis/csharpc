using Microsoft.CodeAnalysis;

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
    private IMethodSymbol? HostedBuiltinMember(IMethodSymbol target, ITypeSymbol? constrainedTo)
    {
        if (!target.IsStatic) return null;
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
