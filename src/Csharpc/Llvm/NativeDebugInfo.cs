using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Csharpc.Llvm;

public readonly record struct LLVMDIBuilderRef(nint Handle);
public readonly record struct LLVMMetadataRef(nint Handle) { public bool IsNull => Handle == 0; }

/// <summary>llvm-c/DebugInfo.h (LLVM 18). Strings are passed as (pointer, length) pairs.</summary>
internal static unsafe partial class Native
{
    [LibraryImport(Lib)] public static partial LLVMDIBuilderRef LLVMCreateDIBuilder(LLVMModuleRef m);
    [LibraryImport(Lib)] public static partial void LLVMDisposeDIBuilder(LLVMDIBuilderRef b);
    [LibraryImport(Lib)] public static partial void LLVMDIBuilderFinalize(LLVMDIBuilderRef b);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateCompileUnit(LLVMDIBuilderRef b, int lang, LLVMMetadataRef file,
        string producer, nuint producerLen, int isOptimized, string flags, nuint flagsLen, uint runtimeVer,
        string splitName, nuint splitNameLen, int kind, uint dwoId, int splitDebugInlining, int debugInfoForProfiling,
        string sysRoot, nuint sysRootLen, string sdk, nuint sdkLen);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateFile(LLVMDIBuilderRef b, string file, nuint fileLen, string dir, nuint dirLen);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateFunction(LLVMDIBuilderRef b, LLVMMetadataRef scope, string name, nuint nameLen,
        string linkageName, nuint linkageNameLen, LLVMMetadataRef file, uint line, LLVMMetadataRef type, int isLocalToUnit,
        int isDefinition, uint scopeLine, int flags, int isOptimized);

    [LibraryImport(Lib)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateSubroutineType(LLVMDIBuilderRef b, LLVMMetadataRef file,
        [In] LLVMMetadataRef[] parameterTypes, uint count, int flags);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateBasicType(LLVMDIBuilderRef b, string name, nuint nameLen, ulong sizeInBits,
        uint encoding, int flags);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreatePointerType(LLVMDIBuilderRef b, LLVMMetadataRef pointee, ulong sizeInBits,
        uint alignInBits, uint addressSpace, string name, nuint nameLen);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateStructType(LLVMDIBuilderRef b, LLVMMetadataRef scope, string name, nuint nameLen,
        LLVMMetadataRef file, uint line, ulong sizeInBits, uint alignInBits, int flags, LLVMMetadataRef derivedFrom,
        [In] LLVMMetadataRef[] elements, uint count, uint runtimeLang, LLVMMetadataRef vtableHolder, string uniqueId, nuint uniqueIdLen);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateMemberType(LLVMDIBuilderRef b, LLVMMetadataRef scope, string name, nuint nameLen,
        LLVMMetadataRef file, uint line, ulong sizeInBits, uint alignInBits, ulong offsetInBits, int flags, LLVMMetadataRef type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateReplaceableCompositeType(LLVMDIBuilderRef b, uint tag, string name, nuint nameLen,
        LLVMMetadataRef scope, LLVMMetadataRef file, uint line, uint runtimeLang, ulong sizeInBits, uint alignInBits, int flags,
        string uniqueId, nuint uniqueIdLen);

    [LibraryImport(Lib)] public static partial void LLVMMetadataReplaceAllUsesWith(LLVMMetadataRef temp, LLVMMetadataRef replacement);

    [LibraryImport(Lib)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateArrayType(LLVMDIBuilderRef b, ulong size, uint alignInBits, LLVMMetadataRef type,
        [In] LLVMMetadataRef[] subscripts, uint count);

    [LibraryImport(Lib)] public static partial LLVMMetadataRef LLVMDIBuilderGetOrCreateSubrange(LLVMDIBuilderRef b, long lowerBound, long count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateAutoVariable(LLVMDIBuilderRef b, LLVMMetadataRef scope, string name, nuint nameLen,
        LLVMMetadataRef file, uint line, LLVMMetadataRef type, int alwaysPreserve, int flags, uint alignInBits);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateParameterVariable(LLVMDIBuilderRef b, LLVMMetadataRef scope, string name,
        nuint nameLen, uint argNo, LLVMMetadataRef file, uint line, LLVMMetadataRef type, int alwaysPreserve, int flags);

    [LibraryImport(Lib)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateExpression(LLVMDIBuilderRef b, [In] ulong[]? addr, nuint length);

    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMDIBuilderInsertDeclareAtEnd(LLVMDIBuilderRef b, LLVMValueRef storage, LLVMMetadataRef varInfo,
        LLVMMetadataRef expr, LLVMMetadataRef debugLoc, LLVMBasicBlockRef block);

    [LibraryImport(Lib)]
    public static partial LLVMValueRef LLVMDIBuilderInsertDbgValueAtEnd(LLVMDIBuilderRef b, LLVMValueRef value, LLVMMetadataRef varInfo,
        LLVMMetadataRef expr, LLVMMetadataRef debugLoc, LLVMBasicBlockRef block);

    [LibraryImport(Lib)]
    public static partial LLVMMetadataRef LLVMDIBuilderCreateDebugLocation(LLVMContextRef c, uint line, uint column, LLVMMetadataRef scope,
        LLVMMetadataRef inlinedAt);

    [LibraryImport(Lib)] public static partial void LLVMSetCurrentDebugLocation2(LLVMBuilderRef b, LLVMMetadataRef loc);
    [LibraryImport(Lib)] public static partial void LLVMSetSubprogram(LLVMValueRef fn, LLVMMetadataRef subprogram);
    [LibraryImport(Lib)] public static partial LLVMMetadataRef LLVMGetSubprogram(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial LLVMMetadataRef LLVMValueAsMetadata(LLVMValueRef value);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMAddModuleFlag(LLVMModuleRef m, int behavior, string key, nuint keyLen, LLVMMetadataRef value);
}
