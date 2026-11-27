using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// Escape analysis (#457): an object or delegate that never outlives the managed function creating it lives in that function's
/// stack frame instead of the GC heap, as NativeAOT's RyuJIT does since .NET 10. A capturing lambda passed to a method that only
/// invokes it (SpinWait.SpinUntil, List.ForEach, a LINQ predicate) then allocates nothing, and the hand-off loop of
/// sync_throughput (#428) stops paying an allocation per round.
///
/// The decision needs the optimized code: what the inliner exposed, and the callee parameters LLVM's function-attrs proved
/// `nocapture`. But with runtime LTO the pipeline inlines csrt_alloc's fast path into every caller, after which no allocation is
/// visible. So <see cref="Prepare"/> routes the managed functions' csrt_alloc calls through a private wrapper kept out of the
/// inliner, <see cref="Run"/> rewrites the calls that qualify after the pipeline, then inlines the wrapper everywhere else and
/// cleans up the code it touched.
///
/// A call qualifies when its size is a small constant and its result is only loaded from, stored into, offset (GEP), compared, or
/// passed to a parameter the callee does not capture. Storing the pointer anywhere, returning it, a phi or select of it, ptrtoint,
/// or any other use is an escape. Without phi/select the pointer cannot reach the next iteration of a loop, so one entry-block slot
/// serves every iteration, re-zeroed at the allocation site. The collector needs nothing: stacks are scanned conservatively, so the
/// object's fields stay roots, and a stack address is not a heap object. Not under the nursery or concurrent collectors (their
/// barriers take every non-alloca store target for a heap address) nor on WebAssembly.
/// </summary>
internal static class HeapToStack
{
    private const string Wrapper = "csharpc.alloc.h2s";
    private const ulong MaxObjectBytes = 128, MaxFrameBytes = 512;

