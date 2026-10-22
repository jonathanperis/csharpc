# ADR 0004 — Memory management

- **Status:** Accepted. Phase 1 (no GC) is implemented; phase 2 is in progress.

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
