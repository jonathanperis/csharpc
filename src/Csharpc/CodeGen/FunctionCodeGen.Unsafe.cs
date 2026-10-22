using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Unsafe code: pointers, address-of, indirection, pointer arithmetic, `fixed`, `stackalloc`,
/// sizeof and function pointers (docs/03-lowering.md#unsafe-code).
///
/// Roslyn's IOperation API doesn't model pointer indirection, pointer element access, `fixed`
/// initializers or `stackalloc`: they surface as OperationKind.None nodes whose children are
/// still exposed. We recognize them by syntax shape + child types.
/// Pointers are plain LLVM `ptr`s; the GC is non-moving, so `fixed` is just "take the address".
/// </summary>
public sealed partial class FunctionCodeGen
{
    private enum NoneKind { Unknown, Deref, PointerElement, StackAlloc, FixedInitializer }

    private static NoneKind ClassifyNone(IOperation op)
    {
        if (op.Kind != OperationKind.None) return NoneKind.Unknown;
        var children = op.ChildOperations.ToList();
        if (op.Syntax is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax)
            return NoneKind.StackAlloc;
        if (op.Syntax is ElementAccessExpressionSyntax && children.Count == 2 && children[0].Type is IPointerTypeSymbol)
            return NoneKind.PointerElement;
        if (children.Count == 1 && children[0].Type is IPointerTypeSymbol && op.Type is not IPointerTypeSymbol)
            return NoneKind.Deref; // `*p`, and the `p` in `p->f` (typed as the pointee)
        if (op.Type is IPointerTypeSymbol && children.Count == 1 && op.Syntax.Ancestors().Any(a => a is FixedStatementSyntax))
            return NoneKind.FixedInitializer;
        return NoneKind.Unknown;
    }

    private LLVMValueRef EmitNone(IOperation op) => ClassifyNone(op) switch
    {
        NoneKind.Deref or NoneKind.PointerElement => LoadValue(AddressOfNone(op), op.Type!),
        NoneKind.StackAlloc => EmitStackAlloc(op),
        NoneKind.FixedInitializer => EmitFixedInitializer(op.ChildOperations.First()),
        _ => throw CompileError.Unsupported($"construct '{op.Syntax.ToString().ReplaceLineEndings(" ")}' (operation None)", op),
    };

    internal bool TryAddressOfNone(IOperation op, out LLVMValueRef address)
    {
        address = default;
        if (ClassifyNone(op) is not (NoneKind.Deref or NoneKind.PointerElement)) return false;
        address = AddressOfNone(op);
        return true;
    }

    private LLVMValueRef AddressOfNone(IOperation op)
    {
        var children = op.ChildOperations.ToList();
        switch (ClassifyNone(op))
        {
            case NoneKind.Deref:
                return EmitValue(children[0]);
            case NoneKind.PointerElement:
            {
                var pointer = EmitValue(children[0]);
                var pointee = ((IPointerTypeSymbol)children[0].Type!).PointedAtType;
                var index = ToIndex(EmitValue(children[1]), children[1].Type!);
                return _b.InBoundsGep(T.Storage(pointee), pointer, [index]);
            }
            default:
                throw CompileError.Unsupported("taking the address of this expression", op);
        }
    }