    /// <summary>Before the pipeline: managed functions call csrt_alloc through a noinline wrapper. False when nothing allocates.</summary>
    public static bool Prepare(LLVMModuleRef m)
    {
        var alloc = LLVMGetNamedFunction(m, "csrt_alloc");
        if (alloc.IsNull) return false;
        List<LLVMValueRef> calls = [];
        for (var use = LLVMGetFirstUse(alloc); use != 0; use = LLVMGetNextUse(use))
        {
            var user = LLVMGetUser(use);
            if (LLVMIsAInstruction(user).IsNull || LLVMGetInstructionOpcode(user) != LLVMOpcode.Call || LLVMGetCalledValue(user) != alloc) continue;
            if (Managed(LLVMGetBasicBlockParent(LLVMGetInstructionParent(user)))) calls.Add(user);
        }
        if (calls.Count == 0) return false;
        var context = LLVMGetModuleContext(m);
        // The wrapper must carry the whole fast path, as each caller did when the pipeline inlined csrt_alloc into it: with the size a
        // parameter instead of a constant, the inliner left csrt_gc_alloc a call (binarytrees and churn ran 1.5x slower). So these two
        // call sites are always inlined: the wrapper's call to csrt_alloc and csrt_alloc's call to csrt_gc_alloc. Call-site
        // attributes, not function ones: marking the functions alwaysinline forced the fast path into every runtime caller as well
        // and grew every executable by about 12 KB. Inlined into the callers afterwards, the constant size folds the size class again.
        var alwaysInline = LLVMCreateEnumAttribute(context, LLVMGetEnumAttributeKindForName("alwaysinline", 12), 0);
        if (LLVMIsDeclaration(alloc) == 0 && LLVMGetNamedFunction(m, "csrt_gc_alloc") is { IsNull: false } gcAlloc)
            for (var bb = LLVMGetFirstBasicBlock(alloc); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                    if (LLVMGetInstructionOpcode(inst) == LLVMOpcode.Call && LLVMGetCalledValue(inst) == gcAlloc)
                        LLVMAddCallSiteAttribute(inst, FunctionAttributeIndex, alwaysInline);
        var type = LLVMGlobalGetValueType(alloc);
        var wrapper = LLVMAddFunction(m, Wrapper, type);
        // Not internal: the pipeline's interprocedural passes (IPSCCP, dead-argument elimination) rewrite a local function's signature,
        // and dropped both parameters of a wrapper that every call passed the same constants. linkonce_odr keeps it as declared and
        // still lets globaldce drop it; hidden keeps it out of the dynamic symbol table (each partition makes an identical one).
        LLVMSetLinkage(wrapper, LLVMLinkage.LinkOnceODR);
        LLVMSetVisibility(wrapper, 1 /* hidden */);
        AddFunctionAttribute(context, wrapper, "noinline");
        var b = LLVMCreateBuilderInContext(context);
        try
        {
            LLVMPositionBuilderAtEnd(b, LLVMAppendBasicBlockInContext(context, wrapper, "entry"));
            var result = LLVMBuildCall2(b, type, alloc, [LLVMGetParam(wrapper, 0), LLVMGetParam(wrapper, 1)], 2, "");
            LLVMAddCallSiteAttribute(result, FunctionAttributeIndex, alwaysInline);
            LLVMBuildRet(b, result);
        }
        finally { LLVMDisposeBuilder(b); }
        // The allocator facts (noalias, nonnull) carry over to the wrapper's result.
        foreach (var name in (string[])["noalias", "nonnull"])
        {
            var kind = LLVMGetEnumAttributeKindForName(name, (nuint)name.Length);
            if (LLVMGetEnumAttributeAtIndex(alloc, 0, kind).Handle != 0) LLVMAddAttributeAtIndex(wrapper, 0, LLVMCreateEnumAttribute(context, kind, 0));
        }
        foreach (var call in calls) LLVMSetOperand(call, (uint)LLVMGetNumOperands(call) - 1, wrapper);
        return true;
    }

    /// <summary>After the pipeline: rewrites the calls that qualify, then inlines the wrapper into the rest. Returns the rewrites.</summary>
    public static int Run(LLVMModuleRef m, TargetMachine target)
    {
        var wrapper = LLVMGetNamedFunction(m, Wrapper);
        if (wrapper.IsNull) return 0;
        var context = LLVMGetModuleContext(m);
        var escapes = new Escapes(LLVMGetEnumAttributeKindForName("nocapture", 9));
        var i8 = LLVMInt8TypeInContext(context);
        var i64 = LLVMInt64TypeInContext(context);
        Dictionary<LLVMValueRef, List<LLVMValueRef>> byFunction = [];
        for (var use = LLVMGetFirstUse(wrapper); use != 0; use = LLVMGetNextUse(use))
        {
            var user = LLVMGetUser(use);
            if (LLVMIsAInstruction(user).IsNull || LLVMGetInstructionOpcode(user) != LLVMOpcode.Call || LLVMGetCalledValue(user) != wrapper
                || LLVMGetNumArgOperands(user) != 2) continue;
            var fn = LLVMGetBasicBlockParent(LLVMGetInstructionParent(user));
            if (!byFunction.TryGetValue(fn, out var list)) byFunction[fn] = list = [];
            list.Add(user);
        }
        var rewritten = 0;
        var b = LLVMCreateBuilderInContext(context);
        try
        {
            foreach (var (fn, calls) in byFunction)
            {
                ulong frame = 0;
                List<LLVMValueRef> passedTo = [];
                foreach (var call in calls)
                {
                    var size = LLVMGetOperand(call, 1);
                    if (LLVMIsAConstantInt(size).IsNull) continue;
                    var bytes = LLVMConstIntGetZExtValue(size);
                    if (bytes == 0 || bytes > MaxObjectBytes || frame + bytes > MaxFrameBytes) continue;
                    List<LLVMValueRef> receivers = [];
                    if (!escapes.Contained(call, receivers, 0)) continue;
                    frame += bytes;
                    passedTo.AddRange(receivers);
                    // The slot: in the entry block, so it is allocated once per frame (stack coloring and SROA see a static alloca).
                    var entry = LLVMGetEntryBasicBlock(fn);
                    LLVMPositionBuilderBefore(b, LLVMGetFirstInstruction(entry));
                    var slot = LLVMBuildAlloca(b, LLVMArrayType2(i8, bytes), "h2s");
                    LLVMSetAlignment(slot, 16);
                    // What csrt_alloc does: zeroed memory, the method table in the first word.
                    LLVMPositionBuilderBefore(b, call);
                    var memset = LLVMBuildMemSet(b, slot, LLVMConstInt(i8, 0, 0), LLVMConstInt(i64, bytes, 0), 16);
                    LLVMInstructionSetDebugLoc(memset, LLVMInstructionGetDebugLoc(call));
                    LLVMBuildStore(b, LLVMGetOperand(call, 0), slot);
                    LLVMReplaceAllUsesWith(call, slot);
                    LLVMInstructionEraseFromParent(call);
                    rewritten++;
                }
                // A tail call promises the callee touches none of the caller's stack slots; the ones that now receive one can't keep it.
                foreach (var receiver in passedTo)
                    if (LLVMGetTailCallKind(receiver) == 1) LLVMSetTailCall(receiver, 0);
            }
        }
        finally { LLVMDisposeBuilder(b); }
        var noinline = LLVMGetEnumAttributeKindForName("noinline", 8);
        LLVMRemoveEnumAttributeAtIndex(wrapper, FunctionAttributeIndex, noinline);
        // The wrapper's body is csrt_alloc's inlined fast path, and the regular inliner decides per call site whether it goes into the
        // caller, as it decided for csrt_alloc: forcing it everywhere (alwaysinline) put the fast path into every cold allocation too,
        // exception construction included, and grew every executable by about 12 KB. The pipeline used to optimize the inlined path
        // together with the code around it: the size class folds for a constant size, DSE drops the zeroing that the constructor's
        // stores overwrite, GVN knows the fresh object's fields are zero, LICM hoists the allocation cache's TLS address out of loops.
        // Without that, allocation-heavy programs ran up to 1.7x slower (churn, binarytrees, output). So the functions that allocate
        // get those passes again. Meanwhile every other function is fenced off with optnone, and every function but the wrapper is
        // noinline, so this inliner can only inline the wrapper and the cost stays with the code that needs it.
        var optnone = LLVMGetEnumAttributeKindForName("optnone", 7);
        var alwaysinline = LLVMGetEnumAttributeKindForName("alwaysinline", 12);
        List<(LLVMValueRef Fn, bool HadNoinline, bool Fenced)> marked = [];
        for (var fn = LLVMGetFirstFunction(m); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0 || fn == wrapper
                || LLVMGetEnumAttributeAtIndex(fn, FunctionAttributeIndex, optnone).Handle != 0
                || LLVMGetEnumAttributeAtIndex(fn, FunctionAttributeIndex, alwaysinline).Handle != 0) continue;
            var hadNoinline = LLVMGetEnumAttributeAtIndex(fn, FunctionAttributeIndex, noinline).Handle != 0;
            if (!hadNoinline) AddFunctionAttribute(context, fn, "noinline");
            var fence = !byFunction.ContainsKey(fn);
            if (fence) AddFunctionAttribute(context, fn, "optnone");
            marked.Add((fn, hadNoinline, fence));
        }
        LlvmModule.RunPasses(m, "cgscc(inline),function(sroa,early-cse<memssa>,instcombine<no-verify-fixpoint>,simplifycfg,"
            + "loop-mssa(licm<allowspeculation>),gvn,dse,instcombine<no-verify-fixpoint>,simplifycfg)", target);
        foreach (var (fn, hadNoinline, fence) in marked)
        {
            if (fence) LLVMRemoveEnumAttributeAtIndex(fn, FunctionAttributeIndex, optnone);
            if (!hadNoinline) LLVMRemoveEnumAttributeAtIndex(fn, FunctionAttributeIndex, noinline);
        }
        // The inliner deletes the wrapper itself once nothing calls it (linkonce_odr): look it up again rather than keep the handle.
        // Calls the inliner kept (cold sites) still need it.
        var left = LLVMGetNamedFunction(m, Wrapper);
        if (!left.IsNull && LLVMGetFirstUse(left) == 0) LLVMDeleteFunction(left);
        return rewritten;
    }

