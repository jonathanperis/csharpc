using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// `s.StartsWith(x, StringComparison.Ordinal)` and the like: when the trailing StringComparison argument
    /// of a corelib method is the constant Ordinal or OrdinalIgnoreCase, bind to the corelib method named
    /// &lt;Name&gt;Ordinal / &lt;Name&gt;OrdinalIgnoreCase with the remaining parameters (StringOps). The
    /// general method also reaches the culture-sensitive comparisons, and everything a function references is
    /// lowered (and its virtual call sites keep their targets): an ordinal comparison would otherwise link
    /// CompareInfo and the whole culture model. The dropped argument is a constant, so nothing is skipped.
    /// </summary>
    private (IMethodSymbol Method, ImmutableArray<IArgumentOperation> Arguments)? OrdinalComparisonVariant(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.IsGenericMethod || target.Parameters.Length == 0 || !CoreLib.IsCoreLib(target)) return null;
        if (!IsStringComparison(target.Parameters[^1].Type)) return null;
        var instanceShadow = CoreLib.IsInstanceShadow(target) && inv.Instance is not null;
        var ordinal = target.Parameters.Length - 1 - (instanceShadow ? 1 : 0); // of the library method's parameters
        var comparison = inv.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal && a.ArgumentKind == ArgumentKind.Explicit);
        if (comparison is not { Value.ConstantValue: { HasValue: true, Value: int value } } || !IsStringComparison(comparison.Parameter!.Type)) return null;
        var suffix = value switch { (int)StringComparison.Ordinal => "Ordinal", (int)StringComparison.OrdinalIgnoreCase => "OrdinalIgnoreCase", _ => null };
        if (suffix is null) return null;
        var variant = target.ContainingType.GetMembers(target.Name + suffix).OfType<IMethodSymbol>().FirstOrDefault(m =>
            m.IsStatic == target.IsStatic && !m.IsGenericMethod && m.Parameters.Length == target.Parameters.Length - 1
            && CoreLib.IsInstanceShadow(m) == CoreLib.IsInstanceShadow(target)
            && SymbolEqualityComparer.Default.Equals(m.ReturnType, target.ReturnType)
            && m.Parameters.Zip(target.Parameters).All(p => p.First.RefKind == p.Second.RefKind
                && SymbolEqualityComparer.Default.Equals(p.First.Type, p.Second.Type)));
        return variant is null ? null : (variant, inv.Arguments.Remove(comparison));
    }

    private static bool IsStringComparison(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "StringComparison", TypeKind: TypeKind.Enum, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
}
