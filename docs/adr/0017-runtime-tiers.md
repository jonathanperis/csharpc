# ADR 0017 — The runtime in three tiers: C# everywhere above a minimal native floor

- **Status:** Accepted (2026-10-24). Extends [ADR 0008](0008-runtime-in-csharp.md); fits the end state of
  [ADR 0016](0016-nativeaot-parity-and-end-state.md). Roadmap: Runtime tiers 1 and 2.

## Context

csrt (`src/runtime`) is ~7.7k lines of csharpc's own C plus .NET's ICU shim (~6.5k lines, copied from
dotnet/runtime); the C# corelib is ~99k lines. ADR 0008 moved the library half to C#. The question is
how far down C# can go, and what has to stay native.

How .NET does it: CoreCLR's VM, JIT and GC are C++. NativeAOT — csharpc's comparison point — keeps C++
for the GC (`gc.cpp`, shared with CoreCLR), stack walking/unwinding and thread suspension/hijacking,
with a little assembly for allocation fast paths and GC transition stubs, and moves the rest into C#
compiled by ILCompiler itself: exception dispatch logic, casting, interface dispatch caches, startup,
generic lookups (System.Private.CoreLib calling down through `RuntimeImports`). OS access goes through
small C shims (`libSystem.Native`, `libSystem.Globalization.Native`). Other managed runtimes go
further: Go's runtime and GC are written in Go under restricted rules (`//go:nosplit`, no allocation);
Jikes RVM and MMTk write VMs and collectors in Java.

## Decision

The runtime is organised in three tiers:

1. **Tier 1 — ordinary C#, move now.** Everything that runs as normal managed code: the task scheduler,
   thread pool and timers (`async.c`), type and interface helpers (`type.c`, parts of `csrt.c`),
   delegates, and the remaining library helpers (`bclmisc.c`, `datetime.c`, `parse.c`, `strings.c`,
   `span.c`, `records.c`, `decimal.c`, `bigint.c`). They move to the corelib following ADR 0008,
   behaviour identical, sizes within budgets, benchmarks not slower.
2. **Tier 2 — "systems C#", after M6.** The garbage collector, the exception personality/dispatch logic
   and thread management, written in a restricted C# dialect that csharpc enforces at compile time: an
   attribute (e.g. `[RuntimeCritical]`) marks methods that must not allocate on the managed heap, reach
   a GC safepoint, throw, or call anything not equally marked; raw memory through `unsafe` code and
   intrinsics. Developed behind a flag next to the C implementation, compared on the benchmarks, the
   GC stress run and dotnet's own AOT test suites (ADR 0016's acceptance gate), and made the default
   only when it is at least as fast and as correct. A collector compiled by csharpc is the strongest
   self-hosting evidence after csharpc compiling itself.
3. **Tier 3 — the native floor, permanent and minimal.** Process entry glue, signal/SEH handler stubs,
   the unwinder interface (the personality-routine ABI to libunwind/SEH; on wasm32 the explicit
   propagation of ADR 0011), register spilling for stack scanning and any assembly stubs: a few hundred
   lines. OS shims follow .NET: reuse dotnet's own C shims (`System.Native`, `System.Globalization.Native`
   — the latter already reused) rather than maintaining csharpc-specific ones, so OS behaviour is .NET's
   by construction.

## Consequences

- New runtime code goes in the highest tier that can hold it; adding C to tiers 1–2 needs a reason in
  the commit (performance measured, or a tier-2 prerequisite missing).
- Tier 2 needs compiler support first: the `[RuntimeCritical]` checks (no allocation, no safepoint, no
  throw, call-graph closure), and lowering guarantees for that code (no hidden allocations from boxing,
  closures, string interpolation or bounds-check failures that throw).
- The shared runtime ABI (object/array/string layout, method tables) keeps a single definition; as code
  moves to C#, `csrt.h` shrinks to what tier 3 and the remaining C need, mirrored in `TypeMapper`.
- In the dotnet fork (M7), csharpc owns its runtime the way NativeAOT owns its own, and the reused
  .NET shims keep OS behaviour aligned with NativeAOT's.
