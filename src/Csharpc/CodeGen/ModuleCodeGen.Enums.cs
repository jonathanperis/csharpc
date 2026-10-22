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

    private readonly Dictionary<INamedTypeSymbol, LoweredFunction> _enumFormatWith = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// string EnumFormat(T value, string format): Enum.ToString(format) and "{e:X}". "G" (or no format)
    /// is the name formatter (EnumFormatter); D/X/F go through csrt_enum_format.
    /// </summary>
    public LoweredFunction EnumFormatWith(INamedTypeSymbol enumType)
    {
        enumType = enumType.OriginalDefinition;
        if (_enumFormatWith.TryGetValue(enumType, out var existing)) return existing;
        var underlying = enumType.EnumUnderlyingType!;
        var valueType = Types.Value(underlying);
        var signed = TypeMapper.IsSigned(underlying);
        var type = Llvm.FunctionType(Llvm.Ptr, valueType, Llvm.Ptr);
        var fn = Module.AddFunction(Mangler.TypeSymbol(enumType, "ToStringFormat"), type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = _enumFormatWith[enumType] = new LoweredFunction(fn, type);

        var (values, names, count) = EnumTable(enumType);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var value = fn.Param(0);
        var asU64 = valueType.IntWidth == 64 ? value : b.Cast(LLVMOpcode.ZExt, value, Llvm.I64);
        var asI64 = valueType.IntWidth == 64 ? value : b.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, Llvm.I64);
        var format = Runtime("csrt_enum_format", Llvm.Ptr, Llvm.I64, Llvm.I64, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.I1);
        var formatted = b.Call(format.Type, format.Fn, [asU64, asI64, fn.Param(1), values, names,
            Llvm.ConstInt(Llvm.I32, count), Llvm.ConstInt(Llvm.I32, valueType.IntWidth), signed ? Llvm.True : Llvm.False]);
        var byName = b.AppendBlock(fn, "general");
        var done = b.AppendBlock(fn, "done");
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, formatted, LlvmContext.Null(Llvm.Ptr)), byName, done);
        b.PositionAtEnd(done);
        b.Ret(formatted);
        b.PositionAtEnd(byName);
        var general = EnumFormatter(enumType);
        b.Ret(b.Call(general.Type, general.Fn, [value]));
        return lowered;
    }
    /// <summary>Enum.GetHashCode(): the underlying integer's GetHashCode (small types widen, 64-bit folds lo ^ hi).</summary>
    public LLVMValueRef EnumHash(IrBuilder b, LLVMValueRef value, INamedTypeSymbol enumType)
    {
        var underlying = enumType.EnumUnderlyingType!;
        var width = Types.Value(underlying).IntWidth;
        if (width < 32) return b.Cast(TypeMapper.IsSigned(underlying) ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, Llvm.I32);
        if (width == 32) return value;
        var high = b.Cast(LLVMOpcode.Trunc, b.BinOp(LLVMOpcode.LShr, value, Llvm.ConstInt(Llvm.I64, 32)), Llvm.I32);
        return b.BinOp(LLVMOpcode.Xor, b.Cast(LLVMOpcode.Trunc, value, Llvm.I32), high);
    }
}