    /// <summary>
    /// The escape check. A value is contained when every use of it (the new object, or an address derived from it) keeps it inside
    /// the frame. A call keeps it when the callee's parameter is contained in the callee's body, checked the same way (memoized; a
    /// parameter whose check is in progress counts as contained, the greatest fixpoint, which is sound because an escape needs an
    /// escaping use somewhere) or, for a declaration or an indirect call, when LLVM marks it nocapture. LLVM's own nocapture
    /// inference is not enough: it counts a comparison with null as a possible capture unless the pointer is known dereferenceable,
    /// and nearly every managed method null-checks its reference arguments. For a lifetime question a comparison is harmless: it
    /// never makes a copy of the pointer.
    /// </summary>
    private sealed class Escapes(uint nocapture)
    {
        private readonly Dictionary<(LLVMValueRef Fn, uint Index), bool> _parameters = [];
        private readonly List<(LLVMValueRef Fn, uint Index)> _assumed = [];

        /// <summary>Whether <paramref name="value"/> stays in the frame; calls that receive it go to <paramref name="receivers"/>.</summary>
        public bool Contained(LLVMValueRef value, List<LLVMValueRef>? receivers, int depth)
        {
            if (depth > 8) return false;
            for (var use = LLVMGetFirstUse(value); use != 0; use = LLVMGetNextUse(use))
            {
                var user = LLVMGetUser(use);
                if (LLVMIsAInstruction(user).IsNull) return false;
                switch (LLVMGetInstructionOpcode(user))
                {
                    case LLVMOpcode.Load:
                    case LLVMOpcode.ICmp:
                        break;
                    case LLVMOpcode.Store:
                        // Into the object is fine; the object (or an address in it) stored anywhere escapes.
                        if (LLVMGetOperand(user, 0) == value) return false;
                        break;
                    case LLVMOpcode.AtomicRMW:
                    case LLVMOpcode.AtomicCmpXchg:
                        if (LLVMGetOperand(user, 0) != value) return false;
                        for (var i = 1u; i < (uint)LLVMGetNumOperands(user); i++)
                            if (LLVMGetOperand(user, i) == value) return false;
                        break;
                    case LLVMOpcode.GetElementPtr:
                        if (LLVMGetOperand(user, 0) != value || !Contained(user, receivers, depth + 1)) return false;
                        break;
                    case LLVMOpcode.Call:
                    {
                        // A musttail call must not see the caller's frame; the callee operand itself is never the object.
                        var callee = LLVMGetCalledValue(user);
                        if (LLVMGetTailCallKind(user) == 2 || callee == value) return false;
                        var args = LLVMGetNumArgOperands(user);
                        for (var i = 0u; i < args; i++)
                            if (LLVMGetOperand(user, i) == value && !Kept(user, callee, i, depth)) return false;
                        receivers?.Add(user);
                        break;
                    }
                    default:
                        return false;
                }
            }
            return true;
        }

