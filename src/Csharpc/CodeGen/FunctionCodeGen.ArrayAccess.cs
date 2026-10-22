using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Length loads and data accesses annotated with <see cref="MemoryFacts"/>, so LLVM can widen
/// induction variables and drop redundant bounds checks and length reloads.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>Addresses of fields, bounds-checked array elements and span elements (by LLVM handle).</summary>
    private readonly Dictionary<nint, (DataKind Kind, IFieldSymbol? Field)> _dataAddresses = [];

    /// <summary>T[].Length (i64; the total element count for any rank) of a non-null array.</summary>
    internal LLVMValueRef LoadArrayLength(LLVMValueRef array)
    {
        var load = _b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.I8), array, 1), "len");
        _m.Facts.ArrayLengthLoad(load);
        return load;
    }

    /// <summary>string.Length (i32) of a non-null string.</summary>
    internal LLVMValueRef LoadStringLength(LLVMValueRef str)
    {
        var load = _b.Load(L.I32, _b.StructGep(T.StringHeader, str, 1), "strlen");
        _m.Facts.StringLengthLoad(load);
        return load;
    }

    /// <summary>The char at a (checked) index of a non-null string.</summary>
    internal LLVMValueRef LoadStringChar(LLVMValueRef str, LLVMValueRef index)
    {
        var load = _b.Load(L.I16, _b.InBoundsGep(T.StringHeader, str, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), index]));
        _m.Facts.StringCharLoad(load);
        return load;
    }

    /// <summary>
    /// string[int], inline rather than a csrt call: the C helper's loads carry clang's TBAA tags,
    /// which alias everything of ours, so its length load couldn't be hoisted out of a loop that stores.
    /// </summary>
    internal LLVMValueRef StringCharAt(LLVMValueRef str, LLVMValueRef index)
    {
        NullCheck(str);
        FailIf(_b.ICmp(LLVMIntPredicate.UGE, index, LoadStringLength(str)), "csrt_throw_index_out_of_range");
        return LoadStringChar(str, _b.Cast(LLVMOpcode.ZExt, index, L.I64));
    }

    /// <summary>
    /// Marks an address as managed data (a field, an array element or a span element), which can
    /// never be an array's or a string's length; <see cref="LoadValue"/>/<see cref="StoreValue"/> tag
    /// their accesses accordingly.
    /// </summary>
    internal LLVMValueRef DataAddress(LLVMValueRef address, DataKind kind, IFieldSymbol? field = null)
    {
        _dataAddresses[address.Handle] = (kind, field?.OriginalDefinition);
        return address;
    }

    private void TagDataAccess(LLVMValueRef access, LLVMValueRef address)
    {
        if (_dataAddresses.TryGetValue(address.Handle, out var data)) _m.Facts.DataAccess(access, data.Kind, data.Field);
    }
}
