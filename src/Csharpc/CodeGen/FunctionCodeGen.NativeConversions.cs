using Csharpc.Llvm;

namespace Csharpc.CodeGen;

/// <summary>
/// double/float.ConvertToIntegerNative for int and long: the target's own truncating conversion, which is what .NET means by
/// "native" and what NativeAOT emits (#351: Random's legacy generator scales a double and converts it on every call). In range all
/// targets truncate toward zero; outside it the instruction decides, and so does csharpc: x64's cvttsd2si / cvttss2si give the
/// integer minimum for NaN, infinities and out-of-range values (the LLVM x86 intrinsics, since `fptosi` would be poison there), and
/// Arm64's fcvtzs saturates with NaN as 0, which is LLVM's `fptosi.sat`. Other targets keep the corelib's exact-in-range path.
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
}
