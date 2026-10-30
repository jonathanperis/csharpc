# ADR 0004 — Memory management

- **Status:** Accepted. Phase 2 is the collector in `src/runtime/gc.c`: conservative, non-moving, stop-the-world mark-sweep, with thread-local allocation and precise skipping of strings and primitive arrays. The "M3+" statepoint step in the original decision is not the next increment; the revision below replaces it.

## Context

C# is garbage-collected. A GC needs (a) to find **roots** (stack slots, registers and globals holding
references), (b) to trace the **heap** (which object fields are references), and optionally (c) to
**move** objects (compaction, generations), which requires updating every root precisely.

LLVM doesn't track GC references by default. In optimized code, references live in registers and
spill slots that only LLVM knows about. The options for finding them:

| Approach | Roots | Moving? | Cost | Used by |
|---|---|---|---|---|
| No GC | — | — | none, memory grows | early spikes |
| Conservative stack scan (Boehm-style) | scan stack, registers, data segment for anything that looks like a heap pointer | no | small; false retention | Mono (stack), IL2CPP (Boehm), many language runtimes |
| Shadow stack | compiler spills each live reference to an explicit frame linked list | possible | loads/stores on every safepoint | NativeAOT-LLVM (wasm), LLVM `shadow-stack` GC strategy |
| Statepoints + stack maps | LLVM's `gc.statepoint`/`gc.relocate`, `.llvm_stackmaps` section | **yes** | best codegen; complex | gollvm-style precise GC, Azul Falcon JIT |

## Decision

A staged approach:

1. **Spike (now):** `csrt_alloc` is `calloc`; nothing is ever freed. This is acceptable for
   short-lived programs and makes the object model easy to debug.
2. **Next (M1): conservative, non-moving mark-sweep in csrt.**
   - The allocator keeps metadata so an arbitrary word can be tested for being a pointer into
     (or to the start of) a live object, e.g. size-classed pages plus a large-object list.
   - Roots: the current thread's native stack between `main`'s frame and the current `sp`,
     registers via `setjmp` (or `__builtin_unwind_init`), and the module's data segment (static
     fields are plain globals).
   - Heap tracing: conservative first. Then **precise** using per-type pointer maps that csharpc
     emits next to each method table (it knows every layout). Arrays of primitives aren't scanned.
     String contents are never scanned.
   - Collection is triggered by allocation volume. Explicit `csrt_gc_collect` for tests.
3. **Later (M3+): precise, potentially moving GC** using LLVM statepoints (`gc "statepoint-example"`
   or a custom strategy) and stack maps, rewritten with `RewriteStatepointsForGC`. On wasm32, a
   shadow stack, since wasm can't scan its own stack.

Objects stay **non-moving** until statepoints land, which also keeps interop (pinning) trivial.

## Consequences

- Phase 2 needs no compiler changes beyond optional pointer maps. The IR stays clean and LLVM
  optimizes freely. False retention is possible but rarely matters in practice (IL2CPP shipped on
  Boehm for years).
- A conservative stack scan is compatible with every LLVM optimization, because it doesn't care
  where LLVM keeps values. It does assume references aren't hidden (no pointer compression or
  tagging, no derived pointers to *outside* an object). Interior pointers (array element addresses,
  `ref` locals) must be recognized as keeping the object alive.
- A future moving GC requires statepoints, which affect codegen quality and compile time and need
  the C++ API (or a shim) for some pieces.

## Alternatives considered