    /// <summary>`fixed (T* p = expr)`: arrays/strings pin to their first element (null if null/empty, as in C#).</summary>
    private LLVMValueRef EmitFixedInitializer(IOperation source)
    {
        var type = source.Type;
        if (type is IPointerTypeSymbol) return EmitValue(source); // fixed (int* p = &s.field)
        if (type is IArrayTypeSymbol { Rank: 1 } arrayType)
        {
            var array = EmitValue(source);
            var result = NewTemp(L.Ptr, "fixed");
            _b.Store(LlvmContext.Null(L.Ptr), result);
            var check = NewBlock("fixed.len");
            var take = NewBlock("fixed.take");
            var done = NewBlock("fixed.done");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, array, LlvmContext.Null(L.Ptr)), done, check);
            _b.PositionAtEnd(check);
            var header = T.ArrayHeader(T.Storage(arrayType.ElementType));
            var length = _b.Load(L.I64, _b.StructGep(header, array, 1));
            _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, length, L.ConstInt(L.I64, 0)), done, take);
            _b.PositionAtEnd(take);
            _b.Store(_b.StructGep(header, array, 2), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Load(L.Ptr, result);
        }
        if (type?.SpecialType == SpecialType.System_String)
        {
            // GetPinnableReference: the chars (a null string pins to null).
            var str = EmitValue(source);
            var chars = _b.StructGep(T.StringHeader, str, 2);
            return _b.Select(_b.ICmp(LLVMIntPredicate.EQ, str, LlvmContext.Null(L.Ptr)), LlvmContext.Null(L.Ptr), chars);
        }
        if (TypeMapper.IsSpan(type)) return _b.ExtractValue(EmitValue(source), 0);
        throw CompileError.Unsupported($"fixed on '{type}'", source);
    }

    /// <summary>`stackalloc T[n]` (optionally with an initializer): a zeroed dynamic alloca; Span&lt;T&gt; targets get {ptr, n}.</summary>
    private LLVMValueRef EmitStackAlloc(IOperation op)
    {
        var elementType = op.Type switch
        {
            IPointerTypeSymbol p => p.PointedAtType,
            INamedTypeSymbol n when TypeMapper.IsSpan(n) => n.TypeArguments[0],
            _ => throw CompileError.Unsupported($"stackalloc into '{op.Type}'", op),
        };
        var storage = T.Storage(elementType);
        // Children: [count, initializer elements...] — Roslyn synthesizes the count for `stackalloc T[] { ... }`.
        var children = op.ChildOperations.ToList();
        var sizeOp = children[0];
        var elements = children.Skip(1).ToList();
        var count64 = ToIndex(EmitValue(sizeOp), sizeOp.Type!);
        FailIf(_b.ICmp(LLVMIntPredicate.SLT, count64, L.ConstInt(L.I64, 0)), "csrt_throw_overflow");
        var memory = Native.LLVMBuildArrayAlloca(_b.Ref, storage, count64, "stackalloc");
        var bytes = _b.BinOp(LLVMOpcode.Mul, count64, L.ConstInt(L.I64, (long)T.Target.SizeOf(storage)));
        MemSet(memory, bytes); // .NET zero-initializes stackalloc (no SkipLocalsInit)
        for (var i = 0; i < elements.Count; i++)
        {
            var address = _b.InBoundsGep(storage, memory, [L.ConstInt(L.I64, i)]);
            StoreValue(ToStorage(Coerce(EmitValue(elements[i]), elements[i].Type, elementType), elementType), address);
        }
        if (op.Type is IPointerTypeSymbol) return memory;
        return MakeSpan(memory, _b.Cast(LLVMOpcode.Trunc, count64, L.I32));
    }

    internal void MemSet(LLVMValueRef destination, LLVMValueRef bytes, byte value = 0)
    {
        var f = _m.Intrinsic("llvm.memset.p0.i64", L.Void, L.Ptr, L.I8, L.I64, L.I1);
        _b.Call(f.Type, f.Fn, [destination, L.ConstInt(L.I8, value), bytes, L.False]);
    }

    internal void MemMove(LLVMValueRef destination, LLVMValueRef source, LLVMValueRef bytes)
    {
        var f = _m.Intrinsic("llvm.memmove.p0.p0.i64", L.Void, L.Ptr, L.Ptr, L.I64, L.I1);
        _b.Call(f.Type, f.Fn, [destination, source, bytes, L.False]);
    }

    private LLVMValueRef EmitAddressOf(IAddressOfOperation op)
    {
        if (op.Reference is IMethodReferenceOperation method) return FunctionPointerTo(method.Method, op);
        return AddressOf(op.Reference);
    }

    private LLVMValueRef EmitSizeOf(ISizeOfOperation op) =>
        L.ConstInt(L.I32, (long)T.Target.SizeOf(T.Storage(S(op.TypeOperand))));

    // ======================================================================================
    // Pointer arithmetic: p ± n scales by sizeof(*p); p - q is an element count (long)
    // ======================================================================================

    internal static bool IsPointer(ITypeSymbol? type) => type is IPointerTypeSymbol;

    private LLVMTypeRef PointeeStorage(ITypeSymbol pointerType) =>
        ((IPointerTypeSymbol)pointerType).PointedAtType is { SpecialType: SpecialType.System_Void } ? L.I8
            : T.Storage(((IPointerTypeSymbol)pointerType).PointedAtType);

    internal LLVMValueRef PointerArithmetic(BinaryOperatorKind kind, LLVMValueRef l, LLVMValueRef r, ITypeSymbol lt, ITypeSymbol rt, IOperation at)
    {
        switch (kind)
        {
            case BinaryOperatorKind.Add when IsPointer(lt):
                return _b.InBoundsGep(PointeeStorage(lt), l, [ToIndex(r, rt)]);
            case BinaryOperatorKind.Add when IsPointer(rt):
                return _b.InBoundsGep(PointeeStorage(rt), r, [ToIndex(l, lt)]);
            case BinaryOperatorKind.Subtract when IsPointer(lt) && IsPointer(rt):
            {
                var bytes = _b.BinOp(LLVMOpcode.Sub, _b.Cast(LLVMOpcode.PtrToInt, l, L.I64), _b.Cast(LLVMOpcode.PtrToInt, r, L.I64));
                var size = (long)T.Target.SizeOf(PointeeStorage(lt));
                return size == 1 ? bytes : _b.BinOp(LLVMOpcode.SDiv, bytes, L.ConstInt(L.I64, size));
            }
            case BinaryOperatorKind.Subtract when IsPointer(lt):
                return _b.InBoundsGep(PointeeStorage(lt), l, [_b.Neg(ToIndex(r, rt))]);
            case BinaryOperatorKind.Equals: return _b.ICmp(LLVMIntPredicate.EQ, l, r);
            case BinaryOperatorKind.NotEquals: return _b.ICmp(LLVMIntPredicate.NE, l, r);
            case BinaryOperatorKind.LessThan: return _b.ICmp(LLVMIntPredicate.ULT, l, r);
            case BinaryOperatorKind.LessThanOrEqual: return _b.ICmp(LLVMIntPredicate.ULE, l, r);
            case BinaryOperatorKind.GreaterThan: return _b.ICmp(LLVMIntPredicate.UGT, l, r);
            case BinaryOperatorKind.GreaterThanOrEqual: return _b.ICmp(LLVMIntPredicate.UGE, l, r);
            default: throw CompileError.Unsupported($"pointer operator {kind}", at);
        }
    }

    private LLVMValueRef PointerStep(LLVMValueRef pointer, ITypeSymbol pointerType, long elements) =>
        _b.InBoundsGep(PointeeStorage(pointerType), pointer, [L.ConstInt(L.I64, elements)]);

    // ======================================================================================
    // Function pointers: delegate*<...> = a plain code pointer to a static method
    // ======================================================================================

    private LLVMValueRef FunctionPointerTo(IMethodSymbol method, IOperation at)
    {
        if (!_m.IsUserMethod(method)) throw CompileError.Unsupported($"function pointer to library method '{method}'", at);
        var target = _m.GetFunction(method);
        if (!ModuleCodeGen.IsNestedFunction(method)) return target.Fn;

        // Static local functions use the closure convention (env first): wrap them in a thunk.
        var name = Mangler.Method(method) + ".fnptr";
        var existing = _m.Module.GetFunction(name);
        if (!existing.IsNull) return existing;
        var signature = FunctionPointerType(method);
        var thunk = _m.Module.AddFunction(name, signature);
        Native.LLVMSetLinkage(thunk, LLVMLinkage.Internal);
        using var tb = new IrBuilder(L);
        tb.PositionAtEnd(tb.AppendBlock(thunk, "entry"));
        var args = new LLVMValueRef[method.Parameters.Length + 1];
        args[0] = LlvmContext.Null(L.Ptr);
        for (var i = 0; i < method.Parameters.Length; i++) args[i + 1] = thunk.Param((uint)i);
        var call = tb.Call(target.Type, target.Fn, args);
        if (method.ReturnsVoid) tb.RetVoid(); else tb.Ret(call);
        return thunk;
    }

    private LLVMTypeRef FunctionPointerType(IMethodSymbol signature)
    {
        var parameters = signature.Parameters.Select(p => p.RefKind == RefKind.None ? T.Value(p.Type) : L.Ptr).ToArray();
        var ret = signature.RefKind != RefKind.None ? L.Ptr : T.Value(signature.ReturnType);
        return L.FunctionType(ret, parameters);
    }

    private LLVMValueRef EmitFunctionPointerInvocation(IFunctionPointerInvocationOperation op)
    {
        var signature = ((IFunctionPointerTypeSymbol)op.Target.Type!).Signature;
        var fn = EmitValue(op.Target);
        var args = new LLVMValueRef[signature.Parameters.Length];
        foreach (var argument in op.Arguments) args[argument.Parameter!.Ordinal] = EmitArgument(argument);
        var result = _b.Call(FunctionPointerType(signature), fn, args);
        return signature.RefKind != RefKind.None ? LoadValue(result, signature.ReturnType) : result;
    }
}
