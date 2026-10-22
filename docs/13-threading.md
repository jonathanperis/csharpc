# 13 — Threading

csharpc programs can run on real OS threads with .NET semantics. This page describes how the
garbage collector, the runtime and the compiler cooperate, what is supported, and what still
isn't.

## What works

| Area | Supported |
|---|---|
| `System.Threading.Thread` | `new Thread(ThreadStart / ParameterizedThreadStart / lambda)`, `Start()`, `Start(obj)`, `Join()`, `Join(int)`, `IsAlive`, `Name`, `ManagedThreadId` (main thread = 1), `IsBackground`, `Thread.CurrentThread`, `Thread.Sleep(int)`, `Thread.Yield()`, `Thread.SpinWait`, `Thread.MemoryBarrier()` |
| Process lifetime | like .NET, the process outlives `Main` until every *foreground* thread has finished; background threads are abandoned |
| Unhandled exceptions | an exception escaping a thread's delegate is reported ("Unhandled exception. …") and aborts the process (exit 134), as in .NET |
| `lock` / `Monitor` | real reentrant monitors: `Enter`, `Enter(o, ref taken)`, `TryEnter` (+timeout / `ref taken`), `Exit` (`SynchronizationLockException` when not owned), `IsEntered`, `Wait` (+timeout), `Pulse`, `PulseAll` |
| `System.Threading.Lock` (.NET 9) | `lock (l)`, `Enter`, `Exit`, `TryEnter`, `EnterScope`, `IsHeldByCurrentThread` |
| Wait handles | `ManualResetEventSlim`, `ManualResetEvent`, `AutoResetEvent` (`Set`/`Reset`/`Wait`/`WaitOne`/`IsSet`), `SemaphoreSlim` (`Wait`/`Release`/`CurrentCount`), `CountdownEvent` (`Signal`/`AddCount`/`Wait`/`IsSet`/`CurrentCount`) |
| `Interlocked` | `Increment`, `Decrement`, `Add`, `Exchange`, `CompareExchange` (int, long, float, double, references, generic `T`), `And`, `Or`, `Read`, `MemoryBarrier` — inline LLVM `atomicrmw`/`cmpxchg`, sequentially consistent |
| `Volatile` / `volatile` | `Volatile.Read`/`Write` and `volatile` fields lower to acquire loads / release stores |
| `[ThreadStatic]` | static fields become LLVM `thread_local` globals (one copy per thread, default-initialized on new threads) |
| `Parallel` | `Parallel.For(int/long, …, Action<int/long>)`, `Parallel.ForEach(T[] , Action<T>)`, `Parallel.Invoke(params Action[])` on a runtime worker pool |
| `Environment` | `ProcessorCount`, `CurrentManagedThreadId` |
| Console | each `Console.Write`/`WriteLine` is atomic with respect to other threads (as `Console.Out` is synchronized in .NET) |

## Garbage collection with threads (runtime/gc.c)

The collector stays conservative, non-moving, mark-sweep; threads add four things:

