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
        var frameExit = Runtime("csrt_gc_frame_exit", Llvm.Void, Llvm.Ptr);
        var frameExitTop = Runtime("csrt_gc_frame_exit_top", Llvm.Void);
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
                                // A small aggregate (a span, a struct with a few references): a value barrier per
                                // reference it holds. The range barrier would pass the address to a call, and a
                                // store into a local once inlined (an `out` span) could no longer be promoted to
                                // registers (SROA): strbuild's IndexOf loop ran +60 % instructions.
                                var leaves = new List<uint[]>();
                                if (PointerLeaves(type, [], leaves))
                                    foreach (var path in leaves)
                                    {
                                        var field = stored;
                                        foreach (var index in path) field = b.ExtractValue(field, index);
                                        if (LLVMIsConstant(field) == 0) Located(inst, b.Call(value.Type, value.Fn, [field]));
                                    }
                                else Located(inst, b.Call(range.Type, range.Fn, [address, Llvm.ConstInt(Llvm.I64, (long)Types.Target.SizeOf(type))]));
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
                            if (!callee.IsNull && Wasm.Ir.HasEnumAttribute(callee, "presplitcoroutine") && callee.Handle != fn.Handle)
                            {
                                // The first run of an async method or iterator (its frame was entered when it was
                                // allocated): it has suspended once this call returns.
                                FirstRunEnded(b, inst, frameExit, frameExitTop);
                                break;
                            }
                            if (callee.IsNull || LLVMGetIntrinsicID(callee) == 0) break;
                            var name = LLVMGetValueName(callee);
                            if (!name.StartsWith("llvm.memcpy", StringComparison.Ordinal) && !name.StartsWith("llvm.memmove", StringComparison.Ordinal)) break;
                            var destination = LLVMGetOperand(inst, 0);
                            if (IsRootAddress(destination) || LLVMGetMetadata(inst, Facts.NoReferencesKind).Handle != 0) break;
                            // A copy out of constant data (InitializeArray's RVA blob, a struct literal) stores no
                            // heap object: those exist only at run time. Its barrier, inside a lazy initialization
                            // in a loop, kept LLVM from hoisting the loop's loads (strbuild's IndexOf: +60 %).
                            var source = LLVMGetOperand(inst, 1);
                            if (!LLVMIsAGlobalVariable(source).IsNull && LLVMIsGlobalConstant(source) != 0) break;
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

    /// <summary>The paths of the references in an aggregate, false when there are more than 8 or a vector holds some.</summary>
    private static bool PointerLeaves(LLVMTypeRef type, List<uint> path, List<uint[]> leaves)
    {
        switch (type.Kind)
        {
            case LLVMTypeKind.Pointer:
                if (leaves.Count == 8) return false;
                leaves.Add([.. path]);
                return true;
            case LLVMTypeKind.Struct:
                for (uint i = 0, n = LLVMCountStructElementTypes(type); i < n; i++)
                {
                    path.Add(i);
                    var ok = PointerLeaves(LLVMStructGetTypeAtIndex(type, i), path, leaves);
                    path.RemoveAt(path.Count - 1);
                    if (!ok) return false;
                }
                return true;
            case LLVMTypeKind.Array:
            {
                var element = LLVMGetElementType(type);
                if (!HoldsPointer(element)) return true;
                var length = LLVMGetArrayLength2(type);
                if (length > 8) return false;
                for (uint i = 0; i < length; i++)
                {
                    path.Add(i);
                    var ok = PointerLeaves(element, path, leaves);
                    path.RemoveAt(path.Count - 1);
                    if (!ok) return false;
                }
                return true;
            }
            case LLVMTypeKind.Vector:
                return !HoldsPointer(type);
            default:
                return true;
        }
    }

    /// <summary>After a coroutine's first run: csrt_gc_frame_exit(its task or frame), or the top frame for async void.</summary>
    private static void FirstRunEnded(IrBuilder b, LLVMValueRef call, LoweredFunction exit, LoweredFunction exitTop)
    {
        if (LLVMGetInstructionOpcode(call) == LLVMOpcode.Invoke)
        {
            var normal = LLVMGetNormalDest(call);
            var first = LLVMGetFirstInstruction(normal);
            while (!first.IsNull && LLVMGetInstructionOpcode(first) == LLVMOpcode.PHI) first = LLVMGetNextInstruction(first);
            if (first.IsNull) return;
            LLVMPositionBuilderBefore(b.Ref, first);
        }
        else
        {
            var next = LLVMGetNextInstruction(call);
            if (next.IsNull) return;
            LLVMPositionBuilderBefore(b.Ref, next);
        }
        if (call.Type.IsPointer) Located(call, b.Call(exit.Type, exit.Fn, [call]));
        else Located(call, b.Call(exitTop.Type, exitTop.Fn, []));
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
