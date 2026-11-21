using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// double/float.ConvertToIntegerNative for int and long: the target's own truncating conversion, which is what .NET means by
/// "native" and what NativeAOT emits (#351: Random's legacy generator scales a double and converts it on every call). In range all
/// targets truncate toward zero; outside it the instruction decides, and so does csharpc: x64's cvttsd2si / cvttss2si give the
/// integer minimum for NaN, infinities and out-of-range values (the LLVM x86 intrinsics, since `fptosi` would be poison there), and
/// Arm64's fcvtzs saturates with NaN as 0, which is LLVM's `fptosi.sat`. The other integer types, and int and long on the other targets,
/// get the exact in-range truncation and an explicit "not supported" error outside the range (the instruction's result is not defined
/// there). The members' CoreLib IL calls itself for a primitive TInteger (RyuJIT expands it), so csharpc lowers them (the IL is not
/// copied: the slice would call its own copy); a non-primitive TInteger (Int128, a user type) takes the IL's other path,
/// TInteger.CreateSaturating (corelib NativeIntegerConversionChecks).
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>"x86" or "arm64" when the target has the instruction ConvertToIntegerNative means; null otherwise.</summary>
    private string? NativeIntegerConversionArch()
    {
        var triple = _m.Types.Target.Triple;
        if (triple.StartsWith("x86_64", StringComparison.Ordinal)) return "x86";
        if (triple.StartsWith("aarch64", StringComparison.Ordinal) || triple.StartsWith("arm64", StringComparison.Ordinal)) return "arm64";
        return null;
    }

    private LLVMValueRef NativeIntegerConversion(LLVMValueRef value, LLVMTypeRef result)
    {
        var isDouble = value.Type.Kind == LLVMTypeKind.Double;
        var wide = result.IntWidth == 64;
        if (NativeIntegerConversionArch() == "x86")
        {
            // cvttsd2si / cvttss2si take the low lane of an SSE vector.
            var vector = Native.LLVMVectorType(value.Type, isDouble ? 2u : 4u);
            var lane = _b.InsertElement(Native.LLVMGetPoison(vector), value, L.ConstInt(L.I32, 0));
            var name = (isDouble ? "llvm.x86.sse2.cvttsd2si" : "llvm.x86.sse.cvttss2si") + (wide ? "64" : "");
            var convert = _m.Intrinsic(name, result, vector);
            return _b.Call(convert.Type, convert.Fn, [lane]);
        }
        var saturating = _m.Intrinsic($"llvm.fptosi.sat.i{(wide ? 64 : 32)}.{(isDouble ? "f64" : "f32")}", result, value.Type);
        return _b.Call(saturating.Type, saturating.Fn, [value]);
    }

    private static bool IsPrimitiveInteger(ITypeSymbol type) =>
        type.SpecialType is >= SpecialType.System_SByte and <= SpecialType.System_UInt64 or SpecialType.System_Char
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    /// <summary>double/float.ConvertToIntegerNative&lt;TInteger&gt;(value), see the type's summary.</summary>
    internal LLVMValueRef ConvertToIntegerNative(IInvocationOperation inv, LLVMValueRef value)
    {
        var target = inv.TargetMethod;
        var integer = S(target.TypeArguments[0]);
        var isDouble = value.Type.Kind == LLVMTypeKind.Double;
        if (!IsPrimitiveInteger(integer))
            return CallCoreLibGenericMethod("NativeIntegerConversionChecks", isDouble ? "SaturatingDouble" : "SaturatingSingle", [integer], value);
        var result = T.Value(integer);
        var bits = (int)result.IntWidth;
        if (bits is 32 or 64 && integer.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 && NativeIntegerConversionArch() is not null)
            return NativeIntegerConversion(value, result);
        var wide = isDouble ? value : _b.Cast(LLVMOpcode.FPExt, value, L.F64); // exact
        var signed = TypeMapper.IsSigned(integer);
        // In range when the truncation toward zero fits: (MinValue - 1, MaxValue + 1), every bound a double exactly (2^63 and 2^64 are
        // powers of two; for 64-bit signed integers MinValue itself is the closed bound). NaN compares false.
        var (low, lowInclusive) = !signed ? (-1.0, false) : bits == 64 ? (-9223372036854775808.0, true) : (-Math.Pow(2, bits - 1) - 1, false);
        var high = Math.Pow(2, signed ? bits - 1 : bits);
        var inRange = _b.BinOp(LLVMOpcode.And,
            _b.FCmp(lowInclusive ? LLVMRealPredicate.OGE : LLVMRealPredicate.OGT, wide, LlvmContext.ConstReal(L.F64, low)),
            _b.FCmp(LLVMRealPredicate.OLT, wide, LlvmContext.ConstReal(L.F64, high)));
        var fail = _b.AppendBlock(_fn.Fn, "native.range");
        var ok = _b.AppendBlock(_fn.Fn, "native.ok");
        _b.CondBr(inRange, ok, fail);
        _b.PositionAtEnd(fail);
        CallCoreLib("NativeIntegerConversionChecks", isDouble ? "ThrowDouble" : "ThrowSingle");
        _b.Unreachable();
        _b.PositionAtEnd(ok);
        return _b.Cast(signed ? LLVMOpcode.FPToSI : LLVMOpcode.FPToUI, wide, result);
    }
}
