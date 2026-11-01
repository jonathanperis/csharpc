using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// --gc-concurrent, after the optimizer: drops the barrier of each store whose value is fresh — null, or
/// an object this thread has held in a register or on its stack since it allocated it (an allocation
/// call, a call to a function that only returns such objects, a phi or select of them). Such a value is
/// marked whenever a cycle runs: allocated before the cycle's initial mark, it was a root of that mark;
/// allocated during the cycle, it is black (gc.c, allocate-black). Logging it would only repeat that.
/// The barrier only matters for values loaded from the heap after the initial mark, and those keep it.
///
/// This runs after inlining, so a constructor's `this.Left = left` sees that `left` is the result of
/// `new` in its caller (binarytrees' nodes: every barrier goes). The inlined helper leaves
/// `load volatile @csrt_gc_marking; br; call @csrt_gc_log(%v)`: the call is erased here, simplifycfg
/// folds the branch, and the flag loads left without users are erased (volatile: LLVM keeps them).
/// With runtime LTO the allocator is inlined; its result is recognized by the marker gc.c puts on it.
/// </summary>
public static class ConcurrentBarrierElision
{
    private static readonly HashSet<string> s_allocators =
        ["csrt_alloc", "csrt_alloc_noclear", "csrt_new_array", "csrt_new_array_primitive",
         "csrt_new_array_primitive_noclear", "csrt_string_alloc_checked", "csrt_string_alloc_zeroed"];

    public static void Run(LLVMModuleRef module, TargetMachine target)
    {
        var log = LLVMGetNamedFunction(module, "csrt_gc_log");
        var flag = LLVMGetNamedGlobal(module, "csrt_gc_marking");
        var calls = new List<LLVMValueRef>();
        if (!log.IsNull && !flag.IsNull)
        {
            var fresh = FreshFunctions(module);
            for (var use = LLVMGetFirstUse(log); use != 0; use = LLVMGetNextUse(use))
            {
                var call = LLVMGetUser(use);
                if (LLVMGetInstructionOpcode(call) == LLVMOpcode.Call && LLVMGetCalledValue(call).Handle == log.Handle
                    && IsFresh(LLVMGetOperand(call, 0), fresh, []))
                    calls.Add(call);
            }
            foreach (var call in calls) LLVMInstructionEraseFromParent(call);
        }
        StripMarkers(module);
        if (calls.Count == 0) return;
        LlvmModule.RunPasses(module, "simplifycfg", target);
        var dead = new List<LLVMValueRef>();
        for (var use = LLVMGetFirstUse(flag); use != 0; use = LLVMGetNextUse(use))
        {
            var load = LLVMGetUser(use);
            if (LLVMIsAInstruction(load).IsNull || LLVMGetInstructionOpcode(load) != LLVMOpcode.Load) continue;
            // simplifycfg leaves the test (icmp) of a folded branch behind too.
            var test = LLVMGetFirstUse(load);
            if (test != 0 && LLVMGetNextUse(test) == 0)
            {
                var icmp = LLVMGetUser(test);
                if (LLVMGetInstructionOpcode(icmp) == LLVMOpcode.ICmp && LLVMGetFirstUse(icmp) == 0) dead.Add(icmp);
                else continue;
            }
            dead.Add(load);
        }
        foreach (var inst in dead) LLVMInstructionEraseFromParent(inst);
    }

    /// <summary>The runtime's allocator marks what it returns (gc.c, FRESH): an empty asm, gone from the output.</summary>
    private static bool IsMarker(LLVMValueRef call)
    {
        var asm = LLVMIsAInlineAsm(LLVMGetCalledValue(call));
        if (asm.IsNull) return false;
        var text = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(LLVMGetInlineAsmAsmString(asm, out var length), (int)length);
        return text == Marker;
    }

    private const string Marker = "/* csrt.fresh */";

    private static void StripMarkers(LLVMModuleRef module)
    {
        var markers = new List<LLVMValueRef>();
        for (var fn = LLVMGetFirstFunction(module); !fn.IsNull; fn = LLVMGetNextFunction(fn))
            for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                    if (LLVMGetInstructionOpcode(inst) == LLVMOpcode.Call && IsMarker(inst)) markers.Add(inst);
        foreach (var marker in markers)
        {
            LLVMReplaceAllUsesWith(marker, LLVMGetOperand(marker, 0));
            LLVMInstructionEraseFromParent(marker);
        }
    }

