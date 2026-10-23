using System.Runtime.InteropServices;

namespace Csharpc.Llvm;

/// <summary>
/// IR inspection and rewriting (llvm-c/Core.h, DebugInfo.h): what the module-level passes in
/// src/Csharpc/Wasm need to walk instructions, follow the CFG and move code between blocks.
/// </summary>
internal static unsafe partial class Native
{
    public const uint FunctionAttributeIndex = uint.MaxValue; // LLVMAttributeFunctionIndex
    public const uint ReturnAttributeIndex = 0;               // LLVMAttributeReturnIndex

    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetFirstBasicBlock(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetNextBasicBlock(LLVMBasicBlockRef bb);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetEntryBasicBlock(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetLastInstruction(LLVMBasicBlockRef bb);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetNextInstruction(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetPreviousInstruction(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial LLVMOpcode LLVMGetInstructionOpcode(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetInstructionParent(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial int LLVMGetNumOperands(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetOperand(LLVMValueRef value, uint index);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetCalledValue(LLVMValueRef call);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMGetCalledFunctionType(LLVMValueRef call);
    [LibraryImport(Lib)] public static partial uint LLVMGetNumArgOperands(LLVMValueRef call);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetNormalDest(LLVMValueRef invoke);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetUnwindDest(LLVMValueRef invoke);
    [LibraryImport(Lib)] public static partial uint LLVMGetInstructionCallConv(LLVMValueRef call);
    [LibraryImport(Lib)] public static partial void LLVMSetInstructionCallConv(LLVMValueRef call, uint cc);
    [LibraryImport(Lib)] public static partial uint LLVMGetCallSiteAttributeCount(LLVMValueRef call, uint index);
    [LibraryImport(Lib)] public static partial void LLVMGetCallSiteAttributes(LLVMValueRef call, uint index, LLVMAttributeRef* attributes);
    [LibraryImport(Lib)] public static partial void LLVMRemoveCallSiteEnumAttribute(LLVMValueRef call, uint index, uint kind);
    [LibraryImport(Lib)] public static partial void LLVMRemoveEnumAttributeAtIndex(LLVMValueRef fn, uint index, uint kind);
    [LibraryImport(Lib)] public static partial LLVMAttributeRef LLVMGetEnumAttributeAtIndex(LLVMValueRef fn, uint index, uint kind);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMRemoveStringAttributeAtIndex(LLVMValueRef fn, uint index, string kind, uint length);
    [LibraryImport(Lib)] public static partial void LLVMInstructionEraseFromParent(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial void LLVMInstructionRemoveFromParent(LLVMValueRef inst);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LLVMInsertIntoBuilderWithName(LLVMBuilderRef b, LLVMValueRef inst, string name);
    [LibraryImport(Lib)] public static partial void LLVMReplaceAllUsesWith(LLVMValueRef oldValue, LLVMValueRef newValue);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMBasicBlockRef LLVMInsertBasicBlockInContext(LLVMContextRef c, LLVMBasicBlockRef before, string name);
    [LibraryImport(Lib)] public static partial void LLVMMoveBasicBlockBefore(LLVMBasicBlockRef bb, LLVMBasicBlockRef movePos);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMBasicBlockAsValue(LLVMBasicBlockRef bb);
    [LibraryImport(Lib)] public static partial uint LLVMCountIncoming(LLVMValueRef phi);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetIncomingValue(LLVMValueRef phi, uint index);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetIncomingBlock(LLVMValueRef phi, uint index);
    [LibraryImport(Lib)] public static partial uint LLVMGetNumSuccessors(LLVMValueRef terminator);
    [LibraryImport(Lib)] public static partial LLVMBasicBlockRef LLVMGetSuccessor(LLVMValueRef terminator, uint index);
    [LibraryImport(Lib)] public static partial void LLVMSetSuccessor(LLVMValueRef terminator, uint index, LLVMBasicBlockRef block);
    [LibraryImport(Lib)] public static partial nint LLVMGetFirstUse(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial nint LLVMGetNextUse(nint use);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetUser(nint use);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAFunction(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAInlineAsm(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAInstruction(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAArgument(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAAllocaInst(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMIsAGlobalAlias(LLVMValueRef value);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMAliasGetAliasee(LLVMValueRef alias);
    [LibraryImport(Lib)] public static partial uint LLVMGetIntrinsicID(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial int LLVMHasPersonalityFn(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial uint LLVMCountParams(LLVMValueRef fn);
    [LibraryImport(Lib)] public static partial LLVMTypeRef LLVMGlobalGetValueType(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial LLVMValueRef LLVMGetPoison(LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial uint LLVMGetFunctionCallConv(LLVMValueRef fn);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial LLVMValueRef LLVMGetNamedGlobal(LLVMModuleRef m, string name);
    [LibraryImport(Lib)] public static partial LLVMModuleRef LLVMGetGlobalParent(LLVMValueRef global);
    [LibraryImport(Lib)] public static partial uint LLVMGetVectorSize(LLVMTypeRef type);
    [LibraryImport(Lib)] public static partial void LLVMGetStructElementTypes(LLVMTypeRef type, LLVMTypeRef* dest);
    [LibraryImport(Lib)] public static partial LLVMMetadataRef LLVMInstructionGetDebugLoc(LLVMValueRef inst);
    [LibraryImport(Lib)] public static partial void LLVMInstructionSetDebugLoc(LLVMValueRef inst, LLVMMetadataRef loc);
}
