using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Math.Clamp / T.Clamp's bounds check (#392): .NET throws ArgumentException("'min' cannot be greater than max.") when min > max, before
/// clamping, for every numeric type. The intrinsics check first, in a cold block that calls the corelib's Math.ThrowMinMaxException
/// (which formats the two values as .NET's message does); a NaN bound compares false, as in .NET, so it never throws.
/// </summary>
public sealed partial class FunctionCodeGen
{
    internal void ClampBoundsCheck(LLVMValueRef min, LLVMValueRef max, ITypeSymbol type)
    {
        var greater = min.Type.IsFloatingPoint
            ? _b.FCmp(LLVMRealPredicate.OGT, min, max)
            : _b.ICmp(TypeMapper.IsSigned(S(type)) ? LLVMIntPredicate.SGT : LLVMIntPredicate.UGT, min, max);
        var fail = _b.AppendBlock(_fn.Fn, "clamp.minmax");
        var ok = _b.AppendBlock(_fn.Fn, "clamp.ok");
        _b.CondBr(greater, fail, ok);
        _b.PositionAtEnd(fail);
        CallCoreLibGenericMethod("MathMoreMembers", "ThrowMinMaxException", [S(type)], min, max);
        _b.Unreachable();
        _b.PositionAtEnd(ok);
    }
}
