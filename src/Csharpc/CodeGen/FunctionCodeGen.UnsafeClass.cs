using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// System.Runtime.CompilerServices.Unsafe called from source. The reference assembly declares the members
/// without bodies, so each call is lowered here to the memory operation it means. The IL front-end has its
/// own entry point (FunctionCodeGen.IlUnsafe.cs) over IL stack entries; both build on the same helpers below.
/// Managed pointers are plain LLVM pointers and the GC is conservative and non-moving (ADR 0004), so
/// reinterpretation and byref arithmetic need no GC cooperation. A `ref` result is the address itself
/// (as for any ref-returning call); `void*` results are the same pointer.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>The result of a handled call that returns void (a null result means "not an Unsafe member").</summary>
    private static readonly LLVMValueRef? Handled = default(LLVMValueRef);

    private LLVMValueRef? TryUnsafeMethod(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.ContainingType is not { Name: "Unsafe", ContainingNamespace: { Name: "CompilerServices", ContainingNamespace.Name: "Runtime" } }) return null;
        var ta = target.TypeArguments;
        var arguments = inv.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => EmitArgument(a, target)).ToArray();
        LLVMValueRef Arg(int i) => arguments[i];
        // Offsets scale as C# converts them: int and nint sign-extend, nuint zero-extends.
        LLVMValueRef Offset(int i) => ExtendTo64(arguments[i], target.Parameters[i].Type.SpecialType != SpecialType.System_UIntPtr);

        switch (target.Name)
        {
            case "As" when ta.Length == 1 && arguments.Length == 1: // object -> T, no type check
                return Arg(0);
            case "As" when ta.Length == 2 && arguments.Length == 1: // ref TFrom -> ref TTo
            case "AsRef" when ta.Length == 1 && arguments.Length == 1: // void* / ref readonly T -> ref T
            case "AsPointer" when ta.Length == 1 && arguments.Length == 1:
                // A fresh value: the argument's address may be tagged as one particular field or element
                // (MemoryFacts), and the reinterpreted accesses must not inherit that TBAA tag.
                return UnsafeUntagged(Arg(0));
            case "NullRef" when ta.Length == 1 && arguments.Length == 0:
                return LlvmContext.Null(L.Ptr);
            case "IsNullRef" when arguments.Length == 1:
                return _b.ICmp(LLVMIntPredicate.EQ, Arg(0), LlvmContext.Null(L.Ptr));
            case "AreSame" when arguments.Length == 2:
                return _b.ICmp(LLVMIntPredicate.EQ, Arg(0), Arg(1));
            case "IsAddressGreaterThan" or "IsAddressLessThan" or "IsAddressGreaterThanOrEqualTo" or "IsAddressLessThanOrEqualTo" when arguments.Length == 2:
                return UnsafeAddressCompare(target.Name, Arg(0), Arg(1));
            case "ByteOffset" when arguments.Length == 2: // target - origin
                return UnsafeByteOffset(Arg(0), Arg(1), T.NativeInt);
            case "Add" or "Subtract" when ta.Length == 1 && arguments.Length == 2:
                return UnsafeElementAddress(Arg(0), Offset(1), S(ta[0]), subtract: target.Name == "Subtract");
            case "AddByteOffset" or "SubtractByteOffset" when ta.Length == 1 && arguments.Length == 2:
                return UnsafeByteAddress(Arg(0), Offset(1), subtract: target.Name == "SubtractByteOffset");
            case "SizeOf" when ta.Length == 1 && arguments.Length == 0:
                return L.ConstInt(L.I32, (long)T.Target.SizeOf(T.Storage(S(ta[0]))));
            case "Read" or "ReadUnaligned" when ta.Length == 1 && arguments.Length == 1:
                return UnsafeLoad(Arg(0), S(ta[0]), unaligned: target.Name == "ReadUnaligned");
            case "Write" or "WriteUnaligned" when ta.Length == 1 && arguments.Length == 2:
                UnsafeStore(Arg(0), Arg(1), S(ta[0]), unaligned: target.Name == "WriteUnaligned");
                return Handled;
            case "Copy" when ta.Length == 1 && arguments.Length == 2: // (void* destination, ref readonly T source) / (ref T destination, void* source)
                UnsafeStore(Arg(0), UnsafeLoad(Arg(1), S(ta[0]), unaligned: false), S(ta[0]), unaligned: false);
                return Handled;
            case "CopyBlock" or "CopyBlockUnaligned" when arguments.Length == 3:
                UnsafeCopyBlock(Arg(0), Arg(1), ExtendTo64(Arg(2), signed: false));
                return Handled;
            case "InitBlock" or "InitBlockUnaligned" when arguments.Length == 3:
                UnsafeInitBlock(Arg(0), Arg(1), ExtendTo64(Arg(2), signed: false));
                return Handled;
            case "SkipInit" when arguments.Length == 1:
                return Handled; // the out argument was evaluated (declared); nothing to initialize
            case "Unbox" when ta.Length == 1 && arguments.Length == 1:
                // ref T into the box: struct setters of source-generated metadata write through it.
                return CallRuntime("csrt_unbox", L.Ptr, [L.Ptr, L.Ptr], Arg(0), MethodTableFor(S(ta[0]), inv));
            case "BitCast" when ta.Length == 2 && arguments.Length == 1:
            {
                var (from, to) = (S(ta[0]), S(ta[1]));
                if (T.Target.SizeOf(T.Storage(from)) != T.Target.SizeOf(T.Storage(to)))
                {
                    // `if (sizeof(TFrom) != sizeof(TTo)) ThrowHelper.ThrowNotSupportedException();`
                    ThrowAndContinue("System.NotSupportedException", "Specified method is not supported.");
                    return LlvmContext.Null(T.Value(to));
                }
                return UnsafeBitCast(Arg(0), from, to);
            }
        }
        throw CompileError.Unsupported($"'{target}'", inv);
    }

    /// <summary>
    /// MemoryMarshal.GetReference (a span's first element, or where it would be: the span's pointer, so a default
    /// span gives a null ref as in .NET) and GetArrayDataReference (the element data of an array, after the header;
    /// a null array is a NullReferenceException). The IL front-end lowers the same members in FunctionCodeGen.IlCalls.cs.
    /// </summary>
    private LLVMValueRef? TryMemoryMarshalReference(IMethodSymbol target, IInvocationOperation inv)
    {
        if (target.ContainingType is not { Name: "MemoryMarshal", ContainingNamespace.Name: "InteropServices" } || target.Parameters.Length != 1) return null;
        switch (target.Name)
        {
            case "GetReference" when target.TypeArguments.Length == 1:
                return SpanPointer(EmitArgument(inv.Arguments[0], target));
            case "GetArrayDataReference" when target.Parameters[0].Type is IArrayTypeSymbol:
            {
                var array = EmitArgument(inv.Arguments[0], target);
                NullCheck(array);
                return _b.InBoundsGep(L.I8, array, [L.ConstInt(L.I64, 16)]); // { mt, i64 length, data }
            }
            case "GetArrayDataReference": // Array of any rank: { mt, i64 length, i64 dimension[rank], data }
            {
                var array = EmitArgument(inv.Arguments[0], target);
                NullCheck(array);
                return CallRuntime("csrt_array_data", L.Ptr, [L.Ptr], array);
            }
        }
        return null;
    }

    private LLVMValueRef ExtendTo64(LLVMValueRef value, bool signed) =>
        value.Type.IntWidth == 64 ? value : _b.Cast(signed ? LLVMOpcode.SExt : LLVMOpcode.ZExt, value, L.I64);

    /// <summary>The same address as a new SSA value, so it carries no data-access tag (see <see cref="MemoryFacts"/>).</summary>
    private LLVMValueRef UnsafeUntagged(LLVMValueRef address) => _b.Gep(L.I8, address, [L.ConstInt(L.I64, 0)]);

    /// <summary>`address ± count * sizeof(T)`: Unsafe.Add/Subtract. Not `inbounds`: a ref may be null or leave its object.</summary>
    private LLVMValueRef UnsafeElementAddress(LLVMValueRef address, LLVMValueRef count64, ITypeSymbol elementType, bool subtract)
    {
        if (subtract) count64 = _b.Neg(count64);
        var size = L.ConstInt(L.I64, (long)T.Target.SizeOf(T.Storage(S(elementType))));
        return _b.Gep(L.I8, address, [_b.BinOp(LLVMOpcode.Mul, count64, size)]);
    }

    private LLVMValueRef UnsafeByteAddress(LLVMValueRef address, LLVMValueRef offset64, bool subtract) =>
        _b.Gep(L.I8, address, [subtract ? _b.Neg(offset64) : offset64]);

    /// <summary>Unsafe.IsAddress*: an unsigned comparison of the addresses (i1).</summary>
    private LLVMValueRef UnsafeAddressCompare(string name, LLVMValueRef left, LLVMValueRef right)
    {
        var predicate = name switch
        {
            "IsAddressGreaterThan" => LLVMIntPredicate.UGT,
            "IsAddressLessThan" => LLVMIntPredicate.ULT,
            "IsAddressGreaterThanOrEqualTo" => LLVMIntPredicate.UGE,
            _ => LLVMIntPredicate.ULE,
        };
        return _b.ICmp(predicate, _b.Cast(LLVMOpcode.PtrToInt, left, L.I64), _b.Cast(LLVMOpcode.PtrToInt, right, L.I64));
    }

    /// <summary>Unsafe.ByteOffset(origin, target): `target - origin` as a native integer of <paramref name="type"/>.</summary>
    private LLVMValueRef UnsafeByteOffset(LLVMValueRef origin, LLVMValueRef target, LLVMTypeRef type) =>
        _b.BinOp(LLVMOpcode.Sub, _b.Cast(LLVMOpcode.PtrToInt, target, type), _b.Cast(LLVMOpcode.PtrToInt, origin, type));

    private void UnsafeCopyBlock(LLVMValueRef destination, LLVMValueRef source, LLVMValueRef bytes64) => MemMove(destination, source, bytes64, noReferences: true);

    private void UnsafeInitBlock(LLVMValueRef destination, LLVMValueRef value, LLVMValueRef bytes64)
    {
        var f = _m.Intrinsic("llvm.memset.p0.i64", L.Void, L.Ptr, L.I8, L.I64, L.I1);
        _b.Call(f.Type, f.Fn, [destination, value.Type.IntWidth == 8 ? value : _b.Cast(LLVMOpcode.Trunc, value, L.I8), bytes64, L.False]);
    }

    /// <summary>An untagged load of <paramref name="type"/> (Unsafe.Read: natural alignment; ReadUnaligned: alignment 1).</summary>
    private LLVMValueRef UnsafeLoad(LLVMValueRef address, ITypeSymbol type, bool unaligned)
    {
        var value = _b.Load(T.Storage(type), address);
        // Alignment 1 keeps LLVM from assuming (and vectorizing on) the natural alignment.
        if (unaligned) Native.LLVMSetAlignment(value, 1);
        return type.SpecialType == SpecialType.System_Boolean ? _b.Cast(LLVMOpcode.Trunc, value, L.I1) : value;
    }

    private void UnsafeStore(LLVMValueRef address, LLVMValueRef value, ITypeSymbol type, bool unaligned)
    {
        var store = _b.Store(ToStorage(value, type), address);
        if (unaligned) Native.LLVMSetAlignment(store, 1);
    }

    /// <summary>Unsafe.BitCast between same-size value types: the bits go through memory.</summary>
    private LLVMValueRef UnsafeBitCast(LLVMValueRef value, ITypeSymbol from, ITypeSymbol to)
    {
        // The slot has the destination's alignment; the source value's store is conservative.
        var slot = NewTemp(T.Storage(to), "bitcast");
        Native.LLVMSetAlignment(_b.Store(ToStorage(value, from), slot), 1);
        return LoadValue(slot, to);
    }
}
