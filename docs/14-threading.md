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

## Garbage collection with threads (src/runtime/gc.c)

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

## Monitors (src/runtime/threads.c)

Objects never move, so monitors live in a hash table of 256 buckets keyed by object address. Every
field of an entry (owner, recursion count, the number of threads with an interest in it, the
condition variables and the `Wait` queue) is guarded by its bucket's mutex, which is also the mutex
the condition variables wait with: an uncontended `Enter`/`Exit` pair is one short critical section
each. An entry lives while some thread owns, enters or waits for it, then goes to its bucket's spare
list (zeroed mutexes and condition variables are initialized ones on glibc, Windows and WASI; macOS
initializes them). `Wait` queues its thread in arrival order; `Pulse` releases the first waiter and
`PulseAll` every waiter, each re-checking its own flag so a signal a timed-out wait swallows, or a
spurious one, neither loses nor invents a pulse. `Thread.Interrupt` ends that wait (and `Sleep` and
wait-handle waits) with `ThreadInterruptedException`, after `Wait` has reacquired the lock.

`System.Threading.Lock` keeps its state in the object, as .NET's does: the owner's lock id (a small
per-thread number, 0 = free), the recursion count and the number of parked waiters. An uncontended
`Enter` is one compare-and-swap, `Exit` one store plus a load of the waiter count; a contended `Enter`
spins briefly, then parks on the `Lock`'s monitor-table entry (its `lock_released` condition). Kestrel
and System.IO.Pipelines take a `Lock` a dozen times per request. `Monitor` on a `Lock` instance is a
separate lock, as in .NET. A `Lock.Scope` value is represented by the `Lock` pointer itself.

## Parallel loops

`Parallel.For` builds a job on the calling thread's stack (`{ body thunk, context, range,
counter }`) and publishes it to a pool of `ProcessorCount - 1` background workers created on first
use. The calling thread and the workers claim chunks (about 8 per participant) with an atomic
counter. The compiler emits a small thunk per call site that loads the delegate (and, for
`ForEach`, the array element) from the context and invokes it — including multicast delegates.
Nested parallel loops, and loops started on pool threads, run inline.

Exceptions: an iteration that throws on the calling thread propagates out of `Parallel.For` (a
C `cleanup` handler retires the job first, so workers never see a dead frame). An exception on a
pool worker is captured with an *exception trap* (src/runtime/eh.c: if no C# handler catches it on
that thread, its finally blocks run and control returns to `csrt_trap_call`) and rethrown on the
calling thread after the loop drains.

## Deviations from .NET and known gaps

- `Parallel.For` rethrows the first exception itself; .NET wraps all exceptions in an
  `AggregateException`. `ParallelLoopState`/`ParallelOptions`/`Break`/`Stop` overloads, `ForEach`
  over non-array `IEnumerable<T>`, and thread-local `localInit` overloads are not mapped yet.
- `Task.Run` and `await` continuations run on the task scheduler's own pool (src/runtime/async.c,
  see [10-async.md](10-async.md)). Those workers are started through `csrt_start_background_thread`
  and are separate from the `Parallel.*` workers here, so `Parallel.For` inside `Task.Run` still
  fans out.
- `Thread.Sleep(TimeSpan)`, `Join(TimeSpan)`, `Thread.Interrupt`/`Abort` (obsolete), thread
  priorities are not supported (the current culture is per thread and flows like in .NET, see
  [19 — Globalization](19-globalization.md)). `ThreadLocal<T>`, `SpinLock`, `Barrier`,
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
