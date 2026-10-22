using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Index / System.Range (`^1`, `1..^1`). Index uses .NET's own encoding — a single int,
/// `n` from the start or `~n` from the end — and Range is { Index start, Index end }. Indexing
/// arrays, spans and strings with them resolves against the length (docs/03-lowering.md#ranges).
/// </summary>
public sealed partial class FunctionCodeGen
{
    private LLVMTypeRef RangeType => L.Struct(L.I32, L.I32);

    private static bool IsIndexType(ITypeSymbol? type) => TypeMapper.IsSystemStruct(type, "Index");
    private static bool IsRangeType(ITypeSymbol? type) => TypeMapper.IsSystemStruct(type, "Range");

    private void CheckNonNegative(LLVMValueRef value) =>
        ThrowLibraryExceptionIf(_b.ICmp(LLVMIntPredicate.SLT, value, L.ConstInt(value.Type, 0)),
            "System.ArgumentOutOfRangeException", "Non-negative number required. (Parameter 'value')");

    /// <summary>`int → Index` (Index.FromStart).</summary>
    private LLVMValueRef? TryIndexConversion(IConversionOperation c)
    {
        if (!IsIndexType(c.Type) || c.Operand.Type is not { SpecialType: SpecialType.System_Int32 }) return null;
        var value = EmitValue(c.Operand);
        CheckNonNegative(value);
        return value;
    }

    /// <summary>`^n` (Index.FromEnd) = ~n.</summary>
    private LLVMValueRef EmitHat(IUnaryOperation op)
    {
        var value = Coerce(EmitValue(op.Operand), op.Operand.Type, _m.Compilation.GetSpecialType(SpecialType.System_Int32));
        CheckNonNegative(value);
        return _b.Not(value);
    }

    private LLVMValueRef EmitRange(IRangeOperation op)
    {
        var start = op.LeftOperand is { } l ? EmitValue(l) : L.ConstInt(L.I32, 0);
        var end = op.RightOperand is { } r ? EmitValue(r) : L.ConstInt(L.I32, -1); // ^0
        return _b.InsertValue(_b.InsertValue(LlvmContext.Null(RangeType), start, 0), end, 1);
    }

    /// <summary>Index → offset for a sequence of <paramref name="length"/> (i32): from-end n is length - n.</summary>
    private LLVMValueRef ResolveIndex(LLVMValueRef index, LLVMValueRef length)
    {
        var fromEnd = _b.ICmp(LLVMIntPredicate.SLT, index, L.ConstInt(L.I32, 0));
        var offset = _b.BinOp(LLVMOpcode.Add, _b.BinOp(LLVMOpcode.Add, length, index), L.ConstInt(L.I32, 1)); // length - ~index
        return _b.Select(fromEnd, offset, index);
    }

    /// <summary>Range → (start, count), with .NET's ArgumentOutOfRange check.</summary>
    private (LLVMValueRef Start, LLVMValueRef Count) ResolveRange(LLVMValueRef range, LLVMValueRef length)
    {
        var start = ResolveIndex(_b.ExtractValue(range, 0), length);
        var end = ResolveIndex(_b.ExtractValue(range, 1), length);
        var bad = _b.BinOp(LLVMOpcode.Or, _b.ICmp(LLVMIntPredicate.UGT, end, length), _b.ICmp(LLVMIntPredicate.UGT, start, end));
        ThrowArgumentOutOfRangeIf(bad);
        return (start, _b.BinOp(LLVMOpcode.Sub, end, start));
    }

    private LLVMValueRef StringLength(LLVMValueRef str)
    {
        NullCheck(str);
        return _b.Load(L.I32, _b.StructGep(T.StringHeader, str, 1), "strlen");
    }