- **Link Boehm GC (`libgc`).** Proven and fast to integrate. But it's an external dependency
  (headers weren't even installed on the dev machine), knows nothing of our layouts, and is hard to
  evolve toward precision or generations. It remains a reasonable fallback.
- **Reference counting** (Swift/ARC style). It doesn't match C# semantics (cycles are common), and the
  retain/release traffic is expensive. Rejected.
- **Shadow stack from day one.** Simple and precise, but it taxes every function, and wasm is the only
  target that strictly needs it. Kept for wasm32.

## Revision (2026-10-25): measure, then generations, not statepoints

M3 is done and the collector is still the conservative mark-sweep from phase 2. Under TechEmpower
load, csharpc's RSS is higher than NativeAOT's (73.3 vs 64.9 MiB after load on run 40214226895).
Part of that gap is the binary (15.7 vs 8.9 MiB). The rest, and the pause behaviour on a large
pointer-dense heap, is what this revision is about.

### Measurement

`benchmarks/gc/gclatency.cs` (harness `benchmarks/gc/gc.sh`, also a Benchmarks workflow step) is a
fixed 4-thread program. Stdout is one checksum recorded from `dotnet run`. Stderr is per-phase
timing. The phases are a Fortunes-shaped churn (short-lived objects plus a 1024-slot window),
building an 800,000-node pointer ring, and mutating that ring while allocating short-lived lists.

Median of 3, published binaries, 4-vCPU VM (`benchmarks/gc/gc.sh`, csharpc compile 2.3 s,
stripped binary 195 KiB, NativeAOT 1.33 MiB):

| engine | phase | wall | ops/s | pause | collections | max op | heap | RSS |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| csharpc | churn | 121 ms | 16.5M | 2.73 ms | 14 | 0.5 ms | 40 MiB | 205 MiB |
| csharpc | livemutate | 112 ms | 4.29M | 30.3 ms | 2 | 18.2 ms | 99 MiB | 205 MiB |
| JIT | churn | 82 ms | 24.4M | 1.68 ms | 3 | 6.6 ms | 89 MiB | 258 MiB |
| JIT | livemutate | 161 ms | 2.98M | 118.8 ms | 1 | 119 ms | 164 MiB | 258 MiB |
| NativeAOT | churn | 96 ms | 20.8M | 2.08 ms | 3 | 1.4 ms | 89 MiB | 235 MiB |
| NativeAOT | livemutate | 158 ms | 3.04M | 117.4 ms | 1 | 118 ms | 164 MiB | 235 MiB |

`livebuild` did not collect on csharpc (14 ms) and collected once on the JIT (21 ms, 2.0 ms pause)
and NativeAOT (19 ms, 1.3 ms). On a quiet single run, `CSRT_GC_STATS=1` reported 18 collections,
**mark 46.6 ms, sweep 1.4 ms** (mark is 97 % of stop-the-world time), pause p50 0.22 ms, p99 15.4 ms,
max 18.6 ms. Sweep is `allocated &= marked`.

csharpc's non-moving collector is ahead on the large heap (pause 30 ms vs ~118 ms, throughput
4.29M vs ~3.0M ops/s, RSS 205 vs 235 MiB). It is behind on the short-lived churn (16.5M vs 20.8M
ops/s NativeAOT and 24.4M JIT). That gap is the mutator, not the pause (2.7 ms vs 2.1 ms): the
trigger stays at 32 MiB while the live set is a 1024-slot window, so csharpc collects 14 times to
their 3. The mark still walks every live pointer, on one thread, which is the pause that will not
scale.

Hello world stays 35056 bytes with these runtime changes (same file size as `main`). The parallel
marker below is not in that binary.

### Parallel marking lost on this machine

Two markers were timed against the serial one. Both kept the same reachability (the benchmark
checksum matched, and sample 21 matched `.expected`).

1. **Atomic `fetch_or` on the mark bitmap, work-stealing grey stack.** Idle helpers that spun on
   the mark lock made sample 21 about 30× slower (pause p50 46 ms, several seconds of mark).
   Sleeping when idle removed that. What remained was still slower than one thread: sample 21
   forced-parallel mark 158 ms vs serial ~75 ms (p50 pause 2.58 vs 1.6 ms), and `livemutate`
   pause ~55 ms vs ~31 ms. A mark bit word covers 64 objects in a chunk, so every claim bounced
   the same cache lines. A shared in-flight counter did the same and was not used.
2. **One owner per chunk, plain stores, batched handoff.** Only the owner writes the chunk's mark
   bits (the same OR as the serial marker). A pointer into another chunk is copied, 64 at a time,
   to that owner's inbox. Large reference arrays are sliced across workers. Termination counts
   idle workers under the same lock that publishes an inbox. On the 4-vCPU VM this was still
   slower: mark 53–74 ms vs ~47 ms serial, and `livemutate` did not get faster. The serial scan
   already moves about 1.5–2 GB/s. Copying each object to its owner costs more than scanning it
   when the object is a couple of pointers.

The ownership marker stays in `gc.c` behind `-DCSRT_GC_PARALLEL_MARK`
(`CSHARPC_RUNTIME_CFLAGS=-DCSRT_GC_PARALLEL_MARK` at compile time, then `CSRT_GC_PARALLEL=1`).
It is about 8 KiB of text plus the worker stacks' buffers, which pushed `tests/size` hello from
35056 to 43256 bytes, over the 32768 budget. The default runtime does not compile it in.
`CSRT_GC_PARALLEL` without that define does nothing.

Replacing the size-class division with a multiplicative inverse (`(n * ceil(2^32 / size)) >> 32`,
checked for every byte offset in a chunk) was inside the noise on this host. It stays: it is exact,
and the division was on the hot path.

### What is not next

- **LLVM `gc.statepoint` / `RewriteStatepointsForGC`.** That pass is C++ (ADR 0002 is the C API).
  It is the way to *move* objects and patch roots. Nothing above needs to move an object, and the
  codegen and compile-time cost is unmeasured. Paying it before a moving collector wins a
  benchmark is the wrong order.
