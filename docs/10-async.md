# async/await and iterators

csharpc compiles `async` methods, iterators (`yield return`) and async iterators
(`IAsyncEnumerable<T>`) to **LLVM coroutines**. It uses the switch-resumed lowering, the same
machinery clang uses for C++20 coroutines. Roslyn's IOperation/CFG doesn't lower any of these:
`await` stays an `IAwaitOperation` inside expressions and `yield return` stays an
`IReturnOperation` (kind `YieldReturn`). The state machine is built by LLVM's `CoroSplit` pass,
not by csharpc.

Code: `src/Csharpc/CodeGen/Coroutines.cs` (classification), `FunctionCodeGen.Async.cs`
(lowering), `ModuleCodeGen.Async.cs` (iterator method tables), `src/runtime/async.c` (tasks,
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
  `csrt_coro_destroy` wrappers around `llvm.coro.resume`/`llvm.coro.destroy`. `src/runtime/async.c`
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
{ mt, i32 state, i32 payload_size, conts, i32 ncont, i32 lock, exception, aggregate, payload @ +48 }
```

`default(ValueTask)` is a null pointer, meaning "completed".

**Threading model** (.NET semantics for console apps, `src/runtime/async.c`):

- **Thread pool.** `Task.Run` work items run on a pool of background worker threads
  (processor-count workers, minimum 2), concurrently with the caller. Pool threads never keep the
  process alive: when `Main` returns, the process exits.
- **Continuations.** An `await` on an already-completed task continues synchronously. Otherwise the
  coroutine registers itself as a continuation and is resumed by the thread that completes the task,
  inline, as .NET does. Past 32 nested inlined continuations it is resumed on the pool instead.
  `Task.Yield()` always resumes on the pool. There is no `SynchronizationContext`.
- **Publishing a frame safely.** Before handing its frame to the runtime, a coroutine executes
  `llvm.coro.save`. From that point it counts as suspended, so another thread may resume it even
  before it reaches `llvm.coro.suspend`. Without an explicit save, LLVM would place an implicit one
  just before the suspend, after the frame was already published, and a fast resume on another
  thread would use a stale resume index.
- **Blocking.** `.Result`, `.Wait()`, `GetAwaiter().GetResult()` and `async Main` (the native `main`
  calls `csrt_task_wait` on the returned task) block on a condition variable. If a pool worker
  blocks this way and no other worker is free, another one is started.
- **Timers.** A timer thread hands expired `Task.Delay` / `CancelAfter` timers to the pool, earliest
  deadline first. Once every 100 ms it also checks for starvation: queued work with no progress and
  no idle worker. When that happens it injects a worker, up to 512. This is the
  `CountdownEvent`/`Monitor.Wait`-on-pool-threads case, which .NET handles with its starvation
  detector.
- **Thread safety.** Each task's state and continuation list are guarded by a per-task spinlock,
  held in the former padding word, so the compiler ABI is unchanged. State reads are atomic;
  `WhenAll` counts down atomically; `WhenAny` picks the first completion under the result's lock.
- **Cancellation.** Callbacks run synchronously inside `Cancel()`, most recently registered first.
  A `Task.Delay(ms, token)` registers its own cancellation as one of those callbacks, as in .NET.
- **GC.** Work queues, timers and continuation lists are GC arrays rooted by globals. Iterator
  exceptions are parked in thread-local storage, which the collector also scans.

Supported: `await` on tasks, value tasks and configured awaitables; `Task.FromResult`,
`CompletedTask`, `Delay` (with tokens), `Yield`, `WhenAll` (`Task[]` and `Task<T>[]`, with the
result as `T[]`, including loose `params` arguments), `WhenAny`; `Task.Run(Action | Func<T> |
Func<Task> | Func<Task<T>>)` on the pool, where `Func<T>` uses a compiler-generated per-`T` thunk;
`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `IsCompleted`/`IsFaulted`/`IsCanceled`,
`ConfigureAwait` (a no-op without a synchronization context); `ValueTask<T>` constructors and
`AsTask()`; async lambdas and local functions; async instance methods; async `void`; faulted and
canceled tasks, `AggregateException`, `TaskCompletionSource`, `CancellationTokenSource`.

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

- **Thread injection.** The pool doesn't do .NET's hill-climbing. It starts processor-count workers
  and injects more only when work is starving or a worker blocks on a task.
- **Thread identity.** Which pool thread runs a continuation may differ from .NET. Programs whose
  output depends on thread ids or on interleaving are nondeterministic under both runtimes.
- **Elision.** `CoroElide` doesn't fire for iterators: the frame handle escapes into the heap
  iterator object, and `MoveNext` goes through the runtime. A future optimization could resume
  directly when the enumerator's concrete iterator is statically known.
- **Non-generic `IEnumerator.Current`** on a value-type iterator boxes a primitive or a user
  struct. Enums are not supported yet.
- **Not yet supported:** custom awaitables (user `GetAwaiter` patterns), `IAsyncEnumerable`
  `WithCancellation`/`ConfigureAwait`, `SynchronizationContext`/`TaskScheduler`, `Task.Wait`
  timeouts, `TaskCreationOptions` (`LongRunning`, `RunContinuationsAsynchronously`).