    /// <summary>
    /// Facts, as handles: defined functions whose every return value is fresh, and parameters whose every
    /// argument is (of internal functions only called directly: a constructor's `left` when every `new`
    /// passes a new object, which matters where the constructor is not inlined). The greatest fixpoint:
    /// recursion is fine, the value is still held from its allocation to the store.
    /// </summary>
    private static HashSet<IntPtr> FreshFunctions(LLVMModuleRef module)
    {
        var returns = new Dictionary<IntPtr, List<LLVMValueRef>>();
        var arguments = new Dictionary<IntPtr, List<LLVMValueRef>>();
        for (var fn = LLVMGetFirstFunction(module); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0) continue;
            if (LLVMGetReturnType(LLVMGlobalGetValueType(fn)).IsPointer)
            {
                var returned = new List<LLVMValueRef>();
                for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
                {
                    var term = LLVMGetBasicBlockTerminator(bb);
                    if (!term.IsNull && LLVMGetInstructionOpcode(term) == LLVMOpcode.Ret && LLVMGetNumOperands(term) == 1)
                        returned.Add(LLVMGetOperand(term, 0));
                }
                returns[fn.Handle] = returned;
            }
            var linkage = LLVMGetLinkage(fn);
            if (linkage is not (LLVMLinkage.Internal or LLVMLinkage.Private)) continue;
            uint count = LLVMCountParams(fn);
            if (count == 0) continue;
            var sites = new List<LLVMValueRef>();
            bool direct = true;
            for (var use = LLVMGetFirstUse(fn); use != 0 && direct; use = LLVMGetNextUse(use))
            {
                var user = LLVMGetUser(use);
                direct = !LLVMIsAInstruction(user).IsNull
                    && LLVMGetInstructionOpcode(user) is LLVMOpcode.Call or LLVMOpcode.Invoke
                    && LLVMGetCalledValue(user).Handle == fn.Handle && LLVMGetNumArgOperands(user) == count;
                if (direct) sites.Add(user);
            }
            if (!direct || sites.Count == 0) continue;
            for (uint i = 0; i < count; i++)
            {
                var param = LLVMGetParam(fn, i);
                if (!param.Type.IsPointer) continue;
                arguments[param.Handle] = sites.Select(site => LLVMGetOperand(site, i)).ToList();
            }
        }
        var fresh = new HashSet<IntPtr>(returns.Keys);
        fresh.UnionWith(arguments.Keys);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var facts in (Dictionary<IntPtr, List<LLVMValueRef>>[])[returns, arguments])
                foreach (var (fact, values) in facts)
                    if (fresh.Contains(fact) && !values.All(v => IsFresh(v, fresh, [])))
                    {
                        fresh.Remove(fact);
                        changed = true;
                    }
        }
        return fresh;
    }

    private static bool IsFresh(LLVMValueRef value, HashSet<IntPtr> freshFunctions, HashSet<IntPtr> visiting)
    {
        if (!LLVMIsAConstantPointerNull(value).IsNull || !LLVMIsAUndefValue(value).IsNull) return true;
        if (!LLVMIsAArgument(value).IsNull) return freshFunctions.Contains(value.Handle);
        if (LLVMIsAInstruction(value).IsNull) return false;
        switch (LLVMGetInstructionOpcode(value))
        {
            case LLVMOpcode.Call or LLVMOpcode.Invoke:
            {
                if (IsMarker(value)) return true;
                var callee = LLVMIsAFunction(LLVMGetCalledValue(value));
                if (callee.IsNull) return false;
                if (LLVMIsDeclaration(callee) != 0) return s_allocators.Contains(LLVMGetValueName(callee));
                return freshFunctions.Contains(callee.Handle);
            }
            case LLVMOpcode.PHI:
            {
                // A loop's phi refers to itself: assume it fresh while its other inputs are checked.
                if (!visiting.Add(value.Handle)) return true;
                for (uint i = 0, n = LLVMCountIncoming(value); i < n; i++)
                    if (!IsFresh(LLVMGetIncomingValue(value, i), freshFunctions, visiting)) return false;
                return true;
            }
            case LLVMOpcode.Select:
                return IsFresh(LLVMGetOperand(value, 1), freshFunctions, visiting) && IsFresh(LLVMGetOperand(value, 2), freshFunctions, visiting);
            default:
                return false;
        }
    }
}