- **Precise per-type pointer maps as the pause win.** Strings and primitive arrays are already
  `CSRT_MT_NO_REFERENCES` and are not scanned. The retained graph here is reference arrays and
  cells. Skipping an integer id does not remove the scan. Maps are still required before a
  compacting collector, and they cut false retention in mixed objects (doubles, large integers).
  They are not the first lever on this workload.
- **Thread-local allocation.** Already there: one claimed bitmap word per size class per thread,
  lock-free fast path (`docs/14-threading.md`).
- **A page-protection write barrier.** `livemutate` stores into scattered old cells. A fault per
  newly-dirtied page would cost more than the collection it is trying to shrink.

### Increment shipped (opt-in): non-moving nursery + compiler card table

> **Unsound, gated (2026-10-30).** A review found reference stores without a barrier: LLVM's coroutine
> pass spills locals into async/iterator frames (heap objects) after the barrier hook runs; reference
> `Span<T>.CopyTo` lowers to a memmove; the C runtime's `Array.Resize`/`Array.Fill`, task continuations
> and exception fields store directly; `Array.Copy` dirties only pointer-sized elements, not structs
> holding references. A young object reachable only through such a store is freed by a minor
> collection. Large objects and small objects in reused old chunks also never count toward the minor
> trigger. `--gc-nursery` failed unless `CSHARPC_EXPERIMENTAL_GC_NURSERY=1` until every store path
> had a barrier and a CI workflow ran the suites with it; see "Soundness" below. The gate is gone; the
> flag stays opt-in.

Implemented on `ci/gc-nursery` and kept **off by default**. Enable with `--gc-nursery` or
`CSHARPC_GC_NURSERY=1` (runtime compiled with `-DCSRT_GC_NURSERY_DEFAULT`; override at run time with
`CSRT_GC_NURSERY=0`). Without that flag the default binary is the same single-generation collector
as before: nursery/card code is `#if`'d out so size budgets stay green.

Design (measured, not the ADR sketch as written):

1. **Young chunks, not a contiguous nursery.** Small objects still live in 256 KiB size-classed
   chunks. Fresh chunks are young. A minor collection marks only young objects (from stacks,
   statics, and dirty old regions), sweeps only young chunks, then **promotes survivors in place**
   by clearing the chunk's young bit. Promoted chunks are not allocated into again until a major
   frees them. Large objects are born old. After 32 minors, on `GC.Collect`, or once the heap
   reaches twice the live size of the last major, a major runs today's full mark-sweep.
2. **Per-chunk / per-large dirty bits, not a flat card table.** A table over `[heap_min, heap_max)`
   under ASLR is sparse and tried to reserve gigabytes; that OOM'd. Dirty is one byte on the chunk
   header (and a single `large_dirty` flag for the large-object list). Coarse: a minor that has
   dirtied many old chunks still walks a lot of old memory.
3. **Compiler write barrier.** With `--gc-nursery`, every heap reference store goes through
   `csrt_gc_write` (dirty, then store — dirty first so a stop-the-world suspend cannot hide an
   old→young edge). Locals and statics are roots and skip the barrier. The barrier is one pass
   over the finished module (`ModuleCodeGen.WriteBarriers.cs`), not a hook in one builder, so it
   covers every function csharpc emits (source, IL front-end, corelib, thunks) and wasm32-wasi
   (wasm keeps the shadow stack; ADR 0011). There is no `mprotect` barrier.

#### Soundness (2026-10-30)

A review found heap reference stores without a barrier; a young object reachable only through such
a store was freed by a minor collection (wrong output or a crash). Every store path now has one:

