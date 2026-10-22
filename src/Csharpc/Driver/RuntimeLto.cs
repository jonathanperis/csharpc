using System.Runtime.InteropServices;
using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Driver;

/// <summary>
/// Whole-program LTO with csrt: the runtime's bitcode is linked into the program module before the
/// optimization pipeline runs, then everything except <c>main</c> is internalized. With the
/// allocator visible, <c>new T()</c> becomes an inlined size-class fast path whose size class and
/// zeroing are constant-folded per call site — the same effect NativeAOT gets from its
/// hand-written allocation helpers, but derived by LLVM from portable C.
/// </summary>
public static class RuntimeLto
{
    public static void LinkInto(LlvmModule module, LlvmContext context, IEnumerable<string> bitcodeFiles)
    {
        foreach (var path in bitcodeFiles)
        {
            if (LLVMCreateMemoryBufferWithContentsOfFile(path, out var buffer, out var message) != 0)
                throw new InvalidOperationException($"cannot read runtime bitcode '{path}': {TakeMessage(message)}");
            try
            {
                if (LLVMParseBitcodeInContext2(context.Ref, buffer, out var runtime) != 0)
                    throw new InvalidOperationException($"invalid runtime bitcode '{path}'");
                if (LLVMLinkModules2(module.Ref, runtime) != 0) // consumes `runtime`
                    throw new InvalidOperationException($"failed to link runtime bitcode '{path}'");
            }
            finally { LLVMDisposeMemoryBuffer(buffer); }
        }
        Internalize(module);
    }

    /// <summary>Closed world: only the process entry point needs external linkage.</summary>
    private static void Internalize(LlvmModule module)
    {
        for (var fn = LLVMGetFirstFunction(module.Ref); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (ShouldInternalize(fn) && NameOf(fn) != "main")
                LLVMSetLinkage(fn, LLVMLinkage.Internal);
        for (var global = LLVMGetFirstGlobal(module.Ref); !global.IsNull; global = LLVMGetNextGlobal(global))
            if (ShouldInternalize(global) && !NameOf(global).StartsWith("llvm.", StringComparison.Ordinal))
                LLVMSetLinkage(global, LLVMLinkage.Internal);
    }

    private static bool ShouldInternalize(LLVMValueRef value) =>
        LLVMIsDeclaration(value) == 0 && LLVMGetLinkage(value) == LLVMLinkage.External;

    private static string NameOf(LLVMValueRef value) =>
        Marshal.PtrToStringUTF8(LLVMGetValueName2(value, out var length), (int)length) ?? "";
}
