using System.Runtime.InteropServices;

namespace Csharpc.Llvm;

/// <summary>What Driver/TypeDataDetach.cs and Driver/ParallelOptimize.cs need beyond the other bindings.</summary>
internal static unsafe partial class Native
{
    [LibraryImport(Lib)] public static partial int LLVMIsGlobalConstant(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial int LLVMIsThreadLocal(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetPersonalityFn(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial nint LLVMGetSection(LLVMValueRef global);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMSetSection(LLVMValueRef global, string section);
}
