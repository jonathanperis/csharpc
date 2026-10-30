using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Csharpc.Llvm;

// Opaque LLVM C API handles. Each one is a blittable wrapper over a pointer, so arrays of
// them marshal by pinning (no copies) through the LibraryImport source generator.
public readonly record struct LLVMContextRef(nint Handle);
public readonly record struct LLVMModuleRef(nint Handle);
public readonly record struct LLVMTypeRef(nint Handle) { public bool IsNull => Handle == 0; }
public readonly record struct LLVMValueRef(nint Handle) { public bool IsNull => Handle == 0; }
public readonly record struct LLVMBasicBlockRef(nint Handle) { public bool IsNull => Handle == 0; }
public readonly record struct LLVMBuilderRef(nint Handle);
public readonly record struct LLVMTargetRef(nint Handle);
public readonly record struct LLVMTargetMachineRef(nint Handle);
public readonly record struct LLVMTargetDataRef(nint Handle);
public readonly record struct LLVMPassBuilderOptionsRef(nint Handle);
public readonly record struct LLVMAttributeRef(nint Handle);
public readonly record struct LLVMErrorRef(nint Handle);
public readonly record struct LLVMMemoryBufferRef(nint Handle);

public enum LLVMTypeKind
{
    Void, Half, Float, Double, X86_FP80, FP128, PPC_FP128, Label, Integer, Function, Struct, Array, Pointer,
    Vector, Metadata, X86_MMX, Token, ScalableVector, BFloat, X86_AMX, TargetExt,
}

public enum LLVMIntPredicate { EQ = 32, NE, UGT, UGE, ULT, ULE, SGT, SGE, SLT, SLE }

public enum LLVMRealPredicate { False, OEQ, OGT, OGE, OLT, OLE, ONE, ORD, UNO, UEQ, UGT, UGE, ULT, ULE, UNE, True }

public enum LLVMAtomicOrdering { NotAtomic = 0, Unordered = 1, Monotonic = 2, Acquire = 4, Release = 5, AcquireRelease = 6, SequentiallyConsistent = 7 }

public enum LLVMAtomicRMWBinOp { Xchg, Add, Sub, And, Nand, Or, Xor, Max, Min, UMax, UMin, FAdd, FSub }

public enum LLVMLinkage { External = 0, LinkOnceODR = 3, WeakAny = 5, Internal = 8, Private = 9 }

public enum LLVMCodeGenOptLevel { None, Less, Default, Aggressive }
public enum LLVMRelocMode { Default, Static, PIC, DynamicNoPic }
public enum LLVMCodeModel { Default, JITDefault, Tiny, Small, Kernel, Medium, Large }
public enum LLVMCodeGenFileType { Assembly, Object }

/// <summary>
/// Hand-written P/Invoke surface over the LLVM C API (llvm-c/*.h), the same stable ABI used by
/// e.g. llvm-sys (Rust) and LLVMSharp. We only bind what the compiler actually uses.
/// </summary>
internal static unsafe partial class Native
{
    private const string Lib = "LLVM";

