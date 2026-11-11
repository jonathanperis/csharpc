using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// CSHARPC_GENERIC_REPORT=&lt;file&gt;: one tab-separated line per lowered function that is a generic instantiation (its own type arguments or a
/// containing type's): its definition, its exact instantiation, the canonical form shared generic code would compile it as (every
/// reference-type type argument replaced by __Canon, value types kept: ADR 0026), whether that form is shared code, the number of
/// generic-dictionary slots the canonical body would need (-1: body not readable), and the LLVM function's name, so a size table (nm) can be
/// joined to it. Measurement only (ADR 0026, steps 1 and 2): it changes nothing in the output, and with the variable unset it costs one test
/// of a static field per lowered function.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private static readonly string? s_genericReport = Environment.GetEnvironmentVariable("CSHARPC_GENERIC_REPORT");
    private List<IMethodSymbol>? _genericLowered;
    private SharedGenerics? _sharedGenerics;

    /// <summary>Canonical forms and dictionary layouts (ADR 0026); created on first use.</summary>
    internal SharedGenerics SharedGenericsInfo => _sharedGenerics ??= new SharedGenerics(Compilation, Il);

    private void NoteGenericLowered(ISymbol symbol)
    {
        if (s_genericReport is null || symbol is not IMethodSymbol method || !IsInstantiation(method)) return;
        (_genericLowered ??= []).Add(method);
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
        if (s_genericReport is null || _genericLowered is null) return;
        var shared = SharedGenericsInfo;
        using var writer = new StreamWriter(s_genericReport, append: true);
        foreach (var method in _genericLowered)
        {
            var definition = method.OriginalDefinition.ContainingType.OriginalDefinition.ToDisplayString() + "." + method.OriginalDefinition.Name
                + (method.Arity > 0 ? "`" + method.Arity : "") + "(" + method.OriginalDefinition.Parameters.Length + ")";
            var canonical = shared.Canonicalize(method);
            var name = _functions.TryGetValue(method, out var lowered) ? Native.LLVMGetValueName(lowered.Fn) : "";
            writer.WriteLine($"{definition}\t{method.ToDisplayString()}\t{canonical.ToDisplayString()}\t{(shared.IsShared(canonical) ? "shared" : "exact")}"
                + $"\t{shared.DictionarySlots(method)}\t{name}");
        }
    }
}
