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

    /// <summary>The dictionary slot of a shared generic class (ADR 0026, step 2c): appended to its own vtable section.</summary>
    private static void AddDictionarySlot(ClassLayout layout, INamedTypeSymbol type)
    {
        layout.DictionarySlots[type.OriginalDefinition] = layout.VTable.Count;
        layout.VTable.Add(null);
    }

    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _genericDictionaries = new(SymbolEqualityComparer.Default);

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
            for (var owner = layout; owner is not null; owner = owner.Base)
                if (SymbolEqualityComparer.Default.Equals(owner.Type.OriginalDefinition, definition)) return GenericDictionary(owner);
        }
        return null;
    }

    /// <summary>
    /// One constant per exact instantiation of a shared generic class: [its own method table, then one slot per type-parameter-dependent
    /// symbol the canonical class's instance methods name]. Only slot 0 is filled for now; the others are laid out and stay null until
    /// step 3 reads them, so filling them cannot pull in code nothing uses.
    /// </summary>
    private LLVMValueRef GenericDictionary(ClassLayout exact)
    {
        if (_genericDictionaries.TryGetValue(exact.Type, out var existing)) return existing;
        var count = 1 + SharedGenericsInfo.ClassDictionarySlots(exact.Type.OriginalDefinition);
        var entries = new LLVMValueRef[count];
        entries[0] = exact.MethodTable;
        for (var i = 1; i < count; i++) entries[i] = LlvmContext.Null(Llvm.Ptr);
        var init = LlvmContext.ConstArray(Llvm.Ptr, entries);
        var global = Module.AddGlobal(Mangler.TypeSymbol(exact.Type, "GDICT"), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        return _genericDictionaries[exact.Type] = global;
    }
}
