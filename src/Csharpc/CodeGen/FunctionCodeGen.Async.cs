using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// async methods and iterators as LLVM coroutines (switch-resumed lowering — clang's C++20
/// coroutine machinery). The method itself is the coroutine "ramp":
///
///   start:   promise = csrt_task_new / csrt_iterator_new      (the object the caller gets back)
///            id = llvm.coro.id; mem = csrt_coro_alloc(llvm.coro.size); hdl = llvm.coro.begin
///            [iterator: initial suspend — the body runs on the first MoveNext()]
///   await t: if (!completed(t)) { csrt_task_on_completed(t, hdl); suspend }  result = t.payload
///   yield v: it.current = v; it.state = 1; suspend (destroy path runs pending finally blocks)
///   return:  task.payload = v; csrt_task_complete(task) / it.state = 2; -> cleanup -> coro.end
///
/// CoroSplit turns this into ramp/resume/destroy functions and moves everything live across a
/// suspend point into the frame, which is a GC object (so references there stay alive).
/// Object layouts (payload at +48, iterator state at +8) are shared with src/runtime/async.c.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private const int PromisePayloadOffset = 48;
    private const int IteratorStateOffset = 8;

    private sealed class CoroutineState
    {
        public required CoroutineKind Kind { get; init; }
        public required ITypeSymbol? ValueType { get; init; }
        public LLVMValueRef Id { get; set; }
        public LLVMValueRef Handle { get; set; }
        public LLVMValueRef Promise { get; set; }
        public LLVMBasicBlockRef Cleanup { get; set; }
        public LLVMBasicBlockRef SuspendReturn { get; set; }
        public LLVMBasicBlockRef Complete { get; set; }
        /// <summary>Entered with the escaping exception in <see cref="ExceptionSlot"/>: fault the task / iterator.</summary>
        public LLVMBasicBlockRef Fault { get; set; }
        public LLVMBasicBlockRef Pad { get; set; }
        public LLVMValueRef ExceptionSlot { get; set; }
    }

    private CoroutineState? _coro;

    private LLVMValueRef TokenNone => LlvmContext.Null(L.Token);

    private LoweredFunction CoroIntrinsic(string name, LLVMTypeRef ret, params LLVMTypeRef[] parameters) =>
        _m.Intrinsic(name, ret, parameters);

    private void BeginCoroutine(CoroutineKind kind)
    {
        var method = _method!;
        var isIterator = kind is CoroutineKind.Iterator or CoroutineKind.AsyncIterator;
        var valueType = isIterator
            ? S(Coroutines.IteratorElementType(method, _m.Compilation))
            : Coroutines.AsyncResultType(S(method.ReturnType));
        var coro = _coro = new CoroutineState { Kind = kind, ValueType = valueType };
        var payloadSize = valueType is null ? 0 : (long)T.Target.SizeOf(T.Storage(valueType));

        coro.Promise = kind == CoroutineKind.Async
            ? CallRuntime("csrt_task_new", L.Ptr, [L.I32], L.ConstInt(L.I32, payloadSize))
            : NewIterator(method, payloadSize, _m.IteratorMethodTable(valueType!, kind == CoroutineKind.AsyncIterator));
        coro.Promise.SetName(kind == CoroutineKind.Async ? "task" : "iterator");

        var id = CoroIntrinsic("llvm.coro.id", L.Token, L.I32, L.Ptr, L.Ptr, L.Ptr);
        // -g: LLVM 18's coroutine frame debug info (buildFrameDebugInfo) dereferences the promise
        // alloca, so give the coroutine a dummy one, as clang always has a promise.
        var promise = _dbg is not null ? _allocas.Alloca(L.I8, "coro.promise") : LlvmContext.Null(L.Ptr);
        coro.Id = _b.Call(id.Type, id.Fn, [L.ConstInt(L.I32, 16), promise, LlvmContext.Null(L.Ptr), LlvmContext.Null(L.Ptr)], "coro.id");
        var sizeFn = CoroIntrinsic("llvm.coro.size.i64", L.I64);
        var size = _b.Call(sizeFn.Type, sizeFn.Fn, [], "coro.size");
        var mem = CallRuntime("csrt_coro_alloc", L.Ptr, [L.I64], size);
        var begin = CoroIntrinsic("llvm.coro.begin", L.Ptr, L.Token, L.Ptr);
        coro.Handle = _b.Call(begin.Type, begin.Fn, [coro.Id, mem], "coro.handle");

        var current = _b.InsertBlock;
        coro.SuspendReturn = NewBlock("coro.suspend.ret");
        coro.Cleanup = NewBlock("coro.cleanup");
        coro.Complete = NewBlock(isIterator ? "coro.finished" : "coro.complete");

        _b.PositionAtEnd(coro.SuspendReturn);
        var end = CoroIntrinsic("llvm.coro.end", L.I1, L.Ptr, L.I1, L.Token);
        _b.Call(end.Type, end.Fn, [coro.Handle, L.False, TokenNone]);
        if (_fn.Type.ReturnType().IsVoid) _b.RetVoid(); // async void
        else _b.Ret(coro.Promise);

        _b.PositionAtEnd(coro.Cleanup);
        var free = CoroIntrinsic("llvm.coro.free", L.Ptr, L.Token, L.Ptr);
        _b.Call(free.Type, free.Fn, [coro.Id, coro.Handle]); // frames are GC-managed: nothing to free
        _b.Br(coro.SuspendReturn);

        _b.PositionAtEnd(coro.Complete);
        if (kind == CoroutineKind.Async)
            CallRuntime("csrt_task_complete", L.Void, [L.Ptr], coro.Promise);
        else
            _b.Store(L.ConstInt(L.I32, 2), _b.InBoundsGep(L.I8, coro.Promise, [L.ConstInt(L.I64, IteratorStateOffset)]));
        if (kind == CoroutineKind.AsyncIterator)
            CallRuntime("csrt_async_iterator_finished", L.Void, [L.Ptr], coro.Promise); // MoveNextAsync() -> false
        _b.Br(coro.Cleanup);

        // Exceptions escaping the body (C# semantics): captured into the task (even before the first
        // await), parked for the iterator's MoveNext() caller, or — async void — a process crash.
        coro.ExceptionSlot = _allocas.Alloca(L.Ptr, "coro.exn");
        coro.Fault = NewBlock("coro.fault");
        _b.PositionAtEnd(coro.Fault);
        var escaped = _b.Load(L.Ptr, coro.ExceptionSlot, "exn");
        if (kind == CoroutineKind.Async && _fn.Type.ReturnType().IsVoid)
        {
            CallRuntime("csrt_unhandled_exception", L.Void, [L.Ptr], escaped);
            _b.Unreachable();
        }
        else
        {
            var faultFn = kind switch
            {
                CoroutineKind.Async => "csrt_task_fault_async", // OperationCanceledException cancels
                CoroutineKind.Iterator => "csrt_iterator_fault",
                _ => "csrt_async_iterator_fault",
            };
            CallRuntime(faultFn, L.Void, [L.Ptr, L.Ptr], coro.Promise, escaped);
            _b.Br(coro.Cleanup);
        }

        _b.PositionAtEnd(current);
        if (isIterator)
        {
            CallRuntime("csrt_iterator_set_frame", L.Void, [L.Ptr, L.Ptr], coro.Promise, coro.Handle);
            Suspend(coro.Cleanup); // initial suspend: nothing runs until MoveNext()
        }
    }

    /// <summary>Catch-all landing pad for code of a coroutine body that no try region protects.</summary>
    private LLVMBasicBlockRef CoroutinePad()
    {
        var coro = _coro!;
        if (!coro.Pad.IsNull) return coro.Pad;
        EnsurePersonality();
        var (savedBlock, savedUnwind) = (_b.InsertBlock, _b.UnwindDest);
        _b.UnwindDest = default;
        coro.Pad = NewBlock("coro.lpad");
        _b.PositionAtEnd(coro.Pad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, [LlvmContext.Null(L.Ptr)], cleanup: false);
        _b.Store(CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0)), coro.ExceptionSlot);
        _b.Br(coro.Fault);
        _b.PositionAtEnd(savedBlock);
        _b.UnwindDest = savedUnwind;
        return coro.Pad;
    }

    /// <summary>
    /// csrt_iterator_new(payload, clone, args). The arguments are saved so that calling
    /// GetEnumerator() on an enumerable that's already been enumerated can start over.
    /// </summary>
    private LLVMValueRef NewIterator(IMethodSymbol method, long payloadSize, LLVMValueRef methodTable)
    {
        List<(LLVMTypeRef Type, LLVMValueRef Value)> saved = [];
        if (_incomingEnv.Handle != 0) saved.Add((L.Ptr, _incomingEnv));
        else if (_this.Handle != 0) saved.Add((L.Ptr, _this));
        // A C# 14 extension member's receiver comes right after: the clone re-invokes the method with
        // the same signature (ModuleCodeGen.FunctionTypeOf).
        else if (ModuleCodeGen.HasExtensionReceiver(method) && method.ContainingType.ExtensionParameter is { } receiver)
            saved.Add(receiver.RefKind == RefKind.None
                ? (T.Value(S(receiver.Type)), LoadValue(_slots[receiver], S(receiver.Type)))
                : (L.Ptr, _slots[receiver]));
        foreach (var p in method.Parameters)
            saved.Add((T.Value(S(p.Type)), LoadValue(ParameterSpill(p), S(p.Type))));

        var argsType = L.Struct([.. saved.Select(s => s.Type)]);
        LLVMValueRef args = LlvmContext.Null(L.Ptr);
        if (saved.Count > 0)
        {
            args = CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], _m.ExternalGlobal("csrt_Object_MT"),
                L.ConstInt(L.I64, (long)T.Target.SizeOf(argsType)));
            for (var i = 0; i < saved.Count; i++)
                _b.Store(saved[i].Value, _b.StructGep(argsType, args, (uint)i));
        }
        var clone = _m.IteratorCloneThunk(method, _fn, argsType, saved.Count);
        return CallRuntime("csrt_iterator_new", L.Ptr, [L.Ptr, L.I32, L.Ptr, L.Ptr], methodTable, L.ConstInt(L.I32, payloadSize), clone, args);
    }

    /// <summary>A parameter's spill slot (generic instantiations key slots by the definition's symbols).</summary>
    private LLVMValueRef ParameterSpill(IParameterSymbol p) =>
        _slots.TryGetValue(p, out var slot) ? slot : _slots.First(kv => kv.Key is IParameterSymbol other && other.Ordinal == p.Ordinal
            && SymbolEqualityComparer.Default.Equals(other.ContainingSymbol.OriginalDefinition, p.ContainingSymbol.OriginalDefinition)).Value;

    /// <summary>suspend; resume continues in a fresh block, destroy goes to <paramref name="onDestroy"/>.</summary>
    private void Suspend(LLVMBasicBlockRef onDestroy, LLVMValueRef? saveToken = null)
    {
        var suspend = CoroIntrinsic("llvm.coro.suspend", L.I8, L.Token, L.I1);
        var state = _b.Call(suspend.Type, suspend.Fn, [saveToken ?? TokenNone, L.False], "suspend");
        var resume = NewBlock("resume");
        _b.Switch(state, _coro!.SuspendReturn, [(L.ConstInt(L.I8, 0), resume), (L.ConstInt(L.I8, 1), onDestroy)]);
        _b.PositionAtEnd(resume);
    }

    /// <summary>
    /// llvm.coro.save: from here on the coroutine counts as suspended and may be resumed by another
    /// thread (a pool worker completing the awaited task) even before it reaches llvm.coro.suspend.
    /// Must precede handing the frame to anyone who might resume it.
    /// </summary>
    private LLVMValueRef CoroSave()
    {
        var save = CoroIntrinsic("llvm.coro.save", L.Token, L.Ptr);
        return _b.Call(save.Type, save.Fn, [_coro!.Handle], "coro.save");
    }

    private LLVMValueRef PromisePayload => _b.InBoundsGep(L.I8, _coro!.Promise, [L.ConstInt(L.I64, PromisePayloadOffset)], "payload");

    /// <summary>`return v;` in an async method: store the result, run finally blocks, complete the task.</summary>
    private void CoroutineReturn(IOperation? value, ControlFlowBranch branch)
    {
        var coro = _coro!;
        if (value is not null)
        {
            var v = EmitValue(value);
            if (coro.Kind == CoroutineKind.Async && coro.ValueType is { } resultType)
                StoreValue(ToStorage(Coerce(v, TypeOf(value), resultType), resultType), PromisePayload);
        }
        _b.Br(ThroughFinally(branch.FinallyRegions, coro.Complete));
    }

    /// <summary>`yield return v;`</summary>
    private void EmitYieldReturn(IReturnOperation yieldReturn)
    {
        var coro = _coro ?? throw CompileError.Unsupported("yield return outside an iterator", yieldReturn);
        var elementType = coro.ValueType!;
        var value = yieldReturn.ReturnedValue!;
        var valueType = TypeOf(value);
        var v = Coerce(EmitValue(value), valueType, elementType);
        if (valueType is { IsValueType: true } && elementType.IsReferenceType) v = Box(v, valueType, value);
        StoreValue(ToStorage(v, elementType), PromisePayload);
        _b.Store(L.ConstInt(L.I32, 1), _b.InBoundsGep(L.I8, coro.Promise, [L.ConstInt(L.I64, IteratorStateOffset)]));
        LLVMValueRef? save = null;
        if (coro.Kind == CoroutineKind.AsyncIterator)
        {
            save = CoroSave(); // the consumer may call MoveNextAsync() (resume) on another thread right away
            CallRuntime("csrt_async_iterator_yielded", L.Void, [L.Ptr], coro.Promise); // MoveNextAsync() -> true
        }
        // Dispose() while suspended here destroys the coroutine: run the enclosing finally blocks first.
        Suspend(ThroughFinally(EnclosingFinallyRegions(_currentBlock), coro.Cleanup), save);
    }

    /// <summary>Finally regions around a block, innermost first.</summary>
    private static ImmutableArray<ControlFlowRegion> EnclosingFinallyRegions(BasicBlock? block)
    {
        var result = ImmutableArray.CreateBuilder<ControlFlowRegion>();
        for (var r = block?.EnclosingRegion; r is not null; r = r.EnclosingRegion)
            if (r is { Kind: ControlFlowRegionKind.Try, EnclosingRegion: { Kind: ControlFlowRegionKind.TryAndFinally } tf })
                result.Add(tf.NestedRegions[^1]);
        return result.ToImmutable();
    }

    /// <summary>`await e` on Task / Task&lt;T&gt; / ValueTask(&lt;T&gt;) / configured awaitables / Task.Yield().</summary>
    private LLVMValueRef EmitAwait(IAwaitOperation await)
    {
        var coro = _coro is { Kind: CoroutineKind.Async or CoroutineKind.AsyncIterator } c ? c : throw CompileError.Unsupported("await outside an async method", await);
        var operand = await.Operation;
        var operandType = TypeOf(operand);
        if (!Coroutines.IsTaskHandleType(operandType))
            throw CompileError.Unsupported($"awaiting '{operandType}' (custom awaitables)", await);

        var task = EmitValue(operand);
        if (Coroutines.IsYieldAwaitable(operandType))
        {
            var yieldSave = CoroSave();
            CallRuntime("csrt_async_yield", L.Void, [L.Ptr], coro.Handle);
            Suspend(coro.Cleanup, yieldSave);
            return default;
        }

        var wait = NewBlock("await.wait");
        var ready = NewBlock("await.ready");
        _b.CondBr(CallRuntime("csrt_task_is_completed", L.I1, [L.Ptr], task), ready, wait);
        _b.PositionAtEnd(wait);
        var awaitSave = CoroSave();
        CallRuntime("csrt_task_on_completed", L.Void, [L.Ptr, L.Ptr], task, coro.Handle);
        Suspend(coro.Cleanup, awaitSave);
        _b.Br(ready);
        _b.PositionAtEnd(ready);
        CallRuntime("csrt_task_throw_if_faulted", L.Void, [L.Ptr], task); // rethrows the original exception
        return TaskResult(task, TypeOf(await));
    }

    /// <summary>Reads a completed task's result (nothing for void / non-generic tasks).</summary>
    internal LLVMValueRef TaskResult(LLVMValueRef task, ITypeSymbol? resultType)
    {
        if (resultType is null || resultType.SpecialType == SpecialType.System_Void) return default;
        var payload = CallRuntime("csrt_task_result_ptr", L.Ptr, [L.Ptr], task);
        return LoadValue(payload, resultType);
    }

    /// <summary>A new, already-completed task holding <paramref name="value"/> (Task.FromResult).</summary>
    internal LLVMValueRef CompletedTask(LLVMValueRef value, ITypeSymbol? resultType)
    {
        if (resultType is null) return CallRuntime("csrt_task_completed_task", L.Ptr, []);
        var task = CallRuntime("csrt_task_new", L.Ptr, [L.I32], L.ConstInt(L.I32, (long)T.Target.SizeOf(T.Storage(resultType))));
        StoreValue(ToStorage(value, resultType), _b.InBoundsGep(L.I8, task, [L.ConstInt(L.I64, PromisePayloadOffset)]));
        CallRuntime("csrt_task_complete", L.Void, [L.Ptr], task);
        return task;
    }

    /// <summary>
    /// TaskCompletionSource&lt;T&gt;.SetResult / TrySetResult (the TCS *is* its task). Set*: throws if the
    /// task already completed; TrySet*: returns false instead and leaves the result untouched.
    /// </summary>
    internal LLVMValueRef SetTaskResult(LLVMValueRef task, LLVMValueRef? value, ITypeSymbol? type, bool tryMode)
    {
        NullCheck(task);
        if (value is null || type is null)
            return tryMode ? CallRuntime("csrt_task_try_complete", L.I1, [L.Ptr], task)
                : CallRuntime("csrt_task_complete", L.Void, [L.Ptr], task);
        var pending = NewBlock("tcs.pending");
        var done = NewBlock("tcs.done");
        var already = CallRuntime("csrt_task_is_completed", L.I1, [L.Ptr], task);
        if (tryMode)
        {
            var result = NewTemp(L.I8, "tcs.ok");
            _b.Store(L.ConstInt(L.I8, 0), result);
            _b.CondBr(already, done, pending);
            _b.PositionAtEnd(pending);
            StoreValue(ToStorage(value.Value, type), _b.InBoundsGep(L.I8, task, [L.ConstInt(L.I64, PromisePayloadOffset)]));
            CallRuntime("csrt_task_complete", L.Void, [L.Ptr], task);
            _b.Store(L.ConstInt(L.I8, 1), result);
            _b.Br(done);
            _b.PositionAtEnd(done);
            return _b.Cast(LLVMOpcode.Trunc, _b.Load(L.I8, result), L.I1);
        }
        _b.CondBr(already, done, pending); // completed: csrt_task_complete below throws InvalidOperationException
        _b.PositionAtEnd(pending);
        StoreValue(ToStorage(value.Value, type), _b.InBoundsGep(L.I8, task, [L.ConstInt(L.I64, PromisePayloadOffset)]));
        _b.Br(done);
        _b.PositionAtEnd(done);
        CallRuntime("csrt_task_complete", L.Void, [L.Ptr], task);
        return default;
    }

    /// <summary>
    /// Body of the Task.Run(Func&lt;T&gt;) thunk for one T: pack = { mt, delegate, task, thunk };
    /// invoke the delegate and store the result into the task payload (runtime/async.c completes it).
    /// </summary>
    internal void EmitTaskRunThunk(ITypeSymbol resultType)
    {
        var pack = _fn.Fn.Param(0);
        var del = _b.Load(L.Ptr, _b.InBoundsGep(L.I8, pack, [L.ConstInt(L.I64, 8)]), "del");
        var task = _b.Load(L.Ptr, _b.InBoundsGep(L.I8, pack, [L.ConstInt(L.I64, 16)]), "task");
        var value = InvokeDelegate0(del, T.Value(resultType));
        StoreValue(ToStorage(value, resultType), _b.InBoundsGep(L.I8, task, [L.ConstInt(L.I64, PromisePayloadOffset)]));
        _b.RetVoid();
        Finish();
    }

    /// <summary>Calls a delegate object with no arguments: fn(target).</summary>
    internal LLVMValueRef InvokeDelegate0(LLVMValueRef d, LLVMTypeRef returnType)
    {
        NullCheck(d);
        var target = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 1), "target");
        var fn = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 2), "fn");
        return _b.Call(L.FunctionType(returnType, L.Ptr), fn, [target]);
    }

    /// <summary>A Task[] / Task&lt;T&gt;[] from an array argument or a params-span collection expression.</summary>
    internal LLVMValueRef TaskArrayArgument(IArgumentOperation argument)
    {
        var value = argument.Value;
        while (value is IConversionOperation { Conversion.IsImplicit: true } c && c.Operand.Type is IArrayTypeSymbol) value = c.Operand;
        if (value is ICollectionExpressionOperation collection)
        {
            if (collection.Elements.Any(e => e is ISpreadOperation)) throw CompileError.Unsupported("spread elements", collection);
            var objectType = _m.Compilation.GetSpecialType(SpecialType.System_Object);
            var array = NewArray(objectType, L.ConstInt(L.I64, collection.Elements.Length));
            StoreElements(array, objectType, collection.Elements);
            return array;
        }
        if (value.Type is IArrayTypeSymbol) return EmitValue(value);
        throw CompileError.Unsupported($"task collection of type '{value.Type}' (use an array)", argument);
    }
}

internal static class LlvmTypeRefExtensions
{
    public static LLVMTypeRef ReturnType(this LLVMTypeRef functionType) => Native.LLVMGetReturnType(functionType);
}
