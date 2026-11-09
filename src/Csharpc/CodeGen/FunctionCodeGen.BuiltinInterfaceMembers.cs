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
                if (_m.Compilation.GetTypeByMetadataName(className) is not { } holder) continue;
                foreach (var candidate in holder.GetMembers(expected).OfType<IMethodSymbol>())
                    if (candidate.IsStatic && SameSignature(candidate, target)) return candidate;
            }
        return ShadowStaticMember(concrete, target)
            ?? throw CompileError.Unsupported($"no implementation of the static abstract member '{target.ContainingType.OriginalDefinition}.{target.Name}' for the built-in type '{concrete}' "
                + "(an interface that the reference pack does not declare, implemented by the primitive inside System.Private.CoreLib; "
                + "the slice must carry the implementation: tools/il-first/slicer)", target);
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
