# async/await and iterators

csharpc compiles `async` methods, iterators (`yield return`) and async iterators
(`IAsyncEnumerable<T>`) to **LLVM coroutines**. It uses the switch-resumed lowering, the same
machinery clang uses for C++20 coroutines. Roslyn's IOperation/CFG doesn't lower any of these:
`await` stays an `IAwaitOperation` inside expressions and `yield return` stays an
`IReturnOperation` (kind `YieldReturn`). The state machine is built by LLVM's `CoroSplit` pass,
not by csharpc.

Code: `src/Csharpc/CodeGen/Coroutines.cs` (classification), `FunctionCodeGen.Async.cs`
(lowering), `ModuleCodeGen.Async.cs` (iterator method tables), `runtime/async.c` (tasks,
scheduler, iterator objects).

## Lowering

The method itself is the coroutine "ramp" (it carries the `presplitcoroutine` attribute):

```
start:   promise = csrt_task_new(sizeof T)            ; async: the Task the caller receives
         promise = csrt_iterator_new(MT, sizeof T, …)  ; iterator: the IEnumerable<T> object
         id  = llvm.coro.id(16, null, null, null)
         hdl = llvm.coro.begin(id, csrt_coro_alloc(llvm.coro.size.i64()))
         [iterators: initial suspend; the body runs on the first MoveNext()]

await t: if (!csrt_task_is_completed(t)) { csrt_task_on_completed(t, hdl); suspend }
         result = load T from t.payload                ; completed tasks continue synchronously
yield v: it.current = v; it.state = 1; suspend         ; destroy path runs pending finally blocks
return:  task.payload = v -> finally blocks -> csrt_task_complete(task) -> coro.free -> coro.end
end:     it.state = 2 (async iterator: complete the pending MoveNextAsync with false)

suspend: switch (llvm.coro.suspend(none, false)) { 0: resume, 1: cleanup, default: ret promise }
```

- **Frames** come from `csrt_coro_alloc` and live on the GC heap. Everything CoroSplit spills
  into the frame (locals, temporaries, the awaited task) is scanned by the conservative collector.
  `llvm.coro.free` frees nothing.
- **Resuming from C.** The split resume and destroy functions use `fastcc`, so the runtime never
  calls `frame[0]` directly. Modules containing coroutines emit C-ABI `csrt_coro_resume` and
  `csrt_coro_destroy` wrappers around `llvm.coro.resume`/`llvm.coro.destroy`. `runtime/async.c`
  has weak fallbacks so programs without coroutines still link.
- **-O0.** CoroSplit is mandatory, so the driver runs `default<O0>` when the module contains
  coroutines and no other pipeline is requested.
- **Generics.** Coroutines are monomorphized like any other generic code: element and result types
  go through the instantiation's type substitution.

## Tasks and the scheduler

`Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, the awaiters (`TaskAwaiter`, `ValueTaskAwaiter`,
the `Configured*` ones, `YieldAwaitable`) and `CancellationToken` are all represented by a pointer
to one csrt task object (`TypeMapper` maps these library types to `ptr`):

```
{ mt, i32 state, i32 payload_size, conts, i32 ncont, pad, reserved×2, payload @ +48 }
```

`default(ValueTask)` is a null pointer, meaning "completed".

**Threading model.** Everything runs on the main thread. A FIFO run queue (continuations,
`Task.Yield`, `Task.Run` work items) and a timer list (`Task.Delay`) are drained whenever someone
blocks on a task: `async Main` (the native `main` calls `csrt_task_wait` on the returned task),
`.Result`, `.Wait()` and `GetAwaiter().GetResult()`. With nothing runnable it sleeps until the
earliest timer. With no timers either, it reports a deadlock.

Supported: `await` on tasks, value tasks and configured awaitables; `Task.FromResult`,
`CompletedTask`, `Delay`, `Yield`, `WhenAll` (`Task[]` and `Task<T>[]`, with the result as `T[]`,
including loose `params` arguments), `WhenAny`; `Task.Run(Action | Func<Task> | Func<Task<T>>)`
queued on the scheduler, and `Task.Run(Func<T>)`, which runs inline; `.Result`, `.Wait()`,
`GetAwaiter().GetResult()`, `IsCompleted`, `ConfigureAwait`, which is a no-op; `ValueTask<T>`
constructors and `AsTask()`; async lambdas and local functions; async instance methods; async
`void`.

## Iterators

An iterator object is `{ mt, i32 state, i32 taken, frame, args, clone, reserved, current @ +48 }`.
Each element type gets a compiler-emitted method table (`ModuleCodeGen.IteratorMethodTable`) with
a real interface map:

- sync: `IEnumerable<T>`, `IEnumerable`, `IEnumerator<T>`, `IEnumerator`, `IDisposable`;
- async: `IAsyncEnumerable<T>`, `IAsyncEnumerator<T>`, `IAsyncDisposable`.

Each slot is a small thunk over the runtime, so `foreach`, `await foreach` and any code holding
an `IEnumerable<T>` reach iterators through ordinary interface dispatch.

- `MoveNext()` resumes the frame and returns `state == 1`.
- `Dispose()` on a suspended iterator destroys the coroutine. Each `yield` inside `try` has a
  destroy edge through the enclosing `finally` blocks, so early `break`/`return` from a `foreach`
  runs them, as in .NET.
- `GetEnumerator()` returns the object itself the first time. After that it calls the per-method
  `clone` thunk, which re-invokes the iterator with the saved arguments, so enumerating the same
  enumerable twice starts over.
- **Async iterators:** each `MoveNextAsync()` returns a fresh pending `ValueTask<bool>`. The next
  `yield return` completes it with `true`, and the end of the body completes it with `false`.
  Awaits inside the body suspend as in any async method.

## Deviations from .NET

- **No thread pool.** Work queued by `Task.Run` and the continuations of `Task.Delay` run on the
  main thread, in FIFO / deadline order. Programs whose output depends on true parallelism or
  thread identity behave differently. Programs that are deterministic under .NET behave the same.
- **`Task.Run(Func<T>)` with a non-task `T` runs the delegate immediately,** at the call site.
- **Background work is abandoned when `Main` returns,** as in .NET; the queue is not drained.
- **Exceptions.** An exception in an async method terminates the process like any unhandled
  exception. Faulted tasks, and rethrowing at `await`, come with exception-handling support.
  Cancellation tokens are accepted and ignored.
- **Elision.** `CoroElide` doesn't fire for iterators: the frame handle escapes into the heap
  iterator object, and `MoveNext` goes through the runtime. A future optimization could resume
  directly when the enumerator's concrete iterator is statically known.
- **Non-generic `IEnumerator.Current`** on a value-type iterator boxes a primitive or a user
  struct. Enums are not supported yet.
- **Not yet supported:** custom awaitables (user `GetAwaiter` patterns), `IAsyncEnumerable`
  `WithCancellation`/`ConfigureAwait`, `SynchronizationContext`/`TaskScheduler`, `Task.Wait`
  timeouts, and `TaskCompletionSource`.
