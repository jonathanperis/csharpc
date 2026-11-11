using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// CSHARPC_GENERIC_REPORT=&lt;file&gt;: one line per lowered function that is a generic instantiation (its own type arguments or a containing
/// type's), with its definition, its exact instantiation and the canonical form shared generic code would compile it as (every reference-type
/// type argument replaced by __Canon, value types kept: ADR 0026), and the LLVM function's name, so a size table (nm) can be joined to it.
/// Measurement only (ADR 0026, step 1): it changes nothing in the output.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private static readonly string? s_genericReport = Environment.GetEnvironmentVariable("CSHARPC_GENERIC_REPORT");
    private readonly List<IMethodSymbol> _genericLowered = [];

    private void NoteGenericLowered(ISymbol symbol)
    {
        if (s_genericReport is null || symbol is not IMethodSymbol method || !IsInstantiation(method)) return;
        _genericLowered.Add(method);
    }

    private static bool IsInstantiation(IMethodSymbol method)
    {
        if (method.IsGenericMethod && !method.TypeArguments.All(t => t is ITypeParameterSymbol)) return true;
        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
            if (type.IsGenericType && !type.TypeArguments.All(t => t is ITypeParameterSymbol)) return true;
        return false;
    }

    private void WriteGenericReport()
    {
        if (s_genericReport is null) return;
        using var writer = new StreamWriter(s_genericReport, append: true);
        foreach (var method in _genericLowered)
        {
            var definition = method.OriginalDefinition.ContainingType.OriginalDefinition.ToDisplayString() + "." + method.OriginalDefinition.Name
                + (method.Arity > 0 ? "`" + method.Arity : "") + "(" + method.OriginalDefinition.Parameters.Length + ")";
            var exact = method.ToDisplayString();
            var canonical = Canonical(method);
            var name = _functions.TryGetValue(method, out var lowered) ? Native.LLVMGetValueName(lowered.Fn) : "";
            writer.WriteLine($"{definition}\t{exact}\t{canonical}\t{name}");
        }
    }

    /// <summary>The canonical form's display text: reference-type type arguments (recursively) are __Canon.</summary>
    private static string Canonical(IMethodSymbol method)
    {
        static string Type(ITypeSymbol type) => type switch
        {
            _ when type.IsReferenceType => "__Canon",
            INamedTypeSymbol { IsGenericType: true } named => named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).Split('<')[0]
                + "<" + string.Join(",", named.TypeArguments.Select(Type)) + ">",
            _ => type.ToDisplayString(),
        };
        static string Owner(INamedTypeSymbol type) =>
            (type.ContainingType is { } outer ? Owner(outer) + "+" : type.ContainingNamespace.ToDisplayString() + ".") + type.Name
            + (type.IsGenericType ? "<" + string.Join(",", type.TypeArguments.Select(Type)) + ">" : "");
        return Owner(method.ContainingType) + "." + method.Name + (method.IsGenericMethod ? "<" + string.Join(",", method.TypeArguments.Select(Type)) + ">" : "")
            + "(" + method.Parameters.Length + ")";
    }
}
