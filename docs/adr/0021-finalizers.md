# ADR 0021 — Finalizers

- **Status:** Accepted (2026-11-06). Finalization and `GC.SuppressFinalize` / `ReRegisterForFinalize` /
  `WaitForPendingFinalizers` implemented; the Objective-C reference-tracking hooks that need them are a later step.
- **Context:** [ADR 0004](0004-memory-management.md) (conservative non-moving mark-sweep),
  [ADR 0020](0020-concurrent-marking.md), [ADR 0011](0011-wasm32.md) (no threads), the Finalizers row of the feature matrix in
  [06](../06-roadmap.md), GitHub issues #23 and #50.

## Context

csharpc ran no finalizers: `~T()` compiled to a method nothing called, and `GC.WaitForPendingFinalizers` in a program that
declared one was a compile error. Programs rely on finalizers for their side effects (releasing handles, logging a leak,
`ObjectiveCMarshal`'s tracked objects), and the .NET for macOS bindings need them to exist at all (#23).

## Decision

**Registration at allocation.** A class whose chain (classes compiled by csharpc, from source or IL) declares a finalizer allocates
its instances with `csrt_alloc_finalizable(mt, size, thunk)`. The compiler emits one thunk per class that calls the chain's
finalizers, most derived first; csc's own base-finalizer call at the end of a destructor (`base.Finalize()`, a no-op on
`object`) is an intrinsic that does nothing. Classes without a finalizer take the unchanged `csrt_alloc` path, so programs
that don't use finalizers pay nothing (the runtime's finalization step returns at once when nothing is registered).
`MemberwiseClone` registers the copy, as .NET does.

**The registry lives outside the heap** (a hash table keyed by object address, under the collector's lock), like the weak table,
so it never keeps an object alive. After marking and after weak references are cleared, the collector moves every registered
object that was not reached to a queue, sorted by registration order (what .NET prints for a batch), then marks the whole
queue and what it reaches: the objects survive this collection (resurrection). The queue is sized at registration, so the
stopped world never allocates. Pending objects stay marked until their finalizer ran; the next collection that finds them
unreachable frees them. It works in the default collector, with the nursery and with concurrent marking (the step runs in
the stopped phase of each).

**A finalizer thread** (a background managed thread, started at the first registration, so a program with finalizable
objects is multithreaded from then on, as in .NET) takes objects off the queue and runs them; `GC.WaitForPendingFinalizers`
waits until the queue is empty and no finalizer is running. On WebAssembly (no threads) it runs the pending finalizers on the
calling thread, and they run at no other time. Nothing runs at process exit (.NET Core doesn't either). An exception that
escapes a finalizer is unhandled (stderr report, abort, exit code 134), from the finalizer thread, as in .NET.

`GC.SuppressFinalize` removes the registration (or cancels it for an object already queued); `GC.ReRegisterForFinalize`
registers again (at most one registration per object: a second call is a no-op, where .NET can queue the object twice).

## Consequences and limits

- The collector is conservative: a stale word on a stack or in a register can keep an object reachable, so its finalizer runs
  later than under precise .NET, or never (the same property already applies to weak references). Tests count results over
  many objects instead of expecting each one.
- Finalizers of one batch run in registration order; .NET documents no order, and its observed order matches.
- If a derived finalizer throws, the base finalizers do not run first (csc wraps the base call in `finally`); the process ends
  either way, so only output printed by a base finalizer in that case differs.
- `WeakReference(trackResurrection: true)` is not distinguished: weak slots are cleared before resurrection.
- The corelib's own types (`SafeHandle`, `CriticalFinalizerObject`, streams) declare no finalizers: a handle that is never
  disposed is released at process exit, not when collected.
- Objects created by `Activator`/reflection helpers other than `Activator.CreateInstance<T>()` and `new` are not yet registered
  (`RuntimeHelpers.GetUninitializedObject` makes unregistered instances).
- Next: the collector's tracked-reference callbacks and the `ObjectiveCMarshal` hooks (#23), verified on the macOS runner.
