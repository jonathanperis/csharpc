# ADR 0025 — The compiler catches up before CoreLib flips resume; NativeAOT is the yardstick

- **Status:** Accepted (2026-11-10): the owner asked for this direction after the review of the first flips. Issue [#310](https://github.com/jonathanperis/csharpc/issues/310), child of
  the policy issue [#187](https://github.com/jonathanperis/csharpc/issues/187).
- **Context:** [ADR 0016](0016-nativeaot-parity-and-end-state.md), [ADR 0023](0023-framework-il-first.md),
  [ADR 0024](0024-runtime-boundary.md), [03-lowering.md "Generics"](../03-lowering.md#generics-monomorphization),
  [21-ownership.md](../21-ownership.md), [05-testing.md](../05-testing.md).

## Where the direction stands

ADR 0023 moved csharpc to "framework IL first": csharpc owns the compiler, the C runtime and a thin contract layer, and every
other library comes from the pinned .NET framework IL, flipped one library per PR with the shadow deleted. The direction is
working. Upstream code gives .NET's behaviour by construction, where the hand-written shadows were the main source of silent
differences, and every flip so far forced a *general* compiler fix (comparer devirtualization, `box`/`isinst` folding,
feature-switch folding, `Vector128` on LLVM vectors, facades that define types) rather than more library code. By
2026-11-10 the List, Dictionary/HashSet, Linq, ObjectModel/NonGeneric and Collections/Concurrent flips are merged, and JSON,
ComponentModel, StringBuilder, System.Memory, Random and the managed synchronization types are in flight.

## The problem the flips exposed

Upstream IL is written for NativeAOT's compiler (ILC). ILC shares one body of generic code across reference-type
instantiations (canonical forms with a generic dictionary), analyses the whole program to devirtualize and to drop unreachable
code, honours `AggressiveInlining` / `NoInlining`, and folds feature switches. csharpc does not do the first at all and does
the others only in places: it compiles every generic instantiation separately, with no shared code
([03-lowering.md](../03-lowering.md#generics-monomorphization)). A shadow could be written around those gaps; upstream code
cannot. Each flip therefore paid for the missing optimizer:

| Flip | Cost measured against the old shadow |
|---|---|
| `List<T>` | `Add` +33 % instructions (`AddWithResize` is `NoInlining`, so `_size`/`_version` stay in memory) |
| `StringBuilder` | `Append(string)` +99 %, interpolated append +75 % (short copies go through a libc `memmove` call) |
| System.Text.Json | reflection serializer 18x binary size, about 12x compile CPU |
| Linq | speed-optimized iterators instantiated per element type: the `-O0` self-compile exhausted the 16 GB CI runner (#290) |

The A/B in ADR 0023 compares the IL route with our old shadows. That is the wrong baseline: the project's promise
(ADR 0016) is drop-in parity with NativeAOT *and* being faster and smaller than NativeAOT. Nothing measured recently says
whether the flipped libraries still beat NativeAOT.

## Decision

1. **Phase 1: finish what is in flight.** JSON, ComponentModel, StringBuilder, System.Memory, Random, the managed
   synchronization types, the SR-literal size fix, Net.Primitives, and the general `memmove` lowering. No new large flip
   starts.
2. **Phase 2: a compiler milestone before any further large CoreLib flip** (culture, number formatting, DateTime, calendars,
   text and encodings: about 50 000 lines that would all pay the same costs). In order of measured impact:
   - shared generic code for reference-type instantiations (canonical forms, as NativeAOT does), keeping per-instantiation
     code for value types;
   - whole-program devirtualization and sealed-type analysis;
   - honouring `AggressiveInlining` / `NoInlining`, and inlining small framework methods (`List.Add`, `StringBuilder.Append`,
     the comparers);
   - feature switches and dead-branch folding wherever ILC folds them;
   - compile-time and peak-memory budgets for the self-compile, so Bootstrap stays well inside the CI runner.
   Each item is its own issue and PR, with before/after numbers on the benchmark suite and on the flipped libraries (List,
   Dictionary, Linq, StringBuilder, JSON), against NativeAOT on the same machine. The milestone is done when every flipped
   library is at least as fast as its old shadow *and* as NativeAOT, and the self-compile has clear memory headroom.
3. **Phase 3: resume the flips, largest first,** with the same rules as before (shadow deleted in the same PR, security
   review per flip, numbers recorded).
4. **NativeAOT is the yardstick.** Every ADR 0023 row gains a NativeAOT column (size, compile time, speed) next to the shadow
   and IL numbers, and the merged flips are re-measured. Any case where csharpc loses to NativeAOT on speed goes to the owner.
5. **Linq follows .NET's own default for each kind of program** (corrected after #290's gate). A first version of this
   decision chose speed-optimized Linq for every program, but `UseSizeOptimizedLinq` is observable
   (`Enumerable.TryGetNonEnumeratedCount` answers differently, and `AppContext` exposes the switch), so it changed output
   (samples 462 and 345), and byte-identical behaviour outranks speed. csharpc therefore sets exactly what .NET sets: a
   single file gets the file-based app's default (size-optimized), a project with `PublishAot=true` gets `true` (NativeAOT's
   targets), a project without it gets .NET's default (`false`), and an explicit property, runtimeconfig entry or
   `#:property` always wins. For information, on the Linq throughput program speed-optimized is 0.79 s and 1 369 KB,
   size-optimized 1.80 s and 969 KB; closing that gap belongs to the compiler milestone, not to a different default.
   csharpc's own self-compile sets size-optimized explicitly to fit the CI runner.
6. **Process:** the shared files that every flip edits (`tests/size/budgets.txt`, the ADR 0023 table, `ownership.tsv`) are
   split per library, so parallel PRs stop conflicting and re-running hours of CI. Done (#333): `tests/size/budgets/<program>.txt`,
   `docs/il-first/<library>.md` (`tools/il-first/table.sh` prints the whole table), `src/Csharpc/upstream-types/<library>.txt`, and
   `ownership.tsv` generated on demand and no longer committed; docs/12-corelib.md says where a flip writes. The Bootstrap PR checks ("Roslyn hosted
   from IL", "csharpc compiles csharpc") are part of the merge bar for `src/**` changes (#303).

## Open question for the owner

Reflection-based `JsonSerializer` is disabled by default under NativeAOT, which requires source-generated contexts. csharpc
currently supports it through the rooting rule (#266), at about 18x binary size on the programs that use it. Decide when
Phase 2 starts: keep it (beyond NativeAOT's default) or match NativeAOT's default and make it an explicit opt-in.

## Consequences

- The line count of the corelib falls more slowly for a while; the compiler grows instead. That is consistent with the
  boundary (ADR 0024 A): the optimizer is exactly the part csharpc owns.
- Shared generics change the code generator's core (method tables, generic dictionaries, the GC's view of generic frames).
  It needs its own design note before the first PR, and Bootstrap plus the native suite on every step.
- Once Phase 2 lands, each later flip should cost little or nothing, and the merged flips get faster without further work.
