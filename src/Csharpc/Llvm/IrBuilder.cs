using static Csharpc.Llvm.Native;

namespace Csharpc.Llvm;

/// <summary>Thin wrapper over LLVMBuilderRef. Mirrors llvm::IRBuilder naming.</summary>
public sealed class IrBuilder(LlvmContext context) : IDisposable
{
    public LLVMBuilderRef Ref { get; } = LLVMCreateBuilderInContext(context.Ref);
    public LlvmContext Context { get; } = context;

    public LLVMBasicBlockRef InsertBlock => LLVMGetInsertBlock(Ref);
    public bool CurrentBlockTerminated => InsertBlock.HasTerminator;

    public LLVMBasicBlockRef AppendBlock(LLVMValueRef fn, string name) =>
        LLVMAppendBasicBlockInContext(Context.Ref, fn, name);

    public void PositionAtEnd(LLVMBasicBlockRef block) => LLVMPositionBuilderAtEnd(Ref, block);

    /// <summary>Positions at the start of a block (before its first instruction, if any).</summary>
    public void PositionAtStart(LLVMBasicBlockRef block)
    {
        var first = LLVMGetFirstInstruction(block);
        if (first.IsNull) LLVMPositionBuilderAtEnd(Ref, block);
        else LLVMPositionBuilderBefore(Ref, first);
    }

    // Control flow
    public LLVMValueRef Ret(LLVMValueRef value) => LLVMBuildRet(Ref, value);
    public LLVMValueRef RetVoid() => LLVMBuildRetVoid(Ref);
    public LLVMValueRef Br(LLVMBasicBlockRef dest) => LLVMBuildBr(Ref, dest);
    public LLVMValueRef CondBr(LLVMValueRef cond, LLVMBasicBlockRef then, LLVMBasicBlockRef @else) =>
        LLVMBuildCondBr(Ref, cond, then, @else);
    public LLVMValueRef Unreachable() => LLVMBuildUnreachable(Ref);

    // Arithmetic
    public LLVMValueRef BinOp(LLVMOpcode op, LLVMValueRef lhs, LLVMValueRef rhs, string name = "") =>
        LLVMBuildBinOp(Ref, op, lhs, rhs, name);
    public LLVMValueRef Neg(LLVMValueRef v) => LLVMBuildNeg(Ref, v, "");
    public LLVMValueRef FNeg(LLVMValueRef v) => LLVMBuildFNeg(Ref, v, "");
    public LLVMValueRef Not(LLVMValueRef v) => LLVMBuildNot(Ref, v, "");
    public LLVMValueRef ICmp(LLVMIntPredicate op, LLVMValueRef lhs, LLVMValueRef rhs, string name = "") =>
        LLVMBuildICmp(Ref, op, lhs, rhs, name);
    public LLVMValueRef FCmp(LLVMRealPredicate op, LLVMValueRef lhs, LLVMValueRef rhs, string name = "") =>
        LLVMBuildFCmp(Ref, op, lhs, rhs, name);
    public LLVMValueRef Cast(LLVMOpcode op, LLVMValueRef value, LLVMTypeRef dest) => LLVMBuildCast(Ref, op, value, dest, "");
    public LLVMValueRef Select(LLVMValueRef cond, LLVMValueRef then, LLVMValueRef @else) =>
        LLVMBuildSelect(Ref, cond, then, @else, "");

    // Memory
    public LLVMValueRef Alloca(LLVMTypeRef type, string name = "") => LLVMBuildAlloca(Ref, type, name);
    public LLVMValueRef Load(LLVMTypeRef type, LLVMValueRef ptr, string name = "") => LLVMBuildLoad2(Ref, type, ptr, name);
    public LLVMValueRef Store(LLVMValueRef value, LLVMValueRef ptr) => LLVMBuildStore(Ref, value, ptr);
    public LLVMValueRef StructGep(LLVMTypeRef type, LLVMValueRef ptr, uint index, string name = "") =>
        LLVMBuildStructGEP2(Ref, type, ptr, index, name);
    public LLVMValueRef InBoundsGep(LLVMTypeRef type, LLVMValueRef ptr, LLVMValueRef[] indices, string name = "") =>
        LLVMBuildInBoundsGEP2(Ref, type, ptr, indices, (uint)indices.Length, name);

    // Calls
    public LLVMValueRef Call(LLVMTypeRef fnType, LLVMValueRef fn, LLVMValueRef[] args, string name = "") =>
        LLVMBuildCall2(Ref, fnType, fn, args, (uint)args.Length, name);

    public void Dispose() => LLVMDisposeBuilder(Ref);
}
