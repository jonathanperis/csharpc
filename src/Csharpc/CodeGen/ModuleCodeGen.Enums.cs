using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, (LLVMValueRef Values, LLVMValueRef Names, int Count)> _enumTables = new(SymbolEqualityComparer.Default);

    /// <summary>Members as (raw unsigned value within the underlying width, name), stably sorted by value.</summary>
    public List<(ulong Raw, string Name)> EnumMembers(INamedTypeSymbol enumType)
    {
        var width = (int)Types.Value(enumType.EnumUnderlyingType!).IntWidth;
        return [.. enumType.OriginalDefinition.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
            .Select(f => (Raw: f.ConstantValue switch { ulong u => u, uint u => u, ushort u => u, byte u => u, var v => unchecked((ulong)Convert.ToInt64(v)) }, f.Name))
            .Select(m => (Raw: width == 64 ? m.Raw : m.Raw & ((1UL << width) - 1), m.Name))
            .OrderBy(m => m.Raw)];
    }

    /// <summary>
    /// All members of an enum (aliases included), stably sorted by unsigned value like .NET's
    /// Enum.GetValues/GetNames: { u64 values[], string names[] } constant tables.
    /// </summary>
    public (LLVMValueRef Values, LLVMValueRef Names, int Count) EnumTable(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumTables.TryGetValue(enumType, out var existing)) return existing;
        var members = EnumMembers(enumType);
        var valuesInit = LlvmContext.ConstArray(Llvm.I64, [.. members.Select(m => Llvm.ConstUInt(Llvm.I64, m.Raw))]);
        var namesInit = LlvmContext.ConstArray(Llvm.Ptr, [.. members.Select(m => StringLiteral(m.Name))]);
        var values = Module.AddGlobal(Mangler.TypeSymbol(enumType, "EnumValues"), Native.LLVMTypeOf(valuesInit), valuesInit, constant: true, linkage: LLVMLinkage.Internal);
        var names = Module.AddGlobal(Mangler.TypeSymbol(enumType, "EnumNames"), Native.LLVMTypeOf(namesInit), namesInit, constant: true, linkage: LLVMLinkage.Internal);
        return _enumTables[enumType] = (values, names, members.Count);
    }
}
