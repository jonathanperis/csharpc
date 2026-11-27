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

    internal void MemMove(LLVMValueRef destination, LLVMValueRef source, LLVMValueRef bytes, bool noReferences = false)
    {
        // Up to 32 bytes of data without references, inline: the head and the tail of the range, each read before anything is written, so
        // overlapping ranges move correctly (what .NET's Buffer.Memmove does for short lengths). A constant size LLVM expands itself, and
        // copies of references stay one memmove, which the write-barrier passes look for.
        if (noReferences && Native.LLVMIsConstant(bytes) == 0)
        {
            MemMoveShort(destination, source, bytes);
            return;
        }
        MemMoveCall(destination, source, bytes, noReferences);
    }

    private void MemMoveCall(LLVMValueRef destination, LLVMValueRef source, LLVMValueRef bytes, bool noReferences)
    {
        var f = _m.Intrinsic("llvm.memmove.p0.p0.i64", L.Void, L.Ptr, L.Ptr, L.I64, L.I1);
        var copy = _b.Call(f.Type, f.Fn, [destination, source, bytes, L.False]);
        if (noReferences) _m.Facts.NoReferences(copy);
    }

    private void MemMoveShort(LLVMValueRef destination, LLVMValueRef source, LLVMValueRef bytes)
    {
        var done = NewBlock("memmove.done");
        var large = NewBlock("memmove.large");
        var count = bytes.Type.IntWidth == 64 ? bytes : _b.Cast(LLVMOpcode.ZExt, bytes, L.I64);
        // (width in bytes, LLVM type) from the widest down; each handles counts in [width, 2 * width]
        (int Width, LLVMTypeRef Type)[] steps = [(16, Native.LLVMVectorType(L.I8, 16)), (8, L.I64), (4, L.I32), (2, L.I16), (1, L.I8)];
        _b.CondBr(_b.ICmp(LLVMIntPredicate.UGT, count, L.ConstInt(L.I64, 32)), large, NewBlockAfter(out var small));
        _b.PositionAtEnd(small);
        foreach (var (width, type) in steps)
        {
            var fits = NewBlock($"memmove.{width}");
            var next = NewBlock($"memmove.below{width}");
            _b.CondBr(_b.ICmp(LLVMIntPredicate.UGE, count, L.ConstInt(L.I64, width)), fits, next);
            _b.PositionAtEnd(fits);
            var tailOffset = _b.BinOp(LLVMOpcode.Sub, count, L.ConstInt(L.I64, width));
            var head = _b.Load(type, source);
            var tail = _b.Load(type, _b.Gep(L.I8, source, [tailOffset]));
            Native.LLVMSetAlignment(head, 1);
            Native.LLVMSetAlignment(tail, 1);
            Native.LLVMSetAlignment(_b.Store(head, destination), 1);
            Native.LLVMSetAlignment(_b.Store(tail, _b.Gep(L.I8, destination, [tailOffset])), 1);
            _b.Br(done);
            _b.PositionAtEnd(next);
        }
        _b.Br(done); // zero bytes
        _b.PositionAtEnd(large);
        MemMoveCall(destination, source, count, noReferences: true);
        _b.Br(done);
        _b.PositionAtEnd(done);
    }

    private LLVMBasicBlockRef NewBlockAfter(out LLVMBasicBlockRef block) => block = NewBlock("memmove.small");

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
        if (!_m.IsUserMethod(method)) return _m.LibraryFunctionPointer(method, at);
        var target = _m.GetFunction(method);
        // An [UnmanagedCallersOnly] method is entered with the C ABI.
        if (ModuleCodeGen.IsUnmanagedCallersOnly(method))
        {
            var managed = ModuleCodeGen.IsNestedFunction(method) ? NestedFunctionThunk(method, target) : target;
            return _m.NativeEntryThunk(method, managed);
        }
        return ModuleCodeGen.IsNestedFunction(method) ? NestedFunctionThunk(method, target).Fn : target.Fn;
    }

    /// <summary>Static local functions use the closure convention (env first): a thunk with the plain signature.</summary>
    private LoweredFunction NestedFunctionThunk(IMethodSymbol method, LoweredFunction target)
    {
        var name = Mangler.Method(method) + ".fnptr";
        var signature = FunctionPointerType(method);
        var existing = _m.Module.GetFunction(name);
        if (!existing.IsNull) return new LoweredFunction(existing, signature);
        var thunk = _m.Module.AddFunction(name, signature);
        Native.LLVMSetLinkage(thunk, LLVMLinkage.Internal);
        using var tb = new IrBuilder(L);
        tb.PositionAtEnd(tb.AppendBlock(thunk, "entry"));
        var args = new LLVMValueRef[method.Parameters.Length + 1];
        args[0] = LlvmContext.Null(L.Ptr);
        for (var i = 0; i < method.Parameters.Length; i++) args[i + 1] = thunk.Param((uint)i);
        var call = tb.Call(target.Type, target.Fn, args);
        if (method.ReturnsVoid) tb.RetVoid(); else tb.Ret(call);
        return new LoweredFunction(thunk, signature);
    }

    private LLVMTypeRef FunctionPointerType(IMethodSymbol signature)
    {
        // Through S(): a function pointer in generic code (`delegate*<T, T, T>`) has the instantiation's types.
        var parameters = signature.Parameters.Select(p => p.RefKind == RefKind.None ? T.Value(S(p.Type)) : L.Ptr).ToArray();
        var ret = signature.RefKind != RefKind.None ? L.Ptr : T.Value(S(signature.ReturnType));
        return L.FunctionType(ret, parameters);
    }

    private LLVMValueRef EmitFunctionPointerInvocation(IFunctionPointerInvocationOperation op, bool address = false)
    {
        var signature = ((IFunctionPointerTypeSymbol)op.Target.Type!).Signature;
        var fn = EmitValue(op.Target);
        var args = new LLVMValueRef[signature.Parameters.Length];
        foreach (var argument in op.Arguments) args[argument.Parameter!.Ordinal] = EmitArgument(argument);
        var result = CallFunctionPointer(signature, fn, args);
        return signature.RefKind != RefKind.None && !address ? LoadValue(result, S(signature.ReturnType)) : result;
    }

    /// <summary>
    /// Calls a function pointer: a managed one is a plain call (csharpc's own convention); an unmanaged one follows the
    /// platform's C ABI (ModuleCodeGen.NativeCalls.cs). The result is the managed representation (the address for a ref return).
    /// </summary>
    internal LLVMValueRef CallFunctionPointer(IMethodSymbol signature, LLVMValueRef fn, LLVMValueRef[] args)
    {
        if (!ModuleCodeGen.IsUnmanagedConvention(signature.CallingConvention)) return _b.Call(FunctionPointerType(signature), fn, args);
        // Through S(): `delegate* unmanaged<T, T>` in generic code has the instantiation's types.
        var types = signature.Parameters.Select(p => S(p.Type)).ToArray();
        var returnType = S(signature.ReturnType);
        if (!types.Zip(signature.Parameters).All(x => SymbolEqualityComparer.Default.Equals(x.First, x.Second.Type))
            || !SymbolEqualityComparer.Default.Equals(returnType, signature.ReturnType))
            signature = ((IFunctionPointerTypeSymbol)_m.Compilation.CreateFunctionPointerTypeSymbol(returnType, signature.RefKind,
                [.. types], [.. signature.Parameters.Select(p => p.RefKind)], signature.CallingConvention)).Signature;
        if (_m.NativeCallStub(signature) is { } stub) return _b.Call(stub.Type, stub.Fn, [fn, .. args]);
        var call = _b.Call(FunctionPointerType(signature), fn, args);
        foreach (var (index, attribute) in _m.NativeExtensionAttributes(signature)) Native.LLVMAddCallSiteAttribute(call, index, attribute);
        return call;
    }

    /// <summary>
    /// `&amp;Math.Sqrt`: a library method has no function of its own (it may be an intrinsic, a corelib shadow or inlined
    /// away), so its pointer is a thunk with the managed signature that calls it like any other call site.
    /// </summary>
    internal void EmitLibraryMethodThunk(IMethodSymbol method, SyntaxNode syntax)
    {
        var arguments = System.Collections.Immutable.ImmutableArray.CreateBuilder<IArgumentOperation>(method.Parameters.Length);
        foreach (var p in method.Parameters)
        {
            var slot = ParamOf((uint)p.Ordinal);
            IOperation operand = p.RefKind == RefKind.None
                ? new IlValueOperation(syntax, p.Type, () => slot)
                : new IlValueOperation(syntax, p.Type, () => LoadValue(slot, S(p.Type)), slot);
            arguments.Add(new IlArgumentOperation(syntax, p, operand));
        }
        var call = new IlInvocationOperation(syntax, method, null, false, arguments.MoveToImmutable(), null);
        if (method.RefKind != RefKind.None) _b.Ret(EmitInvocation(call));
        else
        {
            var result = EmitValue(call);
            if (method.ReturnsVoid) _b.RetVoid();
            else if (result.IsNull) throw CompileError.Unsupported($"function pointer to '{method}' (it produces no value)");
            else _b.Ret(result);
        }
        Finish();
    }
}
