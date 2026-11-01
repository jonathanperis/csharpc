# ADR 0020 — Mostly-concurrent marking

- **Status:** Accepted (2026-11-01), behind `--gc-concurrent`. Step 1 (the barrier) on main; step 2 (the marker)
  on `ci/m7-gc-concurrent`.
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

## Step 2: the marker

Implemented in `src/runtime/gc.c` under `CSRT_HAS_CONCURRENT` (a `--gc-concurrent` runtime on a native target); the
default runtime is unchanged.

- **Cycle start.** An allocation that reaches the trigger, under the allocation lock, runs the initial mark: stop the
  world, sort what was appended to the large-object table since it was last sorted and merge it in, make the
  remaining slots of every thread's allocation words black, scan the roots, set `csrt_gc_marking`, resume, wake the
  marker thread. Cycles run only for multithreaded programs with a live heap of 8 MiB or more
  (`CSRT_GC_CONCURRENT_MIN_MB`), not under `CSRT_GC_STRESS`; `CSRT_GC_CONCURRENT=0` turns them off.
- **Marking.** The marker thread drains the mark stack and the barrier buffers the mutators hand over when full
  (512 entries; values, and ranges for copies). Mark bits are set with an atomic OR, because claim_word sets the bits
  of words claimed during a cycle (allocate-black) in the same bitmap words. The marker looks chunks up in the
  never-freed chunk-set tables the nursery's barrier already uses, and large objects in the table as sorted at the
  cycle's start; a table regrown during the cycle is a copy, the old one is freed after the cycle, and the marks the
  marker set in it are copied over at the remark.
- **Remark.** The marker stops the world: every thread's partial buffer, the roots again, every marked coroutine
  frame (chunks with a frame are remembered, as the nursery does), marking to completion, weak handles and
  ephemerons, the threads' allocation words, the large objects' sweep. The small objects are swept after the world
  resumes, 32 chunks at a time under the allocation lock, by the marker and by allocations that would otherwise map
  a new chunk; until a chunk is swept, its dead objects stay allocated and words claimed in it are black.
- **Pacing.** Allocation continues during a cycle; an allocation that reaches twice the trigger waits for the cycle.
  Between cycles the marker sorts newly appended large objects every 20 ms (a copy, outside the lock, merged under
  it), so the initial mark sorts only what came in the last milliseconds.

Locally (4 cores, shared machine) on a server-shaped program — 36,000 connections with a 4 KiB buffer and eight
small arrays each, a ring of one million objects, two threads replacing ring entries and connection state with fresh
objects (old-to-new edges through the barrier) for six seconds:

| collector | pauses | pause max | longest mutator iteration | GC time | iterations |
|---|---|---:|---:|---:|---:|
| stop-the-world (default) | 11 collections, 1,033 ms | (per collection ~95 ms) | 145 ms | 1,033 ms | 81,494 |
| concurrent | 20 pauses: p50 0.70 ms, p99 2.90 ms | 2.90 ms | 12-27 ms (scheduling on a shared machine) | 17.8 ms of pauses | 77,321 |

The ring's and the connections' checksums match. Of the remark, the coroutine frames took 0.07-0.15 ms here (few
frames); the TechEmpower runs measure it with thousands of suspended request frames. Tests: sample 838 (each
hazard above, recorded from .NET), and the `GC concurrent` workflow (the differential suite and the conformance
corpus with cycles on every multithreaded program and a collection every MiB, the threaded samples ten times).

### On CI (M7 perf 41306893767, Benchmarks 41306874150, GC concurrent 41306874237)

- **Platform entry, plaintext (16384 connections at the end), CSRT_GC_STATS of the profiled server:**
  stop-the-world, 7 collections of 93-164 ms in all per run (13-23 ms each); concurrent, 7 cycles with
  14 pauses, p50 0.64-0.87 ms, max 5.2-5.4 ms. Every maximum is a remark, and 4.5 ms of it is the coroutine
  frames' rescan (thousands of suspended connection frames); initial marks 0.6-4 ms, the rest of a remark
  under 0.6 ms. Cached queries and JSON: max 0.6-1.0 ms.
- **Throughput:** the runs landed on different CPU types, so the ratios only bound the effect: interim
  plaintext at 256/1024 connections 1.07-1.10x NativeAOT with the flag against 1.11-1.16x without, 4096
  0.99-1.05 against 0.95-1.03; the Platform entry's JSON, /db and cached queries within the runner spread.
