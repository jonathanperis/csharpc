using System.Collections;
using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.Wasm;

/// <summary>
/// GC roots on wasm32 (docs/adr/0010-wasm32.md). csrt's collector is conservative: it finds references
/// by scanning memory. On native targets every live value is in memory or in a register spilled by
/// setjmp; in WebAssembly, values held in locals and on the operand stack live inside the engine,
/// where a scan of linear memory can't see them. So after optimization this pass gives every value
/// that may hold a reference and is live across a call that may collect a slot in its function's
/// linear-memory stack frame, and stores the value there (volatile) right where it is defined. A slot
/// always holds the latest value of its SSA value, and keeps it until the function returns, so every
/// object a frame can still reach (directly, or through an interior pointer derived from it) is in
/// memory whenever a collection runs. The collector scans the stack from its own frame up to
/// __stack_high, plus the data segment (gc.c).
///
/// "May collect": calls that can reach csrt_gc_collect_now (or make an indirect call). "May hold a
/// reference": pointers, aggregates and vectors containing pointers, and integers made from pointers
/// (ptrtoint).
/// </summary>
public static class RootSpilling
{
    private const string Collector = "csrt_gc_collect_now";

    public static void Run(LlvmModule module, LlvmContext llvm)
    {
        var m = module.Ref;
        var collector = LLVMGetNamedFunction(m, Collector);
        if (collector.IsNull || LLVMIsDeclaration(collector) != 0) return; // no GC in this program
        var mayCollect = new Ir.CallGraph(m).Reaching([collector.Handle]);
        bool CallMayCollect(LLVMValueRef call)
        {
            if (Ir.IsInlineAsmCall(call)) return false;
            var callee = Ir.DirectCallee(call);
            return callee.IsNull || mayCollect.Contains(callee.Handle);
        }
        using var b = new IrBuilder(llvm);
        foreach (var fn in Ir.DefinedFunctions(m))
            if (mayCollect.Contains(fn.Handle)) // functions that can't collect have no safepoints
                SpillFunction(fn, llvm, b, CallMayCollect);
    }

    private static void SpillFunction(LLVMValueRef fn, LlvmContext llvm, IrBuilder b, Func<LLVMValueRef, bool> mayCollect)
    {
        var blocks = Ir.Blocks(fn);
        // Candidate values: arguments and instructions that may hold a reference.
        Dictionary<nint, int> index = [];
        List<LLVMValueRef> values = [];
        void Consider(LLVMValueRef v)
        {
            if (MayHoldReference(v)) { index[v.Handle] = values.Count; values.Add(v); }
        }
        for (var i = 0u; i < LLVMCountParams(fn); i++) Consider(LLVMGetParam(fn, i));
        var hasSafepoint = false;
        foreach (var bb in blocks)
            for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
            {
                Consider(inst);
                if (Ir.Opcode(inst) == LLVMOpcode.Call && mayCollect(inst)) hasSafepoint = true;
            }
        if (!hasSafepoint || values.Count == 0) return;

        // Liveness (backward dataflow over the blocks; phi operands are live at the end of their
        // incoming block, not at the start of the phi's block).
        var n = values.Count;
        var blockIndex = blocks.Select((bb, i) => (bb, i)).ToDictionary(p => p.bb.Handle, p => p.i);
        var use = new BitArray[blocks.Count];
        var def = new BitArray[blocks.Count];
        var phiUses = new List<(int From, int Value)>[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            use[i] = new BitArray(n);
            def[i] = new BitArray(n);
            phiUses[i] = [];
        }
        for (var i = 0; i < blocks.Count; i++)
            for (var inst = LLVMGetFirstInstruction(blocks[i]); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
            {
                if (Ir.Opcode(inst) == LLVMOpcode.PHI)
                {
                    for (var k = 0u; k < LLVMCountIncoming(inst); k++)
                        if (index.TryGetValue(LLVMGetIncomingValue(inst, k).Handle, out var v)
                            && blockIndex.TryGetValue(LLVMGetIncomingBlock(inst, k).Handle, out var from))
                            phiUses[i].Add((from, v));
                }
                else
                    foreach (var operand in Ir.Operands(inst))
                        if (index.TryGetValue(operand.Handle, out var v) && !def[i][v]) use[i][v] = true;
                if (index.TryGetValue(inst.Handle, out var d)) def[i][d] = true;
            }
        var liveIn = new BitArray[blocks.Count];
        var liveOut = new BitArray[blocks.Count];
        for (var i = 0; i < blocks.Count; i++) { liveIn[i] = new BitArray(n); liveOut[i] = new BitArray(n); }
        var successors = blocks.Select(bb => Ir.Successors(bb).Select(s => blockIndex[s.Handle]).ToArray()).ToArray();
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = blocks.Count - 1; i >= 0; i--)
            {
                var outSet = new BitArray(n);
                foreach (var s in successors[i])
                {
                    outSet.Or(liveIn[s]);
                    foreach (var (from, v) in phiUses[s]) if (from == i) outSet[v] = true;
                }
                var inSet = new BitArray(outSet).And(new BitArray(def[i]).Not()).Or(use[i]);
                if (!SameBits(inSet, liveIn[i]) || !SameBits(outSet, liveOut[i]))
                {
                    liveIn[i] = inSet;
                    liveOut[i] = outSet;
                    changed = true;
                }
            }
        }

