using Csharpc.Llvm;
using Csharpc.Frontend.Il;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Runtime.CompilerServices.Unsafe called from IL. CoreLib declares these methods with bodies
/// the JIT never runs (they are [Intrinsic]: `throw new PlatformNotSupportedException()` standing in for
/// `ldarg.0; ret`), so each one is lowered here to the IL it means. Framework assemblies compiled from IL
/// (System.Reflection.Metadata, System.Collections.Immutable, Roslyn) use them on their hot paths.
/// Managed pointers are plain LLVM pointers and the GC is conservative and non-moving (ADR 0004), so
/// reinterpretation and byref arithmetic need no GC cooperation.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private IlEntry? IlUnsafeCall(IMethodSymbol method, IlEntry[] args)
    {
        var ta = method.TypeArguments;
        var parameters = method.OriginalDefinition.Parameters;
        bool IsPointer(int i) => parameters[i].Type is IPointerTypeSymbol;
        ITypeSymbol T0() => ta[0];
        LLVMValueRef ByteOffset(IlEntry e, ITypeSymbol parameterType) =>
            parameterType.SpecialType == SpecialType.System_Int32 ? IlToI64(e, signed: true) : IlToI64(e, signed: parameterType.SpecialType != SpecialType.System_UIntPtr);
        var handled = new IlEntry(IlKind.Token, default, null);

        switch (method.Name)
        {
            case "As" when ta.Length == 1 && args.Length == 1: // object -> T (no type check)
                return new IlEntry(IlKind.Ref, IlRef(args[0]), T0()) { NonNull = args[0].NonNull };
            case "As" when ta.Length == 2 && args.Length == 1: // ref TFrom -> ref TTo
                return new IlEntry(IlKind.ByRef, IlAddress(args[0]), ta[1]);
            case "AsRef" when ta.Length == 1 && args.Length == 1: // void* / ref readonly T -> ref T
                return new IlEntry(IlKind.ByRef, IlAddress(args[0]), T0());
            case "AsPointer" when ta.Length == 1 && args.Length == 1:
                return new IlEntry(IlKind.Pointer, IlAddress(args[0]), null);
            case "NullRef" when ta.Length == 1 && args.Length == 0:
                return new IlEntry(IlKind.ByRef, LlvmContext.Null(L.Ptr), T0());
            case "IsNullRef" when args.Length == 1:
                return I4Value(_b.Cast(LLVMOpcode.ZExt, _b.ICmp(LLVMIntPredicate.EQ, IlAddress(args[0]), LlvmContext.Null(L.Ptr)), L.I32), Special(SpecialType.System_Boolean));
            case "AreSame" when args.Length == 2:
                return I4Value(_b.Cast(LLVMOpcode.ZExt, _b.ICmp(LLVMIntPredicate.EQ, IlAddress(args[0]), IlAddress(args[1])), L.I32), Special(SpecialType.System_Boolean));
            case "IsAddressGreaterThan" or "IsAddressLessThan" or "IsAddressGreaterThanOrEqualTo" or "IsAddressLessThanOrEqualTo" when args.Length == 2:
            {
                var predicate = method.Name switch
                {
                    "IsAddressGreaterThan" => LLVMIntPredicate.UGT,
                    "IsAddressLessThan" => LLVMIntPredicate.ULT,
                    "IsAddressGreaterThanOrEqualTo" => LLVMIntPredicate.UGE,
                    _ => LLVMIntPredicate.ULE,
                };
                var l = _b.Cast(LLVMOpcode.PtrToInt, IlAddress(args[0]), L.I64);
                var r = _b.Cast(LLVMOpcode.PtrToInt, IlAddress(args[1]), L.I64);
                return I4Value(_b.Cast(LLVMOpcode.ZExt, _b.ICmp(predicate, l, r), L.I32), Special(SpecialType.System_Boolean));
            }
            case "ByteOffset" when args.Length == 2: // target - origin
            {
                var origin = _b.Cast(LLVMOpcode.PtrToInt, IlAddress(args[0]), L.I64);
                var target = _b.Cast(LLVMOpcode.PtrToInt, IlAddress(args[1]), L.I64);
                return new IlEntry(IlKind.NativeInt, _b.BinOp(LLVMOpcode.Sub, target, origin), Special(SpecialType.System_IntPtr));
            }
            case "Add" or "Subtract" when ta.Length == 1 && args.Length == 2:
            {
                // Element offsets scale by sizeof(T); `void*` overloads return void*.
                var count = ByteOffset(args[1], parameters[1].Type);
                if (method.Name == "Subtract") count = _b.Neg(count);
                var size = L.ConstInt(L.I64, (long)T.Target.SizeOf(T.Storage(S(T0()))));
                var address = _b.Gep(L.I8, IlAddress(args[0]), [_b.BinOp(LLVMOpcode.Mul, count, size)]);
                return IsPointer(0) ? new IlEntry(IlKind.Pointer, address, null) : new IlEntry(IlKind.ByRef, address, T0());
            }
            case "AddByteOffset" or "SubtractByteOffset" when ta.Length == 1 && args.Length == 2:
            {
                var offset = ByteOffset(args[1], parameters[1].Type);
                if (method.Name == "SubtractByteOffset") offset = _b.Neg(offset);
                return new IlEntry(IlKind.ByRef, _b.Gep(L.I8, IlAddress(args[0]), [offset]), T0());
            }
            case "SizeOf" when ta.Length == 1 && args.Length == 0:
                return I4((int)T.Target.SizeOf(T.Storage(S(T0()))));
            case "Read" or "ReadUnaligned" when ta.Length == 1 && args.Length == 1:
                return IlPush(IlLoadAligned(IlAddress(args[0]), T0(), method.Name == "ReadUnaligned"), T0());
            case "Write" or "WriteUnaligned" when ta.Length == 1 && args.Length == 2:
                IlStoreAligned(IlAddress(args[0]), args[1], T0(), method.Name == "WriteUnaligned");
                return handled;
            case "Copy" when ta.Length == 1 && args.Length == 2: // Copy<T>(void* destination, ref readonly T source) / Copy<T>(ref T destination, void* source)
                _b.Store(_b.Load(T.Storage(S(T0())), IlAddress(args[1])), IlAddress(args[0]));
                return handled;
            case "CopyBlock" or "CopyBlockUnaligned" when args.Length == 3:
            {
                var f = _m.Intrinsic("llvm.memmove.p0.p0.i64", L.Void, L.Ptr, L.Ptr, L.I64, L.I1);
                _b.Call(f.Type, f.Fn, [IlAddress(args[0]), IlAddress(args[1]), IlToI64(args[2], signed: false), L.False]);
                return handled;
            }
            case "InitBlock" or "InitBlockUnaligned" when args.Length == 3:
            {
                var f = _m.Intrinsic("llvm.memset.p0.i64", L.Void, L.Ptr, L.I8, L.I64, L.I1);
                _b.Call(f.Type, f.Fn, [IlAddress(args[0]), _b.Cast(LLVMOpcode.Trunc, IlInt(args[1]).Value, L.I8), IlToI64(args[2], signed: false), L.False]);
                return handled;
            }
            case "SkipInit" when args.Length == 1:
                return handled;
            case "Unbox" when ta.Length == 1 && args.Length == 1:
                return new IlEntry(IlKind.ByRef, CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], IlRef(args[0]), MethodTableFor(S(T0()), IlAt())), T0());
            case "BitCast" when ta.Length == 2 && args.Length == 1:
            {
                // Same-size value types: reinterpret the bits through memory.
                var (from, to) = (S(ta[0]), S(ta[1]));
                if (T.Target.SizeOf(T.Storage(from)) != T.Target.SizeOf(T.Storage(to)))
                    return null; // .NET throws NotSupportedException; let the library body (which does) run
                var temp = Spill(ToStorage(IlTyped(args[0], ta[0]), from));
                return IlPush(LoadValue(temp, to), ta[1]);
            }
        }
        throw new IlException($"'{method}' is not supported by csharpc yet");
    }

    private LLVMValueRef IlLoadAligned(LLVMValueRef address, ITypeSymbol type, bool unaligned)
    {
        var st = S(type);
        var value = _b.Load(T.Storage(st), address);
        // Alignment 1 keeps LLVM from assuming (and vectorizing on) the natural alignment.
        if (unaligned) Native.LLVMSetAlignment(value, 1);
        return st.SpecialType == SpecialType.System_Boolean ? _b.Cast(LLVMOpcode.Trunc, value, L.I1) : value;
    }

    private void IlStoreAligned(LLVMValueRef address, IlEntry value, ITypeSymbol type, bool unaligned)
    {
        var st = S(type);
        var store = _b.Store(ToStorage(IlTyped(value, type), st), address);
        if (unaligned) Native.LLVMSetAlignment(store, 1);
    }
}