1. **Registration.** Every managed thread has a registry entry (`csrt_GcThread`): stack bounds,
   the address of its thread-local allocation caches, its static TLS block and a root for its
   `Thread` object. The creator *prepares* the entry (so the `Thread` object and its delegate stay
   rooted while the OS thread starts); the new thread *attaches* (records its stack via
   `pthread_getattr_np` and its TLS block via `dl_iterate_phdr`'s `dlpi_tls_data`) before it
   touches the managed heap, and *detaches* before it exits, handing unused cached slots back.
2. **Thread-local allocation.** Each thread owns one claimed bitmap word per size class
   (`_Thread_local AllocCache tl_cache[]`, `local-exec` TLS model). The inlined fast path is the
   same ctz/blsr sequence as before plus a `%fs`-relative base — no atomics, no lock. Claiming a new
   word, large objects and collections take one global lock, which is skipped entirely until the
   program creates its first thread.
3. **Stop the world.** The collecting thread sends `SIGPWR` to every running thread. The signal
   handler spills the callee-saved registers (`setjmp` + `__builtin_unwind_init`; the kernel's
   signal frame above it holds the rest), records its stack pointer, acks on a semaphore and waits
   in `sigsuspend` until `SIGXCPU` resumes it. Handlers use `SA_RESTART`; `Thread.Sleep` resumes
   with the remaining time after an interruption. Threads blocked in locks, joins or I/O are
   stopped the same way.
4. **No allocation while stopped.** A stopped thread may hold the malloc, stdio or loader lock, so
   the collector never calls them with the world stopped: the mark stack grows with `mmap`, large
   objects are sorted in place (heapsort), root segments are cached at startup, and large blocks
   freed by the sweep are released only after the world restarts.

Roots are the writable segments of the executable, every thread's stack (from its recorded stack
pointer, or the collector's own frame) and every thread's TLS block (which covers `[ThreadStatic]`
fields, the in-flight exception of each thread and the current `Thread` object).

A stopped thread may be in the middle of the non-atomic allocation fast path, holding a fresh slot
only in a register. So after marking, every slot of each thread's currently cached word is kept
allocated for this cycle ("cache protection"); dead objects there are reclaimed by a later
collection. Thread caches are never reset by the collector.

## Monitors (runtime/threads.c)

Objects never move, so monitors live in a striped hash table keyed by object address. An entry
holds a `pthread_mutex_t` (the lock), a condition variable (`Wait`/`Pulse`), the owner and the
recursion count; it is reference-counted by threads that hold, wait for or are entering it, and
freed when the count drops to zero. `System.Threading.Lock` uses the same monitors keyed by the
`Lock` object; a `Lock.Scope` value is represented by the `Lock` pointer itself.

## Parallel loops

`Parallel.For` builds a job on the calling thread's stack (`{ body thunk, context, range,
counter }`) and publishes it to a pool of `ProcessorCount - 1` background workers created on first
use. The calling thread and the workers claim chunks (about 8 per participant) with an atomic
counter. The compiler emits a small thunk per call site that loads the delegate (and, for
`ForEach`, the array element) from the context and invokes it — including multicast delegates.
Nested parallel loops, and loops started on pool threads, run inline.

Exceptions: an iteration that throws on the calling thread propagates out of `Parallel.For` (a
C `cleanup` handler retires the job first, so workers never see a dead frame). An exception on a
pool worker is captured with an *exception trap* (runtime/eh.c: if no C# handler catches it on
that thread, its finally blocks run and control returns to `csrt_trap_call`) and rethrown on the
calling thread after the loop drains.

## Deviations from .NET and known gaps

- `Parallel.For` rethrows the first exception itself; .NET wraps all exceptions in an
  `AggregateException`. `ParallelLoopState`/`ParallelOptions`/`Break`/`Stop` overloads, `ForEach`
  over non-array `IEnumerable<T>`, and thread-local `localInit` overloads are not mapped yet.
- **`Task.Run` still runs on the single-threaded async scheduler** (runtime/async.c). The plan:
  back `Task.Run` with this worker pool, protect task state and continuation lists with a
  per-task lock (or CAS on the state word), and run continuations on the pool (or post them to the
  awaiting thread's queue when it blocks in `Wait`/`Result`). This waits for the async exception
  work (faulted tasks) to land so the task object layout is settled.
- `Thread.Sleep(TimeSpan)`, `Join(TimeSpan)`, `Thread.Interrupt`/`Abort` (obsolete), thread
  priorities and culture-per-thread are not supported. `ThreadLocal<T>`, `SpinLock`, `Barrier`,
  `ReaderWriterLockSlim`, `Mutex`/`Semaphore` (named, OS-level) and concurrent collections are not
  implemented yet (concurrent collections belong in the C# corelib).
- Monitor `Wait` may wake spuriously (pthread condition variables); idiomatic `while` loops are
  unaffected.
- With nullable analysis enabled, Roslyn's CFG drops `lock (System.Threading.Lock)` inside lambdas
  to an Invalid operation. The driver lowers with the nullable context disabled (warnings still use
  the real settings with `-W`); a `#nullable enable` directive around such a `lock` is reported as a
  compile error instead of silently losing the lock.
- The collector is still stop-the-world and single-threaded while marking; marking in parallel is
  a natural next step for large heaps on many cores.
