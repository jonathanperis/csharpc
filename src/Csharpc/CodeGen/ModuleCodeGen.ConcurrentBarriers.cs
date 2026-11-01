using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// The concurrent marker's write barrier (--gc-concurrent, docs/adr/0020-concurrent-marking.md): the same
/// pass over the finished module as the nursery's (ModuleCodeGen.WriteBarriers.cs), so it finds the same
/// stores, but each one costs a flag test while no cycle marks:
///
///   store ptr %v, %heap        -> csharpc.gc.barrier(%v); store         (log %v while marking)
///   store {..ptr..} / memcpy / memmove into the heap, atomics on pointers
///                              -> csharpc.gc.barrier_range(%heap, n); store
///
/// The two helpers are internal always-inline functions defined here: `if (csrt_gc_marking) call` with
/// the call out of line, so the optimizer leaves a load and a not-taken branch at each store. The flag
/// is read volatile: a loop that runs while a cycle starts must see it change, so the load may not be
/// hoisted out of the loop.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private LoweredFunction _barrierValue, _barrierRange;

    public void InsertConcurrentBarriers()
    {
        var (value, range) = ConcurrentBarrierHelpers();
        var pointerSize = (long)Types.PointerSize;
        using var b = new IrBuilder(Llvm);
        for (var fn = LLVMGetFirstFunction(Module.Ref); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0 || fn.Handle == value.Fn.Handle || fn.Handle == range.Fn.Handle) continue;
            for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
            {
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull; inst = LLVMGetNextInstruction(inst))
                {
                    switch (LLVMGetInstructionOpcode(inst))
                    {
                        case LLVMOpcode.Store:
                        {
                            var stored = LLVMGetOperand(inst, 0);
                            var address = LLVMGetOperand(inst, 1);
                            if (IsRootAddress(address)) break;
                            var type = stored.Type;
                            if (type.IsPointer)
                            {
                                if (LLVMIsConstant(stored) != 0) break;
                                Before(b, inst);
                                Located(inst, b.Call(value.Type, value.Fn, [stored]));
                            }
                            else if (HoldsPointer(type))
                            {
                                Before(b, inst);
                                Located(inst, b.Call(range.Type, range.Fn, [address, Llvm.ConstInt(Llvm.I64, (long)Types.Target.SizeOf(type))]));
                            }
                            break;
                        }
                        case LLVMOpcode.AtomicCmpXchg:
                        {
                            var address = LLVMGetOperand(inst, 0);
                            var stored = LLVMGetOperand(inst, 2);
                            if (stored.Type.IsPointer && !IsRootAddress(address) && LLVMIsConstant(stored) == 0)
                            {
                                Before(b, inst);
                                Located(inst, b.Call(value.Type, value.Fn, [stored]));
                            }
                            break;
                        }
                        case LLVMOpcode.AtomicRMW:
                        {
                            var address = LLVMGetOperand(inst, 0);
                            var stored = LLVMGetOperand(inst, 1);
                            if (stored.Type.IsPointer && !IsRootAddress(address) && LLVMIsConstant(stored) == 0)
                            {
                                Before(b, inst);
                                Located(inst, b.Call(value.Type, value.Fn, [stored]));
                            }
                            break;
                        }
                        case LLVMOpcode.Call or LLVMOpcode.Invoke:
                        {
                            var callee = LLVMIsAFunction(LLVMGetCalledValue(inst));
                            if (callee.IsNull || LLVMGetIntrinsicID(callee) == 0) break;
                            var name = LLVMGetValueName(callee);
                            if (!name.StartsWith("llvm.memcpy", StringComparison.Ordinal) && !name.StartsWith("llvm.memmove", StringComparison.Ordinal)) break;
                            var destination = LLVMGetOperand(inst, 0);
                            if (IsRootAddress(destination)) break;
                            var length = LLVMGetOperand(inst, 2);
                            Before(b, inst);
                            if (length.Type.IntWidth != 64) length = b.Cast(LLVMOpcode.ZExt, length, Llvm.I64);
                            Located(inst, b.Call(range.Type, range.Fn, [destination, length]));
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>The inline helper for an atomic reference store lowered as an integer (Interlocked on references).</summary>
    internal void EmitConcurrentBarrier(IrBuilder b, LLVMValueRef value)
    {
        var (helper, _) = ConcurrentBarrierHelpers();
        b.Call(helper.Type, helper.Fn, [value]);
    }

    private (LoweredFunction Value, LoweredFunction Range) ConcurrentBarrierHelpers()
    {
        if (!_barrierValue.Fn.IsNull) return (_barrierValue, _barrierRange);
        var flag = LLVMGetNamedGlobal(Module.Ref, "csrt_gc_marking");
        if (flag.IsNull) flag = Module.AddGlobal("csrt_gc_marking", Llvm.I8);
        var log = Runtime("csrt_gc_log", Llvm.Void, Llvm.Ptr);
        var logRange = Runtime("csrt_gc_log_range", Llvm.Void, Llvm.Ptr, Llvm.I64);
        _barrierValue = BarrierHelper("csharpc.gc.barrier", flag, log, [Llvm.Ptr]);
        _barrierRange = BarrierHelper("csharpc.gc.barrier_range", flag, logRange, [Llvm.Ptr, Llvm.I64]);
        return (_barrierValue, _barrierRange);
    }

    private LoweredFunction BarrierHelper(string name, LLVMValueRef flag, LoweredFunction slow, LLVMTypeRef[] parameters)
    {
        var type = Llvm.FunctionType(Llvm.Void, parameters);
        var fn = Module.AddFunction(name, type);
        LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "alwaysinline");
        Llvm.AddFunctionAttribute(fn, "nounwind");
        using var b = new IrBuilder(Llvm);
        var entry = b.AppendBlock(fn, "entry");
        var call = b.AppendBlock(fn, "marking");
        var done = b.AppendBlock(fn, "done");
        b.PositionAtEnd(entry);
        var marking = b.Load(Llvm.I8, flag, "marking");
        LLVMSetVolatile(marking, 1);
        b.CondBr(b.ICmp(LLVMIntPredicate.NE, marking, Llvm.ConstInt(Llvm.I8, 0)), call, done);
        b.PositionAtEnd(call);
        var args = Enumerable.Range(0, parameters.Length).Select(i => fn.Param((uint)i)).ToArray();
        b.Call(slow.Type, slow.Fn, args);
        b.Br(done);
        b.PositionAtEnd(done);
        b.RetVoid();
        return new LoweredFunction(fn, type);
    }
}
