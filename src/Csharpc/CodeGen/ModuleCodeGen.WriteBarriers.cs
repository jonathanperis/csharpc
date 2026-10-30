using Csharpc.Llvm;
using static Csharpc.Llvm.Native;

namespace Csharpc.CodeGen;

/// <summary>
/// The generational write barrier (--gc-nursery, docs/adr/0004-memory-management.md): one pass over the
/// finished module, so every store csharpc emits is covered whichever builder made it — the function
/// lowering, the module's thunks and wrappers, the IL front-end, the corelib. A barrier placed at each
/// emission site missed aggregate stores, memcpy/memmove of values that hold references (Span.CopyTo,
/// struct copies) and the builders that had no hook.
///
///   store ptr %v, %heap        -> csrt_gc_write(%heap, %v)            (dirty the card, then store)
///   store {..ptr..} %v, %heap  -> csrt_gc_cards_dirty(%heap, size); store
///   atomic/volatile ptr store, cmpxchg, atomicrmw of a ptr -> csrt_gc_cards_dirty(%heap, 8) first
///   llvm.memcpy/memmove(%heap, ...) -> csrt_gc_cards_dirty(%heap, n) first
///
/// Stores into allocas and globals are root writes (stacks and data segments are scanned by every
/// collection) and need none; so are stores of constants (null, method tables, literals). Runs before
/// any optimization, so an address is still visibly an alloca when it is one. Coroutine frames are the
/// exception the pass cannot see: CoroSplit turns allocas that live across a suspension into frame
/// fields later. The runtime remembers frames instead (csrt_gc_remember): minors scan them all.
/// </summary>
public sealed partial class ModuleCodeGen
{
    public void InsertWriteBarriers()
    {
        var write = Runtime("csrt_gc_write", Llvm.Void, Llvm.Ptr, Llvm.Ptr);
        var dirty = Runtime("csrt_gc_cards_dirty", Llvm.Void, Llvm.Ptr, Llvm.I64);
        var pointerSize = (long)Types.PointerSize;
        using var b = new IrBuilder(Llvm);
        for (var fn = LLVMGetFirstFunction(Module.Ref); !fn.IsNull; fn = LLVMGetNextFunction(fn))
        {
            if (LLVMIsDeclaration(fn) != 0 || fn.Handle == write.Fn.Handle || fn.Handle == dirty.Fn.Handle) continue;
            for (var bb = LLVMGetFirstBasicBlock(fn); !bb.IsNull; bb = LLVMGetNextBasicBlock(bb))
            {
                for (var inst = LLVMGetFirstInstruction(bb); !inst.IsNull;)
                {
                    var next = LLVMGetNextInstruction(inst);
                    switch (LLVMGetInstructionOpcode(inst))
                    {
                        case LLVMOpcode.Store:
                        {
                            var value = LLVMGetOperand(inst, 0);
                            var address = LLVMGetOperand(inst, 1);
                            var type = value.Type;
                            if (IsRootAddress(address)) break;
                            if (type.IsPointer)
                            {
                                if (LLVMIsConstant(value) != 0) break;
                                if (LLVMGetOrdering(inst) == LLVMAtomicOrdering.NotAtomic && LLVMGetVolatile(inst) == 0)
                                {
                                    Before(b, inst);
                                    Located(inst, b.Call(write.Type, write.Fn, [address, value]));
                                    LLVMInstructionEraseFromParent(inst);
                                }
                                else Dirty(b, dirty, inst, address, Llvm.ConstInt(Llvm.I64, pointerSize));
                            }
                            else if (HoldsPointer(type))
                                Dirty(b, dirty, inst, address, Llvm.ConstInt(Llvm.I64, (long)Types.Target.SizeOf(type)));
                            break;
                        }
                        case LLVMOpcode.AtomicCmpXchg:
                        {
                            var address = LLVMGetOperand(inst, 0);
                            if (LLVMGetOperand(inst, 2).Type.IsPointer && !IsRootAddress(address))
                                Dirty(b, dirty, inst, address, Llvm.ConstInt(Llvm.I64, pointerSize));
                            break;
                        }
                        case LLVMOpcode.AtomicRMW:
                        {
                            var address = LLVMGetOperand(inst, 0);
                            if (LLVMGetOperand(inst, 1).Type.IsPointer && !IsRootAddress(address))
                                Dirty(b, dirty, inst, address, Llvm.ConstInt(Llvm.I64, pointerSize));
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
                            Dirty(b, dirty, inst, destination, length);
                            break;
                        }
                    }
                    inst = next;
                }
            }
        }
    }

    private static void Before(IrBuilder b, LLVMValueRef inst) => LLVMPositionBuilderBefore(b.Ref, inst);

    private static void Located(LLVMValueRef original, LLVMValueRef inserted)
    {
        var location = LLVMInstructionGetDebugLoc(original);
        if (location.Handle != 0) LLVMInstructionSetDebugLoc(inserted, location);
    }

    /// <summary>Dirty first: a collection between the two steps then still scans the old object.</summary>
    private static void Dirty(IrBuilder b, LoweredFunction dirty, LLVMValueRef inst, LLVMValueRef address, LLVMValueRef bytes)
    {
        Before(b, inst);
        Located(inst, b.Call(dirty.Type, dirty.Fn, [address, bytes]));
    }

    /// <summary>A struct, array or vector with a pointer somewhere inside (a reference, or a byref/pointer field).</summary>
    private static bool HoldsPointer(LLVMTypeRef type)
    {
        switch (type.Kind)
        {
            case LLVMTypeKind.Pointer: return true;
            case LLVMTypeKind.Struct:
                for (uint i = 0, n = LLVMCountStructElementTypes(type); i < n; i++)
                    if (HoldsPointer(LLVMStructGetTypeAtIndex(type, i))) return true;
                return false;
            case LLVMTypeKind.Array:
            case LLVMTypeKind.Vector:
                return HoldsPointer(LLVMGetElementType(type));
            default: return false;
        }
    }

    /// <summary>An alloca or a global (or a GEP / bitcast of one): memory every collection scans as roots.</summary>
    internal static bool IsRootAddress(LLVMValueRef address)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            if (!LLVMIsAAllocaInst(address).IsNull) return true;
            if (!LLVMIsAGlobalValue(address).IsNull) return true;
            if (LLVMIsAInstruction(address).IsNull) return false;
            var op = LLVMGetInstructionOpcode(address);
            if (op is LLVMOpcode.GetElementPtr or LLVMOpcode.BitCast)
            {
                address = LLVMGetOperand(address, 0);
                continue;
            }
            return false; // a loaded reference, an argument, a call result: may be the heap
        }
        return false;
    }
}
