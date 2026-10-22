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

    public LLVMValueRef Switch(LLVMValueRef value, LLVMBasicBlockRef @default, IReadOnlyList<(LLVMValueRef Value, LLVMBasicBlockRef Dest)> cases)
    {
        var sw = LLVMBuildSwitch(Ref, value, @default, (uint)cases.Count);
        foreach (var (v, dest) in cases) LLVMAddCase(sw, v, dest);
        return sw;
    }

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

    // Aggregates (structs, Nullable<T>, ValueTuple)
    public LLVMValueRef ExtractValue(LLVMValueRef aggregate, uint index, string name = "") =>
        LLVMBuildExtractValue(Ref, aggregate, index, name);
    public LLVMValueRef InsertValue(LLVMValueRef aggregate, LLVMValueRef element, uint index, string name = "") =>
        LLVMBuildInsertValue(Ref, aggregate, element, index, name);

    // Memory
    public LLVMValueRef Alloca(LLVMTypeRef type, string? name = "") => LLVMBuildAlloca(Ref, type, name ?? ""); // synthesized locals (e.g. lock's lockTaken) have null names
    public LLVMValueRef Load(LLVMTypeRef type, LLVMValueRef ptr, string name = "") => LLVMBuildLoad2(Ref, type, ptr, name);
    public LLVMValueRef Store(LLVMValueRef value, LLVMValueRef ptr) => LLVMBuildStore(Ref, value, ptr);
    public LLVMValueRef StructGep(LLVMTypeRef type, LLVMValueRef ptr, uint index, string name = "") =>
        LLVMBuildStructGEP2(Ref, type, ptr, index, name);
    public LLVMValueRef InBoundsGep(LLVMTypeRef type, LLVMValueRef ptr, LLVMValueRef[] indices, string name = "") =>
        LLVMBuildInBoundsGEP2(Ref, type, ptr, indices, (uint)indices.Length, name);

    // Calls

    /// <summary>
    /// While set (inside a try region), calls that may throw become `invoke`s unwinding to this
    /// landing pad; the builder then continues in the normal-return block.
    /// </summary>
    public LLVMBasicBlockRef UnwindDest { get; set; }

    /// <summary>Decides whether a callee may throw (null: assume it may).</summary>
    public Func<LLVMValueRef, bool>? MayThrow { get; set; }

    public LLVMValueRef Call(LLVMTypeRef fnType, LLVMValueRef fn, LLVMValueRef[] args, string name = "")
    {
        if (UnwindDest.IsNull || MayThrow?.Invoke(fn) == false)
            return LLVMBuildCall2(Ref, fnType, fn, args, (uint)args.Length, name);
        var normal = LLVMAppendBasicBlockInContext(Context.Ref, LLVMGetBasicBlockParent(InsertBlock), "invoke.cont");
        var result = LLVMBuildInvoke2(Ref, fnType, fn, args, (uint)args.Length, normal, UnwindDest, name);
        LLVMPositionBuilderAtEnd(Ref, normal);
        return result;
    }

    public LLVMValueRef LandingPad(LLVMTypeRef type, LLVMValueRef personality, IReadOnlyList<LLVMValueRef> clauses, bool cleanup)
    {
        var pad = LLVMBuildLandingPad(Ref, type, personality, (uint)clauses.Count, "lpad");
        foreach (var clause in clauses) LLVMAddClause(pad, clause);
        LLVMSetCleanup(pad, cleanup ? 1 : 0);
        return pad;
    }

    public void Dispose() => LLVMDisposeBuilder(Ref);
}
