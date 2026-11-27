using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Virtual members of a boxed System.Index / System.Range (ModuleCodeGen.IndexRangeMethodTable); the payload sits at offset 8 of the box.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private LLVMValueRef BoxedPayload(LLVMValueRef box, ITypeSymbol type) => LoadValue(_b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]), type);

    internal void EmitIndexRangeBoxToString(ITypeSymbol type)
    {
        _b.Ret(FormatValue(BoxedPayload(_fn.Fn.Param(0), type), type, null!));
        Finish();
    }

    internal void EmitIndexRangeBoxEquals(ITypeSymbol type, LLVMValueRef mt)
    {
        var (self, other) = (_fn.Fn.Param(0), _fn.Fn.Param(1));
        var value = BoxedPayload(self, type);
        _b.Ret(ShortCircuit(isAnd: true,
        [
            () => _b.ICmp(LLVMIntPredicate.NE, other, LlvmContext.Null(L.Ptr)),
            () => _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, other), mt),
            () => FieldEquals(value, BoxedPayload(other, type), type),
        ]));
        Finish();
    }

    internal void EmitIndexRangeBoxHash(ITypeSymbol type)
    {
        _b.Ret(FieldHash(BoxedPayload(_fn.Fn.Param(0), type), type));
        Finish();
    }
}
