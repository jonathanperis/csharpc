using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Wasm;

/// <summary>
/// Exceptions without an unwinder (docs/adr/0011-wasm32.md). Codegen emits the same Itanium-style IR
/// on every target: `invoke` + `landingpad` inside try regions, `call csrt_throw_object` +
/// `unreachable` for a throw. WASI has no unwinder, so on wasm32 this pass turns that IR into
/// explicit error propagation over the whole program (user code, corelib and the C runtime, which is
/// always linked in as bitcode on wasm):
///
///  * throwing stores the exception in the global <c>csrt_pending_exception</c> and returns normally;
///  * after every call that may throw, the caller tests the global: an `invoke` branches to its
///    landing-pad block (which reads and clears the global in csrt_begin_catch) or its normal
///    destination; a plain call branches to the function's propagate block, which returns at once
///    (the return value is never looked at: every caller tests the global first);
///  * in <c>main</c> the propagate block reports the unhandled exception.
///
/// Handler selection, `when` filters and finally blocks are unchanged: they are ordinary code in the
/// landing-pad blocks. Only calls whose callee can reach csrt_throw_object (or makes an indirect call)
/// get a test; after inlining LLVM folds the tests for code that provably doesn't throw.
/// </summary>
public static class ExceptionLowering
{
    public const string PendingGlobal = "csrt_pending_exception";

    /// <summary>C runtime functions that catch: they call something and then take the pending
    /// exception themselves (src/runtime/eh.c, async.c). Their calls are not tested. Also the
    /// stack-trace recorders, which landing-pad code calls while the exception is still pending
    /// (before csrt_begin_catch takes it): a test after them would propagate it past its handler.</summary>
    private static readonly HashSet<string> s_catching =
        ["csrt_trap_call", "csrt_call_catching_void", "csrt_call_catching_ptr", "csrt_trace_frame", "csrt_trace_finally"];

    public static void Run(LlvmModule module, LlvmContext llvm)
    {
        var m = module.Ref;
        // Throwing = storing an exception into the pending global. After clang's inlining that store
        // can be anywhere in the runtime (csrt_rethrow, the csrt_throw_* helpers), so look for it. A
        // program whose code never stores one (the global is gone) can't throw at all.
        var pending = LLVMGetNamedGlobal(m, PendingGlobal);
        var catching = Ir.DefinedFunctions(m).Where(fn => s_catching.Contains(Ir.Name(fn))).Select(fn => fn.Handle).ToHashSet();
        var throwers = pending.IsNull ? [] : Ir.DefinedFunctions(m).Where(fn => StoresException(fn, pending)).Select(fn => fn.Handle).ToList();
        var mayThrow = throwers.Count == 0 ? [] : new Ir.CallGraph(m).Reaching(throwers, except: catching);
        bool CallMayThrow(LLVMValueRef call)
        {
            if (throwers.Count == 0 || Ir.IsInlineAsmCall(call)) return false;
            var callee = Ir.DirectCallee(call);
            if (callee.IsNull) return true;                       // virtual / delegate / function pointer
            return mayThrow.Contains(callee.Handle);              // declarations (libc, WASI) never throw
        }

        using var b = new IrBuilder(llvm);
        foreach (var fn in Ir.DefinedFunctions(m))
            if (!catching.Contains(fn.Handle) || HasLandingPads(fn))
                new FunctionLowering(fn, llvm, b, pending, CallMayThrow, catching.Contains(fn.Handle)).Run();

        // Nothing unwinds any more: throwing functions return, so `noreturn` (on the C helpers and
        // on csharpc's declarations of csrt_throw*) would now be a lie that deletes the tests.
        // Memory-effect summaries (csharpc annotates runtime declarations) must not hide the write.
        uint[] stale = [Ir.KindOf("noreturn"), Ir.KindOf("memory")];
        foreach (var fn in Ir.Functions(m))
        {
            if (LLVMHasPersonalityFn(fn) != 0) LLVMSetPersonalityFn(fn, default);
            if (mayThrow.Contains(fn.Handle))
                foreach (var kind in stale) LLVMRemoveEnumAttributeAtIndex(fn, FunctionAttributeIndex, kind);
            if (LLVMIsDeclaration(fn) != 0) continue;
            foreach (var bb in Ir.Blocks(fn))
                foreach (var inst in Ir.Instructions(bb))
                    if (Ir.Opcode(inst) == LLVMOpcode.Call && CallMayThrow(inst))
                        foreach (var kind in stale) LLVMRemoveCallSiteEnumAttribute(inst, FunctionAttributeIndex, kind);
        }
    }

