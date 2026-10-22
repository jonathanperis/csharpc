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
        var rank = arrayType.Rank;
        var element = arrayType.ElementType;
        var storage = T.Storage(element);
        var lengths = creation.DimensionSizes.Select(d => ToIndex(EmitValue(d), TypeOf(d)!)).ToArray();
        LLVMValueRef total = L.ConstInt(L.I64, 1);
        foreach (var length in lengths)
        {
            FailIf(_b.ICmp(LLVMIntPredicate.SLT, length, L.ConstInt(L.I64, 0)), "csrt_throw_overflow");
            total = _b.BinOp(LLVMOpcode.Mul, total, length);
        }
        var headerSize = 16 + 8L * rank;
        var size = _b.BinOp(LLVMOpcode.Add, L.ConstInt(L.I64, headerSize),
            _b.BinOp(LLVMOpcode.Mul, total, L.ConstInt(L.I64, (long)T.Target.SizeOf(storage))));
        var array = Allocate(_m.ExternalGlobal("csrt_Array_MT"), size);
        var header = MdArrayHeader(storage, rank);
        _b.Store(total, _b.StructGep(header, array, 1));
        for (var d = 0; d < rank; d++)
            _b.Store(lengths[d], _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I32, d)]));

        if (creation.Initializer is { } init)
        {
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

    private LLVMValueRef MdElementAddress(IArrayElementReferenceOperation element, IArrayTypeSymbol arrayType)
    {
        var rank = arrayType.Rank;
        var array = EmitValue(element.ArrayReference);
        var indices = element.Indices.Select(i => ToIndex(EmitValue(i), TypeOf(i)!)).ToArray();
        NullCheck(array);
        var header = MdArrayHeader(T.Storage(arrayType.ElementType), rank);
        LLVMValueRef linear = L.ConstInt(L.I64, 0);
        for (var d = 0; d < rank; d++)
        {
            var length = _b.Load(L.I64, _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I32, d)]), "dim");
            FailIf(_b.ICmp(LLVMIntPredicate.UGE, indices[d], length), "csrt_throw_index_out_of_range");
            linear = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Mul, linear, length), indices[d]);
        }
        return _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 3), linear]);
    }

    /// <summary>array.GetLength(d) / GetUpperBound(d) for any rank (T[] has rank 1, length at offset 8).</summary>
    internal LLVMValueRef ArrayDimensionLength(LLVMValueRef array, ITypeSymbol arrayType, LLVMValueRef dimension)
    {
        NullCheck(array);
        if (arrayType is not IArrayTypeSymbol { Rank: > 1 } md) return ArrayLength(array); // dimension must be 0
        var header = MdArrayHeader(L.I8, md.Rank);
        var d = ToIndex(dimension, _m.Compilation.GetSpecialType(SpecialType.System_Int32));
        FailIf(_b.ICmp(LLVMIntPredicate.UGE, d, L.ConstInt(L.I64, md.Rank)), "csrt_throw_index_out_of_range");
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
