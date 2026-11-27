using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// CSHARPC_GENERIC_REPORT=&lt;file&gt;: one tab-separated line per lowered function that is a generic instantiation (its own type arguments or a
/// containing type's): its definition, its exact instantiation, the canonical form shared generic code would compile it as (every
/// reference-type type argument replaced by its positional marker, value types kept: ADR 0026), whether that form is shared code, the number of
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
    internal SharedGenerics SharedGenericsInfo => _sharedGenerics ??= new SharedGenerics(Compilation, Il, CoreLib.Map);

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
            // (with --shared-generics: whether step 3 compiles it as its canonical form, and why not)
            var sharing = !SharedGenerics ? "" : shared.ShareAs(method) is not null ? "\tcompiled shared" : $"\tcompiled exact: {shared.NotSharedReason(method) ?? shared.ExactReason(method.OriginalDefinition) ?? "signature"}";
            writer.WriteLine($"{definition}\t{method.ToDisplayString()}\t{canonical.ToDisplayString()}\t{(shared.IsShared(canonical) ? "shared" : "exact")}"
                + $"\t{shared.DictionarySlots(method)}\t{name}{sharing}");
        }
    }

    /// <summary>The dictionary slot of a shared generic class (ADR 0026, step 2c): appended to its own vtable section.</summary>
    private static void AddDictionarySlot(ClassLayout layout, INamedTypeSymbol type)
    {
        layout.DictionarySlots[type.OriginalDefinition] = layout.VTable.Count;
        layout.VTable.Add(null);
    }

    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _genericDictionaries = new(SymbolEqualityComparer.Default);

    /// <summary>Generic class definitions with at least one method compiled as its canonical form (the ones whose dictionaries are read).</summary>
    private readonly HashSet<INamedTypeSymbol> _classesWithSharedCode = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// The generic dictionary a method table's slot <paramref name="slot"/> holds (ADR 0026, step 2d), or null when the slot is not a
    /// dictionary slot: the dictionary of the exact instantiation, in this class's hierarchy, of the generic class that owns the slot.
    /// </summary>
    private LLVMValueRef? DictionaryForSlot(ClassLayout layout, int slot)
    {
        if (layout.DictionarySlots.Count == 0) return null;
        foreach (var (definition, index) in layout.DictionarySlots)
        {
            if (index != slot) continue;
            // Only a class whose shared code asked its dictionary for a handle reads it: the others keep the slot null and emit none
            // (most shared methods need no handle, and on JSON's serializer most shared classes are hot-path collections that stay exact).
            if (!_classesWithSharedCode.Contains(definition)) return null;
            for (var owner = layout; owner is not null; owner = owner.Base)
                if (SymbolEqualityComparer.Default.Equals(owner.Type.OriginalDefinition, definition))
                    return HasDictionarySlots(owner) ? GenericDictionary(owner) : null;
        }
        return null;
    }
}