    private static bool HasLandingPads(LLVMValueRef fn) => LLVMHasPersonalityFn(fn) != 0;

    private static bool StoresException(LLVMValueRef fn, LLVMValueRef pending)
    {
        foreach (var bb in Ir.Blocks(fn))
            for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                if (Ir.Opcode(inst) == LLVMOpcode.Store && LLVMGetOperand(inst, 1) == pending && LLVMIsNull(LLVMGetOperand(inst, 0)) == 0)
                    return true;
        return false;
    }

    private sealed class FunctionLowering(LLVMValueRef fn, LlvmContext llvm, IrBuilder b, LLVMValueRef pending, Func<LLVMValueRef, bool> mayThrow, bool catching)
    {
        private LLVMBasicBlockRef _propagate;

        public void Run()
        {
            foreach (var bb in Ir.Blocks(fn))
            {
                var block = bb;
                for (var inst = LLVMGetFirstInstruction(block); !inst.IsNull;)
                {
                    var next = LLVMGetNextInstruction(inst);
                    switch (Ir.Opcode(inst))
                    {
                        case LLVMOpcode.Invoke:
                            LowerInvoke(inst);
                            break;
                        case LLVMOpcode.LandingPad:
                            LLVMReplaceAllUsesWith(inst, LLVMGetPoison(LLVMTypeOf(inst)));
                            LLVMInstructionEraseFromParent(inst);
                            break;
                        case LLVMOpcode.Resume:
                            var propagate = Propagate(); // (repositions the builder)
                            b.PositionBefore(inst);
                            b.Br(propagate);
                            LLVMInstructionEraseFromParent(inst);
                            break;
                        case LLVMOpcode.Call when !catching && mayThrow(inst):
                            // Everything up to and including the call moves to a new block that tests
                            // the pending exception; `block` keeps the rest (and its terminator, so the
                            // successors' phis stay valid).
                            SplitAfter(block, inst);
                            break;
                    }
                    inst = next;
                }
            }
        }

        private LLVMValueRef TestPending()
        {
            var exception = b.Load(llvm.Ptr, pending, "pending");
            return b.ICmp(LLVMIntPredicate.NE, exception, LlvmContext.Null(llvm.Ptr), "threw");
        }

        private void LowerInvoke(LLVMValueRef invoke)
        {
            var normal = LLVMGetNormalDest(invoke);
            var unwind = LLVMGetUnwindDest(invoke);
            b.PositionBefore(invoke);
            var call = CallLike(invoke);
            LLVMReplaceAllUsesWith(invoke, call);
            if (mayThrow(call)) b.CondBr(TestPending(), unwind, normal);
            else b.Br(normal);
            var name = Ir.Name(invoke);
            LLVMInstructionEraseFromParent(invoke); // last: the builder's position was before it
            if (name.Length > 0) call.SetName(name);
        }