- **Benchmarks wall time (arm64, the steadier runner):** binarytrees +10.7 %, churn +9.3 %, exprtree +8.3 %,
  the others within +-3 %; x86-64's second run was slower across the board (fib, fasta, nbody +7-10 % with
  identical instruction counts). GC latency suite: livemutate's pause 32.0 -> 0.21 ms (x86-64), 36.6 ->
  0.36 ms (arm64), its longest operation 17.6 -> 1.0 ms; churn's longest operation 0.2 -> 1.8 ms; RSS
  +33 % (floating garbage: objects allocated during a cycle live until the next).
- **Tests:** the GC concurrent workflow (differential suite at -O0/-O2, conformance corpus, threaded samples
  ten times) passes.

### The coroutine-frame rescan

The remark rescans every marked frame because their spills have no barrier. Options, cheapest first:

1. **Concurrent pre-cleaning of frames:** the marker scans the frames before the remark; the remark then
   rescans only frames resumed since, which the runtime notes when it resumes one (`csrt_coro_resume` and the
   scheduler's continuation paths; iterators' MoveNext resumes in generated code, which the barrier pass can
   wrap). Frames suspended for the whole cycle — most connections in a server — are scanned once, outside the
   pause.
2. **A frame registry with dirty bits:** frames in a separate list with a byte each, set on resume; the same
   effect without walking the remembered chunks.
3. **Barriers on spills:** run the barrier pass after CoroSplit, which needs a second pass over the split
   functions and costs a flag test per spill.

Option 1 is done, in the form of a running-frame stack. Each thread keeps a stack of the frames it is running:
`csrt_coro_resume` and the frame's creation (`csrt_coro_alloc*`) push one, and its suspension or the end of its first
run (the barrier pass puts `csrt_gc_frame_exit` after each call of a coroutine ramp) pops it. A frame that suspends
while marking is logged as a frame entry, and the marker scans it whole. The remark then rescans only the frames
still running on some thread. When a thread's stack overflows (more than 64 nested frames), the remark falls back to
rescanning every remembered frame. Locally, with 8000 suspended loops, the frames' part of the remark went from
milliseconds to 0.00 ms, and the longest pause was 1.2 ms.

## Step 3: the barrier's remaining cost and the heap

- **Fresh values have no barrier.** After the optimizer runs, `ConcurrentBarrierElision` removes the barrier of a
  store whose value is null or fresh. A value is fresh if it comes from an allocation, from a call to a function
  that returns only fresh values, or from a phi or select of fresh values. The thread has held such an object
  since it allocated it, so it is always marked during a cycle:
  - if it was allocated before the initial mark, that mark took it as a root;
  - if it was allocated during the cycle, it is black.

  Because of runtime LTO, the allocator is inlined, so `gc.c` tags its result with an empty asm (`FRESH`). The pass
  recognizes that tag and then removes it. One window remains: a thread can be stopped in the fast path after it
  took a slot's bit out of `free_bits` but before it holds the slot's address. To close it, the initial mark makes
  the last slot handed out by each cache gray (`AllocCache.claimed`). Running binarytrees' `new TreeNode(Bottom(),
  Bottom())` now needs no flag test at all.
- **Mark bits are atomic only during a cycle.** Stop-the-world collections in a `--gc-concurrent` build set mark
  bits with a plain OR again.
- **Pacing.**
  - A cycle starts earlier than the trigger, by what the last cycle allocated (at most half the budget).
  - The next budget grows from the live heap minus what the cycle allocated. Those objects are black and survive the
    cycle whether or not they are reachable, and before this change they compounded into every following budget.

Executed instructions with the flag against the default build (x86-64, callgrind):

| Program | Before step 3 | After step 3 |
|---|---|---|
| binarytrees | +12.1 % | +0.6 % |
| churn | +8.4 % | +1.0 % |
| exprtree | +5.8 % | +3.1 % |
| listsort | | +2.5 % |
| wordfreq | | +5.5 % |

GC latency suite, peak RSS over 6 local runs (4 threads, median):

| Build | Peak RSS |
|---|---|
| Stop-the-world | 209 MiB |
| Concurrent, before step 3 | 256 MiB (+23 %) |
| Concurrent, after step 3 | 207 MiB |

Wall time did not change. The longest pause stayed under 1 ms.

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
