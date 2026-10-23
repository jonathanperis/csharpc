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
    /// <summary>Links several runtime bitcode files into one (cached by Toolchain).</summary>
    public static void Merge(IReadOnlyList<string> bitcodeFiles, string output)
    {
        using var context = new LlvmContext();
        using var module = new LlvmModule(context, "csrt");
        LinkFiles(module, context, bitcodeFiles);
        module.WriteBitcode(output);
    }

    public static void LinkInto(LlvmModule module, LlvmContext context, IEnumerable<string> bitcodeFiles)
    {
        LinkFiles(module, context, bitcodeFiles);
        Internalize(module);
    }

    private static void LinkFiles(LlvmModule module, LlvmContext context, IEnumerable<string> bitcodeFiles)
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
    }

    /// <summary>Closed world: only the process entry point needs external linkage — and the runtime's
    /// libm gap fillers (win32.c), which the backend may still call as libcalls after optimization.</summary>
    private static void Internalize(LlvmModule module)
    {
        for (var fn = LLVMGetFirstFunction(module.Ref); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            if (ShouldInternalize(fn) && NameOf(fn) is not ("main" or "roundeven" or "roundevenf"))
                LLVMSetLinkage(fn, LLVMLinkage.Internal);
        for (var global = LLVMGetFirstGlobal(module.Ref); !global.IsNull; global = LLVMGetNextGlobal(global))
            if (ShouldInternalize(global) && !NameOf(global).StartsWith("llvm.", StringComparison.Ordinal))
                LLVMSetLinkage(global, LLVMLinkage.Internal);
    }

    // Weak definitions too: csrt's fallbacks for corelib exports (ADR 0008) are final once linked, and
    // must be internal for GlobalDCE to drop them when nothing calls them.
    private static bool ShouldInternalize(LLVMValueRef value) =>
        LLVMIsDeclaration(value) == 0 && LLVMGetLinkage(value) is LLVMLinkage.External or LLVMLinkage.WeakAny;

    private static string NameOf(LLVMValueRef value) =>
        Marshal.PtrToStringUTF8(LLVMGetValueName2(value, out var length), (int)length) ?? "";
}
