using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Rectangular arrays (T[,], T[,,] ...): { mt, i64 total, [rank x i64] lengths, [0 x T] data }, row-major.
/// `Length` keeps working unchanged (the total is at the same offset as for T[]).
/// </summary>
public sealed partial class FunctionCodeGen
{
    private LLVMTypeRef MdArrayHeader(LLVMTypeRef element, int rank) =>
        L.Struct(L.Ptr, L.I64, LlvmContext.Array(L.I64, (ulong)rank), LlvmContext.Array(element, 0));

    private LLVMValueRef EmitMdArrayCreation(IArrayCreationOperation creation, IArrayTypeSymbol arrayType)
    {
        var element = arrayType.ElementType;
        var array = NewMdArray(arrayType, [.. creation.DimensionSizes.Select(d => ToIndex(EmitValue(d), TypeOf(d)!))]);
        if (creation.Initializer is { } init)
        {
            var header = MdArrayHeader(T.Storage(element), arrayType.Rank);
            // Nested initializers ({ {1, 2}, {3, 4} }) flatten row-major.
            var flat = new List<IOperation>();
            void Flatten(IArrayInitializerOperation i)
            {
                foreach (var v in i.ElementValues)
                    if (v is IArrayInitializerOperation nested) Flatten(nested); else flat.Add(v);
            }
            Flatten(init);
            for (var i = 0; i < flat.Count; i++)
            {
                var address = _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 3), L.ConstInt(L.I64, i)]);
                StoreValue(ToStorage(Coerce(EmitValue(flat[i]), TypeOf(flat[i]), element), element), address);
            }
        }
        return array;
    }

    /// <summary>A zeroed rectangular array with these lengths (i64, sign-extended: a negative one overflows as in .NET).</summary>
    internal LLVMValueRef NewMdArray(IArrayTypeSymbol arrayType, LLVMValueRef[] lengths)
    {
        var rank = arrayType.Rank;
        var storage = T.Storage(arrayType.ElementType);
        LLVMValueRef total = L.ConstInt(L.I64, 1);
        foreach (var length in lengths)
        {
            // Like .NET: each dimension must be in [0, int.MaxValue] (else OverflowException) and the total
            // element count within Array.MaxLength (else OutOfMemoryException). Checking the running total
            // after every step keeps the product far from i64 overflow (≤ 2^31 * 2^31), so the size passed to
            // the allocator can never wrap into an undersized block.
            FailIf(_b.ICmp(LLVMIntPredicate.UGT, length, L.ConstInt(L.I64, int.MaxValue)), "csrt_throw_overflow");
            total = _b.BinOp(LLVMOpcode.Mul, total, length);
            var tooBig = _b.ICmp(LLVMIntPredicate.UGT, total, L.ConstInt(L.I64, 0x7FFFFFC7));
            var fail = NewBlock("md.too_big");
            var ok = NewBlock("md.ok");
            _b.CondBr(tooBig, fail, ok);
            _b.PositionAtEnd(fail);
            CallRuntime("csrt_throw_out_of_memory", L.Void, [L.Ptr], _m.CString("Array dimensions exceeded supported range."));
            _b.Unreachable();
            _b.PositionAtEnd(ok);
        }
        var headerSize = 16 + 8L * rank;
        var size = _b.BinOp(LLVMOpcode.Add, L.ConstInt(L.I64, headerSize),
            _b.BinOp(LLVMOpcode.Mul, total, L.ConstInt(L.I64, (long)T.Target.SizeOf(storage))));
        var array = Allocate(_m.MdArrayMethodTable(arrayType), size);
        var header = MdArrayHeader(storage, rank);
        _b.Store(total, _b.StructGep(header, array, 1));
        for (var d = 0; d < rank; d++)
            _b.Store(lengths[d], _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I32, d)]));
        return array;
    }

    private LLVMValueRef MdElementAddress(IArrayElementReferenceOperation element, IArrayTypeSymbol arrayType)
    {
        var array = EmitValue(element.ArrayReference);
        return MdElementAddress(array, arrayType, [.. element.Indices.Select(i => ToIndex(EmitValue(i), TypeOf(i)!))]);
    }

    /// <summary>The element at these (i64) indices, bounds-checked per dimension (IndexOutOfRangeException).</summary>
    internal LLVMValueRef MdElementAddress(LLVMValueRef array, IArrayTypeSymbol arrayType, LLVMValueRef[] indices)
    {
        var rank = arrayType.Rank;
        NullCheck(array);
        var header = MdArrayHeader(T.Storage(arrayType.ElementType), rank);
        LLVMValueRef linear = L.ConstInt(L.I64, 0);
        for (var d = 0; d < rank; d++)
        {
            var length = _b.Load(L.I64, _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I32, d)]), "dim");
            FailIf(_b.ICmp(LLVMIntPredicate.UGE, indices[d], length), "csrt_throw_index_out_of_range");
            linear = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, linear, length), indices[d]);
        }
        return DataAddress(_b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 3), linear]), DataKind.ArrayElement);
    }

    /// <summary>array.GetLength(d) / GetUpperBound(d) for any rank (T[] has rank 1, length at offset 8).</summary>
    internal LLVMValueRef ArrayDimensionLength(LLVMValueRef array, ITypeSymbol arrayType, LLVMValueRef dimension)
    {
        NullCheck(array);
        // An `Array` of unknown rank: the runtime reads the rank from the method table.
        if (arrayType is not IArrayTypeSymbol)
            return CallRuntime("csrt_array_dimension_length", L.I64, [L.Ptr, L.I32], array, _b.Cast(LLVMOpcode.Trunc, ToIndex(dimension, _m.Compilation.GetSpecialType(SpecialType.System_Int32)), L.I32));
        if (arrayType is not IArrayTypeSymbol { Rank: > 1 } md)
        {
            // T[]: dimension 0 only, like .NET (IndexOutOfRangeException otherwise).
            var d0 = ToIndex(dimension, _m.Compilation.GetSpecialType(SpecialType.System_Int32));
            FailIf(_b.ICmp(LLVMIntPredicate.NE, d0, L.ConstInt(L.I64, 0)), "csrt_throw_rank_out_of_range");
            return ArrayLength(array);
        }
        var header = MdArrayHeader(L.I8, md.Rank);
        var d = ToIndex(dimension, _m.Compilation.GetSpecialType(SpecialType.System_Int32));
        FailIf(_b.ICmp(LLVMIntPredicate.UGE, d, L.ConstInt(L.I64, md.Rank)), "csrt_throw_rank_out_of_range");
        return _b.Load(L.I64, _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), d]), "dim");
    }

    /// <summary>Address of element 0 (row-major data start) for an array of any rank.</summary>
    private LLVMValueRef ArrayDataStart(LLVMValueRef array, IArrayTypeSymbol arrayType)
    {
        var storage = T.Storage(arrayType.ElementType);
        var header = arrayType.Rank == 1 ? T.ArrayHeader(storage) : MdArrayHeader(storage, arrayType.Rank);
        return _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, arrayType.Rank == 1 ? 2 : 3), L.ConstInt(L.I64, 0)]);
    }
}
