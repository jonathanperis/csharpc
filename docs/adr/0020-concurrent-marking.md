# ADR 0020 — Mostly-concurrent marking

- **Status:** Proposed (2026-11-01). Step 1 (the barrier and its cost) in progress on `ci/m7-gc-concurrent`.
- **Context:** [ADR 0004](0004-memory-management.md) (conservative non-moving mark-sweep, the opt-in nursery and its
  compiler write barrier), [ADR 0011](0011-wasm32.md) (wasm32: no threads, shadow-stack roots), M7 in
  [06](../06-roadmap.md) (TechEmpower plaintext p99 at 4096+ connections is one of the metrics).

## Context

Every collection stops the world for all of its work. On the TechEmpower server at 4096+ connections the live set is about
1.4 M objects and 36,000 large objects, and a collection pauses 20-30 ms; plaintext p99 at 4096 connections is 66-74 ms
against NativeAOT's 60-62 ms, the one plaintext metric csharpc loses. Timing each phase of a collection on a heap of that
shape (36,000 4 KiB arrays and 1.3 M small objects alive):

| phase | ms |
|---|---:|
| sort of the large-object table | 0.05 |
| roots (data segments, stacks, TLS) | 0.02 |
| **marking (drain)** | **21.4-23.8** |
| weak handles and sweep | 0.2 |

Marking is the whole pause, so nothing outside it (lazy sweeping, cheaper roots) can shorten it. The parallel marker kept
behind `-DCSRT_GC_PARALLEL_MARK` made it longer, not shorter (35 ms against 21 ms locally; ADR 0004, "Parallel marking lost
on this machine"): the mutators sit idle while marking runs either way, and the server gets two CPUs. What shortens the
pause is marking while the mutators run, which needs a write barrier on reference stores.

The compiler already has one: `--gc-nursery` (ADR 0004, "Increment shipped") rewrites every heap reference store in the
finished module into `csrt_gc_write(slot, value)` or `csrt_gc_cards_dirty(address, n)`
(`CodeGen/ModuleCodeGen.WriteBarriers.cs`), and the C runtime's stores go through `CSRT_STORE_REF`/`CSRT_DIRTY`. A review
made that pass cover every store path (field and array stores, aggregates, memcpy/memmove, atomics, Interlocked, the IL
front-end, the corelib, the runtime) and the `GC nursery` workflow runs the suites with it. Its cost, measured for this
ADR with the barrier compiled in and the nursery's collections off (`CSHARPC_GC_NURSERY=1`, `CSRT_GC_NURSERY=0`; executed
instructions, callgrind, x86-64):

| benchmark | instructions |
|---|---:|
| strbuild | +45.4 % |
| sbops | +13.7 % |
| binarytrees | +12.6 % |
| churn | +9.3 % |
| listsort | +3.0 % |
| wordfreq | +2.9 % |
| shapes | +0.4 % |
| quicksort | 0.0 % |