        private bool Kept(LLVMValueRef call, LLVMValueRef callee, uint index, int depth)
        {
            if (LLVMGetCallSiteEnumAttribute(call, index + 1, nocapture).Handle != 0) return true;
            if (LLVMIsAFunction(callee).IsNull) return false;
            if (LLVMGetEnumAttributeAtIndex(callee, index + 1, nocapture).Handle != 0) return true;
            if (LLVMIsDeclaration(callee) != 0 || index >= LLVMCountParams(callee)) return false;
            var key = (callee, index);
            if (_parameters.TryGetValue(key, out var known)) return known;
            // A "contained" found while this parameter was assumed contained may rest on that assumption: when it fails, those
            // answers are dropped (a "no" never rests on an assumption, so it stays).
            var mark = _assumed.Count;
            _parameters[key] = true;
            _assumed.Add(key);
            var contained = Contained(LLVMGetParam(callee, index), null, depth + 1);
            if (!contained)
            {
                for (var i = mark; i < _assumed.Count; i++)
                    if (_parameters.TryGetValue(_assumed[i], out var v) && v) _parameters.Remove(_assumed[i]);
                _assumed.RemoveRange(mark, _assumed.Count - mark);
                _parameters[key] = false;
            }
            return contained;
        }
    }

    private static bool Managed(LLVMValueRef fn) => TakeName(fn).StartsWith("_CN", StringComparison.Ordinal);

    private static void AddFunctionAttribute(LLVMContextRef context, LLVMValueRef fn, string name)
    {
        var kind = LLVMGetEnumAttributeKindForName(name, (nuint)name.Length);
        LLVMAddAttributeAtIndex(fn, FunctionAttributeIndex, LLVMCreateEnumAttribute(context, kind, 0));
    }
}