        /// <summary>A `call` with the invoke's callee, arguments, calling convention, attributes and location.</summary>
        private unsafe LLVMValueRef CallLike(LLVMValueRef invoke)
        {
            var argCount = LLVMGetNumArgOperands(invoke);
            var args = new LLVMValueRef[argCount];
            for (var i = 0u; i < argCount; i++) args[i] = LLVMGetOperand(invoke, i);
            var call = LLVMBuildCall2(b.Ref, LLVMGetCalledFunctionType(invoke), LLVMGetCalledValue(invoke), args, argCount, "");
            LLVMSetInstructionCallConv(call, LLVMGetInstructionCallConv(invoke));
            foreach (var index in (uint[])[FunctionAttributeIndex, ReturnAttributeIndex, .. Enumerable.Range(1, (int)argCount).Select(i => (uint)i)])
            {
                var count = LLVMGetCallSiteAttributeCount(invoke, index);
                if (count == 0) continue;
                var attributes = new LLVMAttributeRef[count];
                fixed (LLVMAttributeRef* p = attributes) LLVMGetCallSiteAttributes(invoke, index, p);
                foreach (var attribute in attributes) LLVMAddCallSiteAttribute(call, index, attribute);
            }
            var location = LLVMInstructionGetDebugLoc(invoke);
            if (!location.IsNull) LLVMInstructionSetDebugLoc(call, location);
            return call;
        }

        /// <summary>
        /// Moves the instructions of <paramref name="block"/> up to <paramref name="call"/> into a new block
        /// placed before it (predecessors are redirected there; phis move along with their incoming
        /// blocks unchanged), and ends that block with the pending-exception test.
        /// </summary>
        private void SplitAfter(LLVMBasicBlockRef block, LLVMValueRef call)
        {
            var head = LLVMInsertBasicBlockInContext(llvm.Ref, block, "");
            // Redirect the predecessors' branches (not RAUW: that would also rewrite the phis of
            // `block`'s successors, whose incoming edge still comes from `block`).
            List<LLVMValueRef> branches = [];
            for (var use = LLVMGetFirstUse(LLVMBasicBlockAsValue(block)); use != 0; use = LLVMGetNextUse(use))
                branches.Add(LLVMGetUser(use));
            foreach (var branch in branches.Distinct())
                if (!LLVMIsAInstruction(branch).IsNull && Ir.Opcode(branch) is LLVMOpcode.Br or LLVMOpcode.Switch or LLVMOpcode.IndirectBr or LLVMOpcode.Invoke or LLVMOpcode.CallBr)
                    for (var i = 0u; i < LLVMGetNumSuccessors(branch); i++)
                        if (LLVMGetSuccessor(branch, i) == block) LLVMSetSuccessor(branch, i, head);
            b.PositionAtEnd(head);
            for (var inst = LLVMGetFirstInstruction(block); ;)
            {
                var next = LLVMGetNextInstruction(inst);
                var name = Ir.Name(inst);
                LLVMInstructionRemoveFromParent(inst);
                LLVMInsertIntoBuilderWithName(b.Ref, inst, name);
                if (inst == call) break;
                inst = next;
            }
            b.CondBr(TestPending(), Propagate(), block);
        }

        /// <summary>Returns to the caller, which tests the pending exception itself.</summary>
        private LLVMBasicBlockRef Propagate()
        {
            if (!_propagate.IsNull) return _propagate;
            var saved = b.InsertBlock;
            _propagate = b.AppendBlock(fn, "propagate");
            b.PositionAtEnd(_propagate);
            var returnType = LLVMGetReturnType(LLVMGlobalGetValueType(fn));
            if (Ir.Name(fn) == "main" || Ir.HasEnumAttribute(fn, "presplitcoroutine"))
            {
                // Nothing above to return to: the exception is unhandled. (A coroutine body catches
                // everything itself; only its allocation can fail before that.)
                var report = LLVMGetNamedFunction(LLVMGetGlobalParent(fn), "csrt_unhandled_pending");
                b.Call(llvm.FunctionType(llvm.Void), report, []);
                b.Unreachable();
            }
            else if (returnType.IsVoid) b.RetVoid();
            else b.Ret(LLVMGetPoison(returnType));
            if (!saved.IsNull) b.PositionAtEnd(saved);
            return _propagate;
        }
    }
}