| store path | covered by |
|---|---|
| pointer stores from any builder | the module pass: `store ptr` into a non-root address becomes `csrt_gc_write` |
| aggregate stores (structs, `KeyValuePair`, `Nullable<T>` holding references) | the pass: `csrt_gc_cards_dirty(address, size)` before the store |
| `llvm.memcpy` / `llvm.memmove` (`Span<T>.CopyTo`, struct copies, `Unsafe.CopyBlock`) | the pass: the destination range is dirtied first |
| atomic / volatile pointer stores, `cmpxchg`, `atomicrmw` | the pass (and `Interlocked` on references, lowered as integers, dirties explicitly) |
| coroutine frames (async methods, iterators): LLVM's CoroSplit spills locals into the heap frame after the pass ran | *remembered* frames: `csrt_coro_alloc[_task]` calls `csrt_gc_remember`, and every minor scans the frames (task / iterator-frame objects) in remembered chunks and large objects, until none is left there |
| the C runtime (`Array.Resize/Fill/Copy` of any element size, task continuations, results and exceptions, WhenAll/WhenAny, timers, CTS callbacks, the pool queue, delegates' invocation lists, exception fields, threads, split, args) | `CSRT_STORE_REF` / `CSRT_DIRTY` (csrt.h): `csrt_gc_write` / `csrt_gc_cards_dirty` in a nursery build, plain stores otherwise |
| objects born old: allocated in a reused old chunk, in a thread's allocation word whose chunk a collection promoted, or large | the chunk is dirtied when the word is claimed and after every collection for the threads' current words; a large allocation sets `large_dirty`. This covers the runtime's unbarriered first stores into an object it just allocated |

Collector fixes found the same way: `protect_caches` set mark bits in old chunks during a minor, which
no sweep cleared, so the next major took those slots as already traced and freed what they
referenced; a minor no longer protects old chunks. Mutators look chunks up without the lock (in the
barrier) while another thread may grow the chunk set: tables carry their capacity and are never
freed. Reformatting an empty chunk for another size class clears it, because minors scan whole dirty
chunks and a stale pointer into a claimed but unused slot made the collector read a field as a method
table. Stress collections (`CSRT_GC_STRESS`) are minors, with a major every 32, so they exercise the
barriers. Allocation accounting: every byte counts toward the minor trigger (young chunks, reused old
chunks, large objects); before, a program that allocated only large arrays never collected.

Coverage: samples 650–652 fail with the earlier nursery (a frame-held object freed, `Task.WhenAll`
results lost, 66 wrong elements after span/array copies, no collection under large-only allocation)
and pass now. The `GC nursery` workflow (`.github/workflows/nursery.yml`, Linux x86-64, not part of
the merge bar) runs the differential suite at -O0/-O2 and the conformance corpus with
`CSHARPC_GC_NURSERY=1 CSRT_GC_NURSERY_MB=1` (a minor every MiB), and the collection-heavy samples
under `CSRT_GC_STRESS=50`. The default build is unchanged: every barrier is behind
`CSRT_GC_NURSERY_DEFAULT` or `--gc-nursery`, and the size programs and `-O0` binaries compile
byte-identical to the build before.

Not covered: a reference written into the heap as an integer (`nint` arithmetic, `Unsafe.As` to an
integer type) by code other than `Interlocked` dirties nothing, so a minor can miss it (a major still
finds it conservatively). Neither .NET code nor the corelib keeps object references that way.

Local 4-vCPU median (same harness as above; NativeAOT numbers from the pre-nursery baseline on this
host where the AOT publish was unavailable in later runs):

| mode | phase | ops/s | pause | collections | heap | RSS |
|---|---|---:|---:|---:|---:|---:|
| default (off) | churn | 18.7M | 3.08 ms | 14 | 40 MiB | 205 MiB |
| default (off) | livemutate | 4.00M | 36.0 ms | 2 | 99 MiB | 205 MiB |
| `--gc-nursery` | churn | 11.7M | 3.99 ms | 14 | 29 MiB | 135 MiB |
| `--gc-nursery` | livemutate | 3.53M | 53.3 ms | 7 | 110 MiB | 135 MiB |
| NativeAOT (baseline) | churn | 20.8M | 2.08 ms | 3 | 89 MiB | 235 MiB |
| NativeAOT (baseline) | livemutate | 3.04M | 117.4 ms | 1 | 164 MiB | 235 MiB |

RSS on livemutate drops (~205 → ~135 MiB) because minors reclaim short-lived cells instead of
waiting for one full collection. That does **not** pay for the rest: churn throughput falls ~37 %
(barrier + minors that still scan coarse dirty old chunks once the live set has promoted), and
livemutate pause rises (~36 → ~53 ms) because several minors plus a major cost more wall time than
two full collections on this pointer-dense ring. A sticky-survivor variant (keep young bit, only
clear cards) mis-collected after cards cleared (checksum wrong); promotion was required.

Size: default hello **28520** / async **147200** (budgets 32768 / 147456). With barriers linked,
async jumped to ~167 KiB (over by ~20 KiB) — that alone forbids making nursery the default.

So the nursery is behind the flag, with the negative result recorded here. Next collector work that
could make it win: a denser remembered set (or remembered slots) so minors do not re-walk whole
dirty chunks; a smaller nursery trigger only after that; then **mostly-copying** (Bartlett) with
precise heap maps. Statepoints stay unjustified until a fully precise moving collector beats that
on a benchmark with codegen cost written down next to the pause numbers.

The mark/sweep split and the pause percentiles above were timed with extra clocks in the collector.
That instrumentation, and a multiplicative inverse in place of the size-class division, added about
1 KiB of text. On CI that was enough to push `tests/size` `async` from under its 147456-byte budget
to 151376 (one 4 KiB page; run 40224424838, x86-64 and arm64). Both came back out. The default
`gc.c` machine code matches the previous collector when nursery is not compiled in;
`CSRT_GC_STATS=1` is the same one-line summary (plus ", nursery on" when enabled). The ownership
marker remains source under `-DCSRT_GC_PARALLEL_MARK` and contributes no bytes unless that flag is
passed.
