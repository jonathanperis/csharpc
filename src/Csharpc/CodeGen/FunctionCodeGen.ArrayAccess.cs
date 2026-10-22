using Csharpc.Llvm;

namespace Csharpc.CodeGen;

/// <summary>
/// Length loads and data accesses annotated with <see cref="MemoryFacts"/>, so LLVM can widen
/// induction variables and drop redundant bounds checks and length reloads.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>Addresses of fields, bounds-checked array elements and span elements (by LLVM handle).</summary>
    private readonly HashSet<nint> _dataAddresses = [];

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

    /// <summary>
    /// Marks an address as managed data (a field, an array element or a span element), which can
    /// never be an array's or a string's length; <see cref="LoadValue"/>/<see cref="StoreValue"/> tag
    /// their accesses accordingly.
    /// </summary>
    internal LLVMValueRef DataAddress(LLVMValueRef address)
    {
        _dataAddresses.Add(address.Handle);
        return address;
    }

    private void TagDataAccess(LLVMValueRef access, LLVMValueRef address)
    {
        if (_dataAddresses.Contains(address.Handle)) _m.Facts.DataAccess(access);
    }
}