    static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);

    /// <summary>
    /// Probe order: $CSHARPC_LIBLLVM, a release bundle's lib/ next to the executable, then versioned
    /// sonames (newest supported first). Release archives ship libLLVM under lib/ (Apache-2.0 with
    /// LLVM exceptions); clang/lld stay a system prerequisite (see README Install).
    /// </summary>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) return 0;
        var dir = AppContext.BaseDirectory;
        var lib = Path.Combine(dir, "lib");
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("CSHARPC_LIBLLVM"),
            // Bundled next to the NativeAOT / self-contained release layout.
            Path.Combine(lib, "libLLVM-18.so"), Path.Combine(lib, "libLLVM.so.18.1"),
            Path.Combine(lib, "libLLVM.dylib"), Path.Combine(lib, "libLLVM-18.dll"),
            Path.Combine(dir, "libLLVM-18.so"), Path.Combine(dir, "libLLVM.dylib"), Path.Combine(dir, "libLLVM-18.dll"),
            "libLLVM-18.so", "libLLVM.so.18.1", "/usr/lib/llvm-18/lib/libLLVM.so",
            "libLLVM-19.so", "libLLVM.so.19.1", "libLLVM-20.so", "libLLVM.so.20.1",
            "libLLVM.so",
            // macOS: Homebrew's llvm@18 (Apple silicon prefix, then Intel), then the loader's search path.
            "/opt/homebrew/opt/llvm@18/lib/libLLVM.dylib", "/usr/local/opt/llvm@18/lib/libLLVM.dylib", "libLLVM.dylib",
            // Windows: LLVM-MinGW's libLLVM (on PATH next to its clang), or an official LLVM's C API DLL.
            "libLLVM-18.dll", "LLVM-C.dll",
        ];
        foreach (var candidate in candidates)
            if (candidate is not null && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        throw new DllNotFoundException(
            "csharpc could not locate libLLVM (tried the release bundle's lib/, then LLVM 18-20 on the system). " +
            "Install LLVM 18 (apt: libllvm18; brew: llvm@18; Windows: LLVM-MinGW) or set CSHARPC_LIBLLVM to the shared library path. " +
            "See https://github.com/jonathanperis/csharpc#install");
    }

    /// <summary>The name of a global value.</summary>
    public static string TakeName(LLVMValueRef value) =>
        Marshal.PtrToStringUTF8(LLVMGetValueName2(value, out var length), (int)length) ?? "";

    /// <summary>Takes ownership of an LLVM-allocated C string.</summary>
    public static string? TakeMessage(nint message)
    {
        if (message == 0) return null;
        var text = Marshal.PtrToStringUTF8(message);
        LLVMDisposeMessage(message);
        return text;
    }

    // ---- Core: context / module -------------------------------------------------------
    [LibraryImport(Lib)] public static partial LLVMContextRef LLVMContextCreate();
    [LibraryImport(Lib)] public static partial void LLVMContextDispose(LLVMContextRef c);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMModuleRef LLVMModuleCreateWithNameInContext(string id, LLVMContextRef c);
    [LibraryImport(Lib)] public static partial void LLVMDisposeModule(LLVMModuleRef m);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMSetTarget(LLVMModuleRef m, string triple);
    [LibraryImport(Lib)] public static partial void LLVMSetModuleDataLayout(LLVMModuleRef m, LLVMTargetDataRef dl);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMSetSourceFileName(LLVMModuleRef m, string name, nuint len);
    [LibraryImport(Lib)] public static partial nint LLVMPrintModuleToString(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial void LLVMDisposeMessage(nint message);
    [LibraryImport(Lib)] public static partial int LLVMVerifyModule(LLVMModuleRef m, int action, out nint message);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int LLVMWriteBitcodeToFile(LLVMModuleRef m, string path);

    // ---- Bitcode / linking (runtime LTO) ------------------------------------------------
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int LLVMCreateMemoryBufferWithContentsOfFile(string path, out LLVMMemoryBufferRef buffer, out nint message);
    [LibraryImport(Lib)] public static partial void LLVMDisposeMemoryBuffer(LLVMMemoryBufferRef buffer);
    [LibraryImport(Lib)]
    public static partial int LLVMParseBitcodeInContext2(LLVMContextRef c, LLVMMemoryBufferRef buffer, out LLVMModuleRef module);
    [LibraryImport(Lib)] public static partial int LLVMLinkModules2(LLVMModuleRef dest, LLVMModuleRef src);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetFirstFunction(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetNextFunction(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetFirstGlobal(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetNextGlobal(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial int LLVMIsDeclaration(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial LLVMLinkage LLVMGetLinkage(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial nint LLVMGetValueName2(LLVMValueRef value, out nuint length);

    // ---- Types --------------------------------------------------------------------------
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMInt1TypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMInt8TypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMInt16TypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMInt32TypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMInt64TypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMIntTypeInContext(LLVMContextRef c, uint bits);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMFloatTypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMDoubleTypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMVoidTypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMTokenTypeInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMGetReturnType(LLVMTypeRef functionType);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMStructGetTypeAtIndex(LLVMTypeRef structType, uint index);
    [LibraryImport(Lib)] public static partial uint LLVMCountStructElementTypes(LLVMTypeRef structType);
    [LibraryImport(Lib)] public static partial ulong LLVMGetArrayLength2(LLVMTypeRef arrayType);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMGetElementType(LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMVectorType(LLVMTypeRef element, uint count);
    [LibraryImport(Lib)] public static partial LLVMAttributeRef LLVMCreateTypeAttribute(LLVMContextRef c, uint kind, LLVMTypeRef type);
    public static string LLVMGetValueName(LLVMValueRef value) =>
        System.Runtime.InteropServices.Marshal.PtrToStringUTF8(LLVMGetValueName2(value, out var n), (int)n);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMPointerTypeInContext(LLVMContextRef c, uint addressSpace);
    [LibraryImport(Lib)]
    public static partial LLVMTypeRef LLVMFunctionType(LLVMTypeRef ret, [In] LLVMTypeRef[] @params, uint count, int isVarArg);
    [LibraryImport(Lib)]
    public static partial LLVMTypeRef LLVMStructTypeInContext(LLVMContextRef c, [In] LLVMTypeRef[] elements, uint count, int packed);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMTypeRef LLVMStructCreateNamed(LLVMContextRef c, string name);
    [LibraryImport(Lib)]
    public static partial void LLVMStructSetBody(LLVMTypeRef type, [In] LLVMTypeRef[] elements, uint count, int packed);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMArrayType2(LLVMTypeRef element, ulong count);
    [LibraryImport(Lib)] public static partial LLVMTypeKind LLVMGetTypeKind(LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial uint LLVMGetIntTypeWidth(LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMTypeOf(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMGetAllocatedType(LLVMValueRef alloca);

    // ---- Values / globals / functions ----------------------------------------------------
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMAddFunction(LLVMModuleRef m, string name, LLVMTypeRef fnType);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMGetNamedFunction(LLVMModuleRef m, string name);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetParam(LLVMValueRef fn, uint index);
    [LibraryImport(Lib)] public static partial void LLVMReplaceAllUsesWith(LLVMValueRef oldValue, LLVMValueRef newValue);
    [LibraryImport(Lib)] public static partial void LLVMDeleteGlobal(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial void LLVMDeleteFunction(LLVMValueRef fn);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMSetValueName2(LLVMValueRef value, string name, nuint len);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMConstInt(LLVMTypeRef type, ulong value, int signExtend);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMConstReal(LLVMTypeRef type, double value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMConstNull(LLVMTypeRef type);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMAddGlobal(LLVMModuleRef m, LLVMTypeRef type, string name);
    [LibraryImport(Lib)] public static partial void LLVMSetInitializer(LLVMValueRef global, LLVMValueRef value);
    [LibraryImport(Lib)] public static partial void LLVMSetGlobalConstant(LLVMValueRef global, int isConstant);
    [LibraryImport(Lib)] public static partial void LLVMSetLinkage(LLVMValueRef global, LLVMLinkage linkage);
    [LibraryImport(Lib)] public static partial void LLVMSetUnnamedAddress(LLVMValueRef global, int kind);
    [LibraryImport(Lib)] public static partial void LLVMSetAlignment(LLVMValueRef value, uint bytes);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMConstStructInContext(LLVMContextRef c, [In] LLVMValueRef[] values, uint count, int packed);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMConstNamedStruct(LLVMTypeRef type, [In] LLVMValueRef[] values, uint count);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMConstArray2(LLVMTypeRef elementType, [In] LLVMValueRef[] values, ulong length);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMConstStringInContext(LLVMContextRef c, byte* str, uint length, int dontNullTerminate);

    // ---- Attributes ---------------------------------------------------------------------
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint LLVMGetEnumAttributeKindForName(string name, nuint length);
    [LibraryImport(Lib)] public static partial LLVMAttributeRef LLVMCreateEnumAttribute(LLVMContextRef c, uint kind, ulong value);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMAttributeRef LLVMCreateStringAttribute(LLVMContextRef c, string key, uint keyLength, string value, uint valueLength);
    [LibraryImport(Lib)] public static partial void LLVMAddAttributeAtIndex(LLVMValueRef fn, uint index, LLVMAttributeRef attribute);
    [LibraryImport(Lib)] public static partial void LLVMAddCallSiteAttribute(LLVMValueRef call, uint index, LLVMAttributeRef attribute);

    // ---- Basic blocks / builder ---------------------------------------------------------
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMBasicBlockRef LLVMAppendBasicBlockInContext(LLVMContextRef c, LLVMValueRef fn, string name);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetBasicBlockTerminator(LLVMBasicBlockRef block);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetFirstInstruction(LLVMBasicBlockRef block);
    [LibraryImport(Lib)] public static partial LLVMBuilderRef LLVMCreateBuilderInContext(LLVMContextRef c);
    [LibraryImport(Lib)] public static partial void LLVMDisposeBuilder(LLVMBuilderRef b);
    [LibraryImport(Lib)] public static partial void LLVMPositionBuilderAtEnd(LLVMBuilderRef b, LLVMBasicBlockRef block);
    [LibraryImport(Lib)] public static partial void LLVMPositionBuilderBefore(LLVMBuilderRef b, LLVMValueRef instruction);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetInsertBlock(LLVMBuilderRef b);

    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildRetVoid(LLVMBuilderRef b);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildRet(LLVMBuilderRef b, LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildBr(LLVMBuilderRef b, LLVMBasicBlockRef dest);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMBuildCondBr(LLVMBuilderRef b, LLVMValueRef cond, LLVMBasicBlockRef then, LLVMBasicBlockRef @else);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildUnreachable(LLVMBuilderRef b);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMBuildSwitch(LLVMBuilderRef b, LLVMValueRef value, LLVMBasicBlockRef @else, uint numCases);
    [LibraryImport(Lib)] public static partial void LLVMAddCase(LLVMValueRef @switch, LLVMValueRef onValue, LLVMBasicBlockRef dest);

    // ---- Exception handling (invoke / landingpad) -----------------------------------------
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildInvoke2(LLVMBuilderRef b, LLVMTypeRef fnType, LLVMValueRef fn, [In] LLVMValueRef[] args,
        uint count, LLVMBasicBlockRef then, LLVMBasicBlockRef @catch, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildLandingPad(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef personality, uint numClauses, string name);
    [LibraryImport(Lib)] public static partial void LLVMAddClause(LLVMValueRef landingPad, LLVMValueRef clause);
    [LibraryImport(Lib)] public static partial void LLVMSetCleanup(LLVMValueRef landingPad, int value);
    [LibraryImport(Lib)] public static partial void LLVMSetPersonalityFn(LLVMValueRef fn, LLVMValueRef personality);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildExtractValue(LLVMBuilderRef b, LLVMValueRef aggregate, uint index, string name);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetBasicBlockParent(LLVMBasicBlockRef block);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildResume(LLVMBuilderRef b, LLVMValueRef exception);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMConstInBoundsGEP2(LLVMTypeRef type, LLVMValueRef pointer, [In] LLVMValueRef[] indices, uint count);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildPhi(LLVMBuilderRef b, LLVMTypeRef type, string name);
    [LibraryImport(Lib)]
    public static partial void LLVMAddIncoming(LLVMValueRef phi, [In] LLVMValueRef[] values, [In] LLVMBasicBlockRef[] blocks, uint count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildBinOp(LLVMBuilderRef b, LLVMOpcode op, LLVMValueRef lhs, LLVMValueRef rhs, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildNeg(LLVMBuilderRef b, LLVMValueRef v, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildFNeg(LLVMBuilderRef b, LLVMValueRef v, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildNot(LLVMBuilderRef b, LLVMValueRef v, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildICmp(LLVMBuilderRef b, LLVMIntPredicate op, LLVMValueRef lhs, LLVMValueRef rhs, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildFCmp(LLVMBuilderRef b, LLVMRealPredicate op, LLVMValueRef lhs, LLVMValueRef rhs, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildCast(LLVMBuilderRef b, LLVMOpcode op, LLVMValueRef value, LLVMTypeRef destType, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildSelect(LLVMBuilderRef b, LLVMValueRef cond, LLVMValueRef then, LLVMValueRef @else, string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildInsertValue(LLVMBuilderRef b, LLVMValueRef aggregate, LLVMValueRef element, uint index, string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildArrayAlloca(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef count, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildAlloca(LLVMBuilderRef b, LLVMTypeRef type, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildLoad2(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef ptr, string name);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBuildStore(LLVMBuilderRef b, LLVMValueRef value, LLVMValueRef ptr);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMBuildAtomicRMW(LLVMBuilderRef b, LLVMAtomicRMWBinOp op, LLVMValueRef ptr, LLVMValueRef value,
        LLVMAtomicOrdering ordering, int singleThread);
    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMBuildAtomicCmpXchg(LLVMBuilderRef b, LLVMValueRef ptr, LLVMValueRef cmp, LLVMValueRef value,
        LLVMAtomicOrdering success, LLVMAtomicOrdering failure, int singleThread);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildFence(LLVMBuilderRef b, LLVMAtomicOrdering ordering, int singleThread, string name);
    [LibraryImport(Lib)] public static partial void LLVMSetOrdering(LLVMValueRef memoryAccess, LLVMAtomicOrdering ordering);
    [LibraryImport(Lib)] public static partial void LLVMSetVolatile(LLVMValueRef memoryAccess, int isVolatile);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint LLVMGetMDKindIDInContext(LLVMContextRef c, string name, uint length);
    [LibraryImport(Lib)] public static partial void LLVMSetMetadata(LLVMValueRef instruction, uint kindId, LLVMValueRef node);
    [LibraryImport(Lib)] public static partial void LLVMSetThreadLocal(LLVMValueRef global, int isThreadLocal);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildInBoundsGEP2(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef ptr, [In] LLVMValueRef[] indices, uint count, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildGEP2(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef ptr, [In] LLVMValueRef[] indices, uint count, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildStructGEP2(LLVMBuilderRef b, LLVMTypeRef type, LLVMValueRef ptr, uint index, string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMBuildCall2(LLVMBuilderRef b, LLVMTypeRef fnType, LLVMValueRef fn, [In] LLVMValueRef[] args, uint count, string name);

    // ---- Targets ------------------------------------------------------------------------
    // LLVMInitializeNativeTarget() is a static inline in the headers, so we call the exported
    // per-architecture initializers directly.
    [LibraryImport(Lib)] public static partial void LLVMInitializeX86TargetInfo();
    [LibraryImport(Lib)] public static partial void LLVMInitializeX86Target();
    [LibraryImport(Lib)] public static partial void LLVMInitializeX86TargetMC();
    [LibraryImport(Lib)] public static partial void LLVMInitializeX86AsmPrinter();
    [LibraryImport(Lib)] public static partial void LLVMInitializeX86AsmParser();
    [LibraryImport(Lib)] public static partial void LLVMInitializeAArch64TargetInfo();
    [LibraryImport(Lib)] public static partial void LLVMInitializeAArch64Target();
    [LibraryImport(Lib)] public static partial void LLVMInitializeAArch64TargetMC();
    [LibraryImport(Lib)] public static partial void LLVMInitializeAArch64AsmPrinter();
    [LibraryImport(Lib)] public static partial void LLVMInitializeAArch64AsmParser();
    [LibraryImport(Lib)] public static partial void LLVMInitializeWebAssemblyTargetInfo();
    [LibraryImport(Lib)] public static partial void LLVMInitializeWebAssemblyTarget();
    [LibraryImport(Lib)] public static partial void LLVMInitializeWebAssemblyTargetMC();
    [LibraryImport(Lib)] public static partial void LLVMInitializeWebAssemblyAsmPrinter();

    [LibraryImport(Lib)] public static partial nint LLVMGetDefaultTargetTriple();
    [LibraryImport(Lib)] public static partial nint LLVMGetHostCPUName();
    [LibraryImport(Lib)] public static partial nint LLVMGetHostCPUFeatures();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int LLVMGetTargetFromTriple(string triple, out LLVMTargetRef target, out nint error);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMTargetMachineRef LLVMCreateTargetMachine(LLVMTargetRef target, string triple, string cpu, string features,
        LLVMCodeGenOptLevel level, LLVMRelocMode reloc, LLVMCodeModel codeModel);
    [LibraryImport(Lib)] public static partial void LLVMDisposeTargetMachine(LLVMTargetMachineRef tm);
    [LibraryImport(Lib)] public static partial LLVMTargetDataRef LLVMCreateTargetDataLayout(LLVMTargetMachineRef tm);
    [LibraryImport(Lib)] public static partial void LLVMDisposeTargetData(LLVMTargetDataRef td);
    [LibraryImport(Lib)] public static partial ulong LLVMABISizeOfType(LLVMTargetDataRef td, LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial ulong LLVMOffsetOfElement(LLVMTargetDataRef td, LLVMTypeRef type, uint element);
    [LibraryImport(Lib)] public static partial uint LLVMABIAlignmentOfType(LLVMTargetDataRef td, LLVMTypeRef type);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int LLVMTargetMachineEmitToFile(LLVMTargetMachineRef tm, LLVMModuleRef m, string path, LLVMCodeGenFileType type, out nint error);

    // ---- New pass manager -----------------------------------------------------------------
    [LibraryImport(Lib)] public static partial LLVMPassBuilderOptionsRef LLVMCreatePassBuilderOptions();
    [LibraryImport(Lib)] public static partial void LLVMDisposePassBuilderOptions(LLVMPassBuilderOptionsRef options);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMErrorRef LLVMRunPasses(LLVMModuleRef m, string passes, LLVMTargetMachineRef tm, LLVMPassBuilderOptionsRef options);
    [LibraryImport(Lib)] public static partial nint LLVMGetErrorMessage(LLVMErrorRef error);
    [LibraryImport(Lib)] public static partial void LLVMDisposeErrorMessage(nint message);
}

/// <summary>Instruction opcodes from llvm-c/Core.h (LLVMOpcode). Only the ones we emit or inspect.</summary>
public enum LLVMOpcode
{
    Ret = 1, Br = 2, Switch = 3, IndirectBr = 4, Invoke = 5, Unreachable = 7, CallBr = 67,
    Alloca = 26, Load = 27, Store = 28, GetElementPtr = 29, ICmp = 42, FCmp = 43, PHI = 44, Call = 45, Select = 46,
    ExtractValue = 53, InsertValue = 54, Fence = 55, AtomicCmpXchg = 56, AtomicRMW = 57, Resume = 58, LandingPad = 59,
    Add = 8, FAdd = 9, Sub = 10, FSub = 11, Mul = 12, FMul = 13, UDiv = 14, SDiv = 15, FDiv = 16,
    URem = 17, SRem = 18, FRem = 19, Shl = 20, LShr = 21, AShr = 22, And = 23, Or = 24, Xor = 25,
    Trunc = 30, ZExt = 31, SExt = 32, FPToUI = 33, FPToSI = 34, UIToFP = 35, SIToFP = 36, FPTrunc = 37, FPExt = 38,
    PtrToInt = 39, IntToPtr = 40, BitCast = 41,
}