That barrier is an out-of-line call per store, dirties a range before every memcpy/memmove into the heap whatever the
copied type holds (strbuild's `char` chunk copies), and links the nursery's allocation-path code into the runtime. A
concurrent marker cannot ship with that cost: speed comes first. The barrier has to be cheap when no marking runs (one
load of a flag and a not-taken branch, inline), and do nothing for copies of data without references.

## Decision

A **mostly-concurrent, incremental-update (Dijkstra) marker** on a thread of its own, sharing the nursery's barrier
pass, with a short final stop-the-world remark. Not snapshot-at-the-beginning.

### The cycle

1. **Initial pause (stop the world):** sort the large-object table, set `marking`, scan the roots (data segments, every
   thread's stack and registers, TLS) and push what they reach. Roots are what the existing collector scans; the
   pause is the root scan only (0.02 ms above).
2. **Concurrent mark:** the marker thread drains the mark stack while the mutators run. It reads objects that mutators
   may be writing: a conservative marker tolerates any word it reads (it checks every candidate against the heap's
   allocation bitmaps), so a torn or stale value costs at most a retained object, never a crash.
3. **Pre-cleaning (concurrent):** the marker takes the mutators' barrier buffers (below) and marks from them, repeating
   while they refill faster than a threshold.
4. **Remark (stop the world):** rescan the roots, take every thread's barrier buffer, rescan the coroutine frames resumed
   since the last pre-cleaning pass, mark to completion, process weak handles and ephemerons, sweep, clear `marking`.
   Its length is the roots plus what was stored since the last pre-cleaning pass, which pre-cleaning keeps small; that
   is the bound the < 2 ms target rests on.

### Incremental update, not snapshot-at-the-beginning

A snapshot-at-the-beginning (Yuasa) barrier logs the value a store overwrites, so it must load the old value before every
store, and it is unsound for stores the barrier cannot see. csharpc has two such kinds: LLVM's coroutine pass spills
locals into heap frames after the barrier pass ran, and the conservative roots include values only in registers. An
incremental-update (Dijkstra) barrier logs the value stored, needs no load, and the remark rescans the roots, which covers
values that live only in registers or on stacks. Frames get their own rule (below). Allocation during marking is black
(the slot's mark bit is set when the allocation word is claimed), so new objects are never freed by the cycle that was
running when they were allocated, and the barrier logs what is stored into them.

### The barrier

The nursery pass stays the single place stores are found; what each rewritten store does depends on the build:

- `store ptr V, heap` becomes `if (csrt_gc_marking) csrt_gc_log(V); store`, with the flag test inline and the log call
  out of line (cold). `csrt_gc_log` appends V to the thread's buffer (256 entries) and hands a full buffer to the marker.
  No chunk lookup is needed: the value is what matters for an insertion barrier, not where it was stored.
- Aggregate stores and memcpy/memmove into the heap: when the copied type holds references, `if (marking)
  csrt_gc_log_range(destination, n)` after the copy (the marker scans the range when it takes the buffer, which happens
  after the store in program order). Copies of types without references (char, byte, struct of integers) get no barrier:
  the pass sees the IR type of an aggregate store, and memcpy/memmove calls carry it when csharpc emits them.
- `cmpxchg`/`atomicrmw`/Interlocked on references log the new value.
- The C runtime's `CSRT_STORE_REF`/`CSRT_DIRTY` map to the same calls.
- With `--gc-nursery` the barrier also dirties the card first, as today; the two compose.

### Coroutine frames, stacks, pinned objects, large objects

- **Coroutine frames** (async methods, iterators) are heap objects that CoroSplit writes without a barrier. A frame's
  fields change only while its coroutine runs, so the runtime logs the frame itself when it resumes one during marking
  (`csrt_coro_resume`, the scheduler's continuation paths): the marker rescans a logged frame, and the remark rescans
  frames resumed after the last pre-cleaning pass.
- **Thread stacks and registers** are conservative roots, rescanned at the remark (stop the world); the concurrent phase
  never reads a running thread's stack.
- **Pinned objects** need nothing: the collector does not move objects.
- **Large objects** are marked like small ones (the sorted table is read-only while marking: large allocations during a
  cycle are appended and black, the table is re-sorted at the remark). The barrier logs values, so stores into large
  objects need no per-object dirty state.

### Weak handles, ConditionalWeakTable, finalization

Weak slots and ephemerons are processed only at the remark, after marking is complete, exactly as today; their targets
are not roots, and the barrier's buffers hold strong references only while a cycle runs. csharpc runs no finalizers.
`WeakReference.Target` reads during marking return the target (a resurrection of a white object by a read is the
classic hazard); the getter logs the value it returns while `marking` is set, so a target read during the cycle stays
alive through its remark.

### What stays stop-the-world

- **wasm32**: no threads, so no marker thread; the barrier pass is not run for the concurrent mode (the nursery's
  remains opt-in there).
- Small heaps: below a live size threshold the collection stays fully stop-the-world (no barrier buffers, no thread
  hand-off), as the parallel marker's threshold does today.
- `GC.Collect()` and `CSRT_GC_STRESS` collections are stop-the-world.

## Consequences

- Every reference store pays an inline load and branch, and stores during a cycle pay a buffer append. Step 1 measures
  that cost on the Benchmarks workflow before any marker exists; if it costs throughput the decision is revisited
  before the marker lands.
- Marking work is the same or more (re-marking logged values and frames), on another CPU. With two CPUs for the server,
  the marker competes with the mutators for the cycle's duration, so throughput during a cycle drops; the pause, not the
  total GC CPU, is what improves.
- The barrier's coverage becomes a correctness property of the default build, not only of `--gc-nursery`: the
  `GC nursery` workflow's stress runs become the concurrent mode's tests too, plus samples for each hazard (a reference
  moved from a stack local into a field during marking, an object reachable only from a frame resumed during marking,
  a WeakReference target read during marking, a ConditionalWeakTable value whose key is stored during marking).

## Step 1: the barrier's cost

`--gc-concurrent` (`CSHARPC_GC_CONCURRENT=1`) runs the nursery pass's store discovery with the concurrent action
(`CodeGen/ModuleCodeGen.ConcurrentBarriers.cs`): before each heap reference store, an inline volatile load of
`csrt_gc_marking` and a branch to an out-of-line, cold `csrt_gc_log(value)`; before aggregate stores holding references,
memcpy/memmove into the heap and pointer atomics, the same test around `csrt_gc_log_range`. The runtime's
`CSRT_STORE_REF`/`CSRT_DIRTY` do the same under `-DCSRT_GC_CONCURRENT`. No cycle sets the flag yet, so this is the
barrier's steady-state cost. Executed instructions (callgrind, x86-64, locally), the default build against
`--gc-concurrent`, and the nursery's barrier for comparison:

| benchmark | nursery barrier | concurrent barrier |
|---|---:|---:|
| strbuild | +45.4 % | +0.7 % |
| sbops | +13.7 % | +3.0 % |
| binarytrees | +12.6 % | +4.5 % |
| churn | +9.3 % | +2.7 % |
| listsort | +3.0 % | 0.0 % |
| wordfreq | +2.9 % | 0.0 % |
| shapes | +0.4 % | +0.1 % |
| quicksort | 0.0 % | 0.0 % |

The stripped executables keep their sizes. What remains is the initializing stores of fresh objects (binarytrees'
nodes, StringBuilder chunks): a store into an object allocated by the same function with no call in between could skip
the barrier if objects allocated during a cycle were scanned at the remark instead of logged, which moves that cost
into the pause; the Benchmarks workflow's wall times decide whether that trade is needed. The workflow now also
reports executed instructions with `--gc-concurrent` on both architectures.

## Plan

1. The cheap barrier (inline flag test, reference-free copies skipped), behind `--gc-concurrent`, with its cost on the
   Benchmarks workflow (executed instructions and wall time against the default build).
2. The marker thread, the barrier buffers, allocate-black, the frame rule, the remark, behind the same flag; GC samples,
   the nursery workflow's stress runs and hazard samples green with it.
3. M7 perf and TechEmpower runs with the flag: pauses, plaintext p99 at 4096+, throughput. Default on only when the
   pause target holds with no throughput loss.

## Alternatives considered

- **Parallel stop-the-world marking:** measured slower (above, ADR 0004), and it does not bound the pause.
- **Generational (the nursery):** shorter minors, but ADR 0004 measured churn throughput -37 % and longer livemutate
  pauses; the old generation's marking is still one stop-the-world pass.
- **Snapshot-at-the-beginning:** needs a load per store and misses the coroutine spills (above).
- **Lazy sweeping:** the sweep is 0.2 ms of the pause.