        // Values live across a call that may collect.
        var spill = new BitArray(n);
        for (var i = 0; i < blocks.Count; i++)
        {
            var live = new BitArray(liveOut[i]);
            for (var inst = LLVMGetLastInstruction(blocks[i]); !inst.IsNull; inst = LLVMGetPreviousInstruction(inst))
            {
                var isPhi = Ir.Opcode(inst) == LLVMOpcode.PHI;
                if (index.TryGetValue(inst.Handle, out var d)) live[d] = false;
                if (Ir.Opcode(inst) == LLVMOpcode.Call && mayCollect(inst)) spill.Or(live);
                if (!isPhi)
                    foreach (var operand in Ir.Operands(inst))
                        if (index.TryGetValue(operand.Handle, out var v)) live[v] = true;
            }
        }

        // One slot per spilled value, in the entry block; a volatile store where the value is defined.
        var entry = LLVMGetEntryBasicBlock(fn);
        var firstInEntry = LLVMGetFirstInstruction(entry);
        for (var v = 0; v < n; v++)
        {
            if (!spill[v]) continue;
            var value = values[v];
            var type = LLVMTypeOf(value);
            b.PositionBefore(firstInEntry);
            var slot = b.Alloca(type, "gcroot");
            if (!LLVMIsAArgument(value).IsNull)
                b.PositionBefore(firstInEntry);
            else if (Ir.Opcode(value) == LLVMOpcode.PHI)
                b.PositionBefore(FirstNonPhi(LLVMGetInstructionParent(value)));
            else
                b.PositionBefore(LLVMGetNextInstruction(value));
            var store = b.Store(value, slot);
            LLVMSetVolatile(store, 1);
        }
    }

    private static LLVMValueRef FirstNonPhi(LLVMBasicBlockRef bb)
    {
        var inst = LLVMGetFirstInstruction(bb);
        while (Ir.Opcode(inst) == LLVMOpcode.PHI) inst = LLVMGetNextInstruction(inst);
        return inst;
    }

    private static bool SameBits(BitArray a, BitArray b)
    {
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool MayHoldReference(LLVMValueRef value)
    {
        if (!LLVMIsAAllocaInst(value).IsNull) return false; // the stack frame itself
        if (!LLVMIsAInstruction(value).IsNull && Ir.Opcode(value) == LLVMOpcode.PtrToInt) return true;
        return TypeMayHoldReference(LLVMTypeOf(value));
    }

    private static unsafe bool TypeMayHoldReference(LLVMTypeRef type)
    {
        switch (type.Kind)
        {
            case LLVMTypeKind.Pointer: return true;
            case LLVMTypeKind.Vector: return TypeMayHoldReference(LLVMGetElementType(type));
            case LLVMTypeKind.Array: return TypeMayHoldReference(LLVMGetElementType(type));
            case LLVMTypeKind.Struct:
                var count = LLVMCountStructElementTypes(type);
                var elements = new LLVMTypeRef[count];
                fixed (LLVMTypeRef* p = elements) LLVMGetStructElementTypes(type, p);
                return elements.Any(TypeMayHoldReference);
            default: return false;
        }
    }
}
