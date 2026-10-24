using System.Runtime.InteropServices;

namespace Csharpc.Llvm;

/// <summary>
/// What parallel code generation (Driver/ParallelCodegen.cs) needs: visibility and
/// comdat edits, in-memory bitcode, module-level inline asm, aliases and ifuncs.
/// </summary>
internal static unsafe partial class Native
{
    public const int HiddenVisibility = 1; // LLVMHiddenVisibility

    [LibraryImport(Lib)] public static partial void LLVMSetVisibility(LLVMValueRef global, int visibility);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetInitializer(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial void LLVMSetComdat(LLVMValueRef global, nint comdat);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAGlobalValue(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetFirstGlobalAlias(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetFirstGlobalIFunc(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial void LLVMSetModuleInlineAsm2(LLVMModuleRef m, byte* asm, nuint length);
    [LibraryImport(Lib)] public static partial LLVMMemoryBufferRef LLVMWriteBitcodeToMemoryBuffer(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial byte* LLVMGetBufferStart(LLVMMemoryBufferRef buffer);
    [LibraryImport(Lib)] public static partial nuint LLVMGetBufferSize(LLVMMemoryBufferRef buffer);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMemoryBufferRef LLVMCreateMemoryBufferWithMemoryRange(byte* data, nuint length, string name, int requiresNullTerminator);
}
