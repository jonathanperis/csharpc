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
    public void PositionBefore(LLVMValueRef instruction) => LLVMPositionBuilderBefore(Ref, instruction);

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

    // Vectors
    public LLVMValueRef ExtractElement(LLVMValueRef vector, LLVMValueRef index) => LLVMBuildExtractElement(Ref, vector, index, "");
    public LLVMValueRef InsertElement(LLVMValueRef vector, LLVMValueRef element, LLVMValueRef index) => LLVMBuildInsertElement(Ref, vector, element, index, "");
    public LLVMValueRef ShuffleVector(LLVMValueRef v1, LLVMValueRef v2, LLVMValueRef mask) => LLVMBuildShuffleVector(Ref, v1, v2, mask, "");

    // Aggregates (structs, Nullable<T>, ValueTuple)
    public LLVMValueRef ExtractValue(LLVMValueRef aggregate, uint index, string name = "") =>
        LLVMBuildExtractValue(Ref, aggregate, index, name);
    public LLVMValueRef InsertValue(LLVMValueRef aggregate, LLVMValueRef element, uint index, string name = "") =>
        LLVMBuildInsertValue(Ref, aggregate, element, index, name);

    // Memory
    public LLVMValueRef Alloca(LLVMTypeRef type, string? name = "") => LLVMBuildAlloca(Ref, type, name ?? ""); // synthesized locals (e.g. lock's lockTaken) have null names
    // 128-bit integers are accessed 8-aligned: boxes keep their payload at offset 8 (a 16-aligned access there
    // is a movaps on a misaligned address), and csharpc's objects are 8-aligned.
    // The same holds for a struct that holds one (a boxed enumerator or KeyValuePair<Int128, V>: the field sits at 16 from the payload, 24 from the object).
    private static bool HoldsInt128(LLVMTypeRef type)
    {
        if (type.IsInteger) return type.IntWidth == 128;
        if (type.Kind == LLVMTypeKind.Array) return HoldsInt128(LLVMGetElementType(type));
        if (type.Kind != LLVMTypeKind.Struct) return false;
        for (uint i = 0, n = LLVMCountStructElementTypes(type); i < n; i++)
            if (HoldsInt128(LLVMStructGetTypeAtIndex(type, i))) return true;
        return false;
    }

    public LLVMValueRef Load(LLVMTypeRef type, LLVMValueRef ptr, string name = "")
    {
        var load = LLVMBuildLoad2(Ref, type, ptr, name);
        if (HoldsInt128(type)) LLVMSetAlignment(load, 8);
        // Vector128<T> (<16 x i8>): objects are 8-aligned, and the framework's vector loops read at any element address.
        else if (type.Kind == LLVMTypeKind.Vector) LLVMSetAlignment(load, 1);
        return load;
    }

    // --gc-nursery write barriers are added over the finished module (ModuleCodeGen.WriteBarriers.cs).
    public LLVMValueRef Store(LLVMValueRef value, LLVMValueRef ptr)
    {
        var store = LLVMBuildStore(Ref, value, ptr);
        if (HoldsInt128(value.Type)) LLVMSetAlignment(store, 8);
        else if (value.Type.Kind == LLVMTypeKind.Vector) LLVMSetAlignment(store, 1);
        return store;
    }
    public LLVMValueRef StructGep(LLVMTypeRef type, LLVMValueRef ptr, uint index, string name = "") =>
        LLVMBuildStructGEP2(Ref, type, ptr, index, name);
    public LLVMValueRef InBoundsGep(LLVMTypeRef type, LLVMValueRef ptr, LLVMValueRef[] indices, string name = "") =>
        LLVMBuildInBoundsGEP2(Ref, type, ptr, indices, (uint)indices.Length, name);

    /// <summary>A GEP without `inbounds`: byref arithmetic that may leave the object (Unsafe.Add on a null ref, pointer bounds).</summary>
    public LLVMValueRef Gep(LLVMTypeRef type, LLVMValueRef ptr, LLVMValueRef[] indices, string name = "") =>
        LLVMBuildGEP2(Ref, type, ptr, indices, (uint)indices.Length, name);

    // Calls

    /// <summary>
    /// While set (inside a try region), calls that may throw become `invoke`s unwinding to this
    /// landing pad; the builder then continues in the normal-return block.
    /// </summary>
    public LLVMBasicBlockRef UnwindDest { get; set; }

    /// <summary>Decides whether a callee may throw (null: assume it may).</summary>
    public Func<LLVMValueRef, bool>? MayThrow { get; set; }

    /// <summary>
    /// Maps <see cref="UnwindDest"/> (possibly null) to the landing pad a may-throw call actually
    /// unwinds to: stack-trace recording gives every call site its own pad (FunctionCodeGen.StackTraces.cs).
    /// </summary>
    public Func<LLVMBasicBlockRef, LLVMBasicBlockRef>? UnwindHook { get; set; }

    public LLVMValueRef Call(LLVMTypeRef fnType, LLVMValueRef fn, LLVMValueRef[] args, string name = "")
    {
        if (MayThrow?.Invoke(fn) == false)
            return LLVMBuildCall2(Ref, fnType, fn, args, (uint)args.Length, name);
        var unwind = UnwindHook is { } hook ? hook(UnwindDest) : UnwindDest;
        if (unwind.IsNull)
            return LLVMBuildCall2(Ref, fnType, fn, args, (uint)args.Length, name);
        var normal = LLVMAppendBasicBlockInContext(Context.Ref, LLVMGetBasicBlockParent(InsertBlock), "invoke.cont");
        var result = LLVMBuildInvoke2(Ref, fnType, fn, args, (uint)args.Length, normal, unwind, name);
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

    public LLVMValueRef Resume(LLVMValueRef exception) => LLVMBuildResume(Ref, exception);

    public LLVMValueRef Phi(LLVMTypeRef type, string name = "") => LLVMBuildPhi(Ref, type, name);

    public static void AddIncoming(LLVMValueRef phi, LLVMValueRef value, LLVMBasicBlockRef block) =>
        LLVMAddIncoming(phi, [value], [block], 1);

    public void Dispose() => LLVMDisposeBuilder(Ref);
}
