# Conformance: measuring "every C# feature works"

csharpc's goal is to compile *any* C# program with .NET-identical behaviour. The differential
samples in `tests/samples/` prove that what we support is correct; the **conformance corpus**
measures how much of the language and everyday BCL surface we support at all, and turns the gap
into a ranked backlog.

## The corpus

`tests/conformance/<area>/<NNN_name>.cs` — 195 small programs, one feature each, across 20 areas:

| area | what it covers |
|---|---|
| lexical | integer/real/char literals, digit separators, verbatim, raw and interpolated-raw strings, UTF-8 literals, preprocessor |
| types | every primitive, `decimal`, `nint`, enums incl. `[Flags]`, `Nullable<T>`, 1-D/multi-dim/jagged arrays, tuples, records (class/struct), structs, `ref struct`/`Span<T>`, boxing |
| classes | fields, constructors and chaining, inheritance, virtual/abstract/sealed/`new`, static classes, init order, nested/partial types, finalizers, object initializers, primary constructors |
| members | properties (`init`, `required`, `field` keyword), indexers, static ctors, operators, conversions, extension methods and C# 14 extension members, partial methods/properties, `ref`/`out`/`in`, `params` arrays and spans, overload resolution, ref returns, generic math |
| interfaces | dispatch, multiple/explicit implementation, default methods, inheritance, static abstract members, `IComparable<T>`, `IDisposable`/`using`, `IEnumerable<T>` |
| generics | generic classes/methods/structs, constraints, nested instantiations, variance, generic interfaces, per-instantiation statics |
| delegates, lambdas, localfunctions | `Func`/`Action`, custom delegates, multicast, events, method groups, closures and loop-capture semantics, higher-order functions, static/natural-type lambdas, capturing and generic local functions |
| statements | all loops, labels/`goto`, `switch` (patterns, `goto case`, strings), `checked`, `lock` (incl. `System.Threading.Lock`), `unsafe`/pointers/`stackalloc`/`fixed`, using declarations, top-level `args`/`return` |
| expressions | precedence, `++`/`--`, `?.`, `??`/`??=`, conditional (incl. `ref`), numeric conversions, `nameof`/`typeof`/`sizeof`, `default`, ranges/indices, collection expressions with spreads, collection initializers, target-typed `new`, throw expressions, anonymous types/`with`, interpolation, lifted operators |
| patterns | type, relational/logical, property, positional, list, tuple patterns, `is not null`, `when` guards |
| exceptions | try/catch/finally, custom exceptions, filters, rethrow, inner exceptions, runtime-raised exceptions, unhandled (exit 134) |
| iterators, async | `yield return`/`yield break`, lazy pipelines, iterator `finally`; `async Main`, `Task.WhenAll`, `ValueTask`, async exceptions, `IAsyncEnumerable` |
| linq | method and query syntax, grouping/ordering, joins, `ToDictionary`, `Aggregate`/`Distinct`/`Zip`/`Chunk`, `SelectMany`/`let` |
| formatting | standard and custom numeric format strings, composite formatting, alignment, round-trip `double` formatting |
| collections | `List`, `Dictionary`, `HashSet`, `Queue`/`Stack`, `SortedDictionary`/`SortedSet`/`LinkedList`, `PriorityQueue`, `Array` helpers |
| bcl | `StringBuilder`, string methods, `Math`, `Span`/`Memory`, `Console` APIs, `Environment`, `DateTime`/`TimeSpan`, `GC`, parsing/`Convert`, `BitOperations`, `BigInteger`, `Tuple` |
| programs | small realistic programs mixing features (sorting, linked lists, a bank simulation with `decimal`, struct matrices, LINQ word counts, an enum state machine) |

Every program is deterministic (no randomness, wall-clock time, hash codes or culture-sensitive
output) and checks itself by printing results; the oracle is **real .NET 11**:
`tests/conformance.sh --regen` runs each file with `dotnet run file.cs` and records stdout
(build warnings filtered) plus the exit code when non-zero.

## Running it

```sh
tests/conformance.sh                # build csharpc (Release), compile + run all, compare, summarize
tests/conformance.sh -j 8 linq      # parallelism, area/path filter
tests/conformance.sh --opt -O0      # other optimization level
tests/conformance.sh -v generics    # show diffs/diagnostics of failures
tests/conformance.sh --regen        # re-record expectations with .NET
```

A program **passes** only if csharpc compiles it, and the native binary's stdout and exit code
are byte-identical to .NET's. Results go to `tests/conformance/results.json` (per program:
status, failure reasons, compile time) and `tests/conformance/results.md`.