    /// <summary>`s[^1]`, `s[1..^1]` on spans and strings (Roslyn's "implicit Index/Range indexer").</summary>
    private LLVMValueRef EmitImplicitIndexer(IImplicitIndexerReferenceOperation op)
    {
        var instanceType = op.Instance.Type;
        if (TypeMapper.IsSpan(instanceType) && IsIndexType(op.Argument.Type))
            return LoadValue(ImplicitIndexerAddress(op), op.Type!);
        if (TypeMapper.IsSpan(instanceType) && IsRangeType(op.Argument.Type))
        {
            var span = EmitValue(op.Instance);
            var (start, count) = ResolveRange(EmitValue(op.Argument), SpanLength(span));
            var element = SpanElement(instanceType!);
            return MakeSpan(_b.InBoundsGep(T.Storage(element), SpanPointer(span), [_b.Cast(LLVMOpcode.ZExt, start, L.I64)]), count);
        }
        if (instanceType?.SpecialType == SpecialType.System_String)
        {
            var str = EmitValue(op.Instance);
            var length = StringLength(str);
            if (IsIndexType(op.Argument.Type))
                return CallRuntime("csrt_string_char_at", L.I16, [L.Ptr, L.I32], str, ResolveIndex(EmitValue(op.Argument), length));
            var (start, count) = ResolveRange(EmitValue(op.Argument), length);
            return CallRuntime("csrt_string_substring", L.Ptr, [L.Ptr, L.I32, L.I32], str, start, count);
        }
        throw CompileError.Unsupported($"Index/Range indexer on '{instanceType}'", op);
    }

    private LLVMValueRef ImplicitIndexerAddress(IImplicitIndexerReferenceOperation op)
    {
        if (!TypeMapper.IsSpan(op.Instance.Type) || !IsIndexType(op.Argument.Type))
            throw CompileError.Unsupported($"assignment through an Index/Range indexer on '{op.Instance.Type}'", op);
        var span = EmitValue(op.Instance);
        var index = ResolveIndex(EmitValue(op.Argument), SpanLength(span));
        return SpanElementAddress(span, SpanElement(op.Instance.Type!), index);
    }

    /// <summary>`array[^n]`: resolve against the array length.</summary>
    private LLVMValueRef ArrayIndexFromIndex(LLVMValueRef array, LLVMValueRef index)
    {
        NullCheck(array);
        var length = _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I64, _b.StructGep(T.ArrayHeader(L.I8), array, 1), "len"), L.I32);
        return ResolveIndex(index, length);
    }

    /// <summary>`array[a..b]` = RuntimeHelpers.GetSubArray: a new array with a copy of the window.</summary>
    private LLVMValueRef EmitArrayRange(IArrayElementReferenceOperation op)
    {
        var element = ((IArrayTypeSymbol)op.ArrayReference.Type!).ElementType;
        var array = EmitValue(op.ArrayReference);
        NullCheck(array);
        var header = T.ArrayHeader(T.Storage(element));
        var length = _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I64, _b.StructGep(header, array, 1), "len"), L.I32);
        var (start, count) = ResolveRange(EmitValue(op.Indices[0]), length);
        var data = _b.InBoundsGep(header, array, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), _b.Cast(LLVMOpcode.ZExt, start, L.I64)]);
        return CallRuntime("csrt_span_to_array", L.Ptr, [L.Ptr, L.I32, L.I64], data, count, ElementSize(element));
    }

    /// <summary>Index.Value / IsFromEnd / GetOffset, Range.Start / End.</summary>
    private bool TryIndexRangeMember(IOperation op, out LLVMValueRef result)
    {
        result = default;
        switch (op)
        {
            case IPropertyReferenceOperation { Property.ContainingType: var owner, Property.Name: var name, Instance: { } instance } when IsIndexType(owner):
            {
                var index = EmitValue(instance);
                var fromEnd = _b.ICmp(LLVMIntPredicate.SLT, index, L.ConstInt(L.I32, 0));
                result = name switch
                {
                    "IsFromEnd" => fromEnd,
                    "Value" => _b.Select(fromEnd, _b.Not(index), index),
                    _ => throw CompileError.Unsupported($"Index.{name}", op),
                };
                return true;
            }
            case IPropertyReferenceOperation { Property.ContainingType: var owner, Property.Name: var name, Instance: { } instance }
                when IsRangeType(owner) && name is "Start" or "End":
                result = _b.ExtractValue(EmitValue(instance), name == "Start" ? 0u : 1u);
                return true;
            case IInvocationOperation { TargetMethod.ContainingType: var owner, TargetMethod.Name: "GetOffset", Instance: { } instance } inv when IsIndexType(owner):
                result = ResolveIndex(EmitValue(instance), ArgValue(inv, 0));
                return true;
        }
        return false;
    }
}