Failure reasons are extracted from csharpc's diagnostics (`CSLLVM0001: not supported by csharpc
yet: X`, `CSLLVM0002: 'M' has no implementation in the csharpc runtime yet`), crashes, and
output mismatches, then normalized (user symbol names become `*`, generic arguments collapse to
`List<>`) and counted by the number of programs they block. A program can count under several
reasons. Note that csharpc reports only the first unsupported construct per method, so fixing a
reason can uncover further ones in the same program; treat the ranking as a guide, not an exact
forecast.

## Current results

Run on 2026-10-22 on branch `bclvalues` with main merged (structs, generics, interfaces, exceptions,
async/iterators, spans/pointers, records, perf/LTO, BCL value types), `-O2`:

**159 / 195 programs pass — 81.5% coverage** (merged main + BCL value types, 2026-10-22; history: 41.0% → 51.3% → 65.1% → 81.5%).
Of the 36 failures, 34 are compile-time "not supported" diagnostics (mostly `List<T>`/collections and LINQ,
which await the C# corelib), one is an internal compiler error (static abstract interface members) and one
is a runtime difference (exception propagation out of a `ValueTask`).

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 3 | 4 | 75.0% |
| bcl | 11 | 12 | 91.7% |
| classes | 15 | 15 | 100.0% |
| collections | 1 | 8 | 12.5% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 14 | 20 | 70.0% |
| formatting | 5 | 5 | 100.0% |
| generics | 5 | 8 | 62.5% |
| interfaces | 8 | 9 | 88.9% |
| iterators | 3 | 4 | 75.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 10 | 10 | 100.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 17 | 18 | 94.4% |
| patterns | 6 | 8 | 75.0% |
| programs | 4 | 6 | 66.7% |
| statements | 12 | 12 | 100.0% |
| types | 21 | 25 | 84.0% |
| **total** | **159** | **195** | **81.5%** |

Progression during the session: 29.2% at `4d91990` (before lambdas/structs), 36.9% after the
lambdas/closures/delegates and numeric-formatting merges, 41.0% after structs/`Nullable<T>`/tuples.

## Top failure reasons (the prioritized roadmap)

From `tests/conformance/results.md` of the latest run:

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 2 | 2 | creating instances of 'System.Collections.Generic.Dictionary<,>' | collections/002_dictionary, programs/003_bank_simulation |
| 3 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 4 | 1 | runtime abort: Unhandled exception. System.InvalidOperationException: async boom | async/003_valuetask_exceptions |
| 5 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 6 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 7 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 8 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 9 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 10 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 11 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 12 | 1 | generic struct '*' | generics/004_generic_struct |
| 13 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 14 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 15 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 16 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 17 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 18 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 19 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 20 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 21 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 22 | 1 | missing library member: System.Linq.Enumerable.Distinct | linq/005_aggregate_distinct_zip |
| 23 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 24 | 1 | missing library member: System.Linq.Enumerable.SelectMany | linq/006_selectmany_let |
| 25 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 26 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 27 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 28 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 29 | 1 | missing library member: System.Collections.Generic.Dictionary<,>.this[] | programs/003_bank_simulation |
| 30 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 31 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 32 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 33 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 34 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 35 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 36 | 1 | missing library member: string.Remove | types/022_string_basics |

### Reading the ranking

- **Interfaces** (#1, #5) and **exceptions** (#2) dominate: interface dispatch blocks nearly every
  program that touches BCL interfaces (`IComparable<T>`, `IDisposable`, `IEnumerable<T>`), records
  (which implement `IEquatable<T>`), and LINQ. Both are in flight on the `generics` and
  `exceptions` branches.
- **Generics** (generic methods/local functions/types/type parameters) are next, and they gate the
  whole collections/LINQ tier together with a **self-hosted corelib**: `List<T>`, `Dictionary<K,V>`,
  `HashSet<T>`, `Queue<T>` etc. (`creating instances of 'System.Collections.Generic.*'`) should come
  from C# implementations compiled by csharpc rather than per-member intrinsics.
- **Iterators and async** (`operation YieldReturn`, `async method`) need csharpc's own
  state-machine lowering — Roslyn's public IOperation/CFG API does not lower them.
- The long tail of `missing library member: ...` (string/StringBuilder/Math/Convert/BitOperations/
  reflection `Type.Name`) is cheap individually; most should move into the C# corelib too.
- `internal compiler error: invalid LLVM IR` for partial methods and partial properties is a plain
  bug (the declaring part of a partial member is emitted as an internal declaration without a body).

Re-run the suite after every merge and update this page (and the roadmap matrix in
`06-roadmap.md`) from `tests/conformance/results.md`.
