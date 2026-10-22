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

Run on 2026-10-22 against main `877db85` (structs, `Nullable<T>`, tuples, lambdas/closures/delegates,
numeric formatting and MSBuild support merged; generics, interfaces and exceptions still in flight), `-O2`:

**80 / 195 programs pass — 41.0% coverage.**
Of the failures, 115 are compile-time "not supported" diagnostics and 0 compile but produce wrong output (none at this point —
everything csharpc accepts behaves like .NET).

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 1 | 12 | 8.3% |
| classes | 14 | 15 | 93.3% |
| collections | 0 | 8 | 0.0% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 6 | 20 | 30.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 0 | 8 | 0.0% |
| interfaces | 0 | 9 | 0.0% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 3 | 4 | 75.0% |
| members | 12 | 18 | 66.7% |
| patterns | 3 | 8 | 37.5% |
| programs | 2 | 6 | 33.3% |
| statements | 7 | 12 | 58.3% |
| types | 10 | 25 | 40.0% |
| **total** | **80** | **195** | **41.0%** |

Progression during the session: 29.2% at `4d91990` (before lambdas/structs), 36.9% after the
lambdas/closures/delegates and numeric-formatting merges, 41.0% after structs/`Nullable<T>`/tuples.

## Top-30 failure reasons (the prioritized roadmap)

| # | programs blocked | reason | examples |
|---:|---:|---|---|
| 1 | 16 | interface implementation on '*' | `collections/006_list_of_objects`, `expressions/012_target_typed_new` |
| 2 | 11 | try/catch (exception handling) | `collections/002_dictionary`, `exceptions/001_try_catch` |
| 3 | 7 | interface '*' | `generics/007_generic_interface`, `interfaces/001_basic` |
| 4 | 6 | creating instances of 'System.Collections.Generic.List<>' | `collections/001_list`, `collections/006_list_of_objects` |
| 5 | 6 | generic local function '*' | `expressions/008_default_literal`, `generics/002_generic_methods` |
| 6 | 6 | generic method '*' | `expressions/008_default_literal`, `generics/002_generic_methods` |
| 7 | 5 | foreach over 'System.Collections.Generic.IEnumerable<>' (only arrays so far) | `iterators/001_yield_return`, `iterators/002_yield_break_lazy` |
| 8 | 4 | async method '*' | `async/001_async_main`, `async/002_task_whenall` |
| 9 | 4 | operation YieldReturn | `iterators/001_yield_return`, `iterators/002_yield_break_lazy` |
| 10 | 4 | missing library member: string.Join | `linq/005_aggregate_distinct_zip`, `linq/006_selectmany_let` |
| 11 | 3 | missing library member: System.Reflection.MemberInfo.Name | `classes/011_is_as_cast`, `expressions/007_nameof_typeof_sizeof` |
| 12 | 3 | missing library member: int.CompareTo | `collections/001_list`, `collections/006_list_of_objects` |
| 13 | 3 | generic type parameter '*' | `generics/001_generic_class`, `generics/004_generic_struct` |
| 14 | 2 | struct 'System.Span<>' | `bcl/004_span_memory`, `types/019_ref_struct_span` |
| 15 | 2 | ref assignment | `expressions/005_conditional`, `members/017_ref_returns_locals` |
| 16 | 2 | operation AnonymousObjectCreation | `expressions/014_with_expression_anonymous`, `linq/006_selectmany_let` |
| 17 | 2 | record struct '*' | `expressions/016_user_defined_conversions_chain`, `types/016_record_struct` |
| 18 | 2 | missing library member: double.ToString | `formatting/001_numeric_format_strings`, `formatting/003_custom_formats` |
| 19 | 2 | generic type '*' | `generics/001_generic_class`, `generics/008_static_generic_cache` |
| 20 | 2 | struct 'System.ReadOnlySpan<>' | `lexical/007_utf8_literals`, `members/015_params_span` |
| 21 | 2 | internal compiler error: invalid LLVM IR | `members/011_partial_methods`, `members/012_partial_properties` |
| 22 | 2 | pattern RecursivePattern | `patterns/006_tuple_patterns`, `programs/006_state_machine_enum` |
| 23 | 2 | System.Decimal | `programs/003_bank_simulation`, `types/003_decimal` |
| 24 | 1 | missing library member: System.Text.StringBuilder.Insert | `bcl/001_stringbuilder` |
| 25 | 1 | missing library member: string.Split | `bcl/002_string_methods` |
| 26 | 1 | missing library member: System.Math.Round | `bcl/003_math` |
| 27 | 1 | missing library member: System.IO.TextWriter.WriteLine | `bcl/005_console_apis` |
| 28 | 1 | missing library member: System.Environment.ProcessorCount | `bcl/006_environment` |
| 29 | 1 | struct 'System.DateTime' | `bcl/007_datetime_timespan` |
| 30 | 1 | missing library member: double.Parse | `bcl/009_char_parse_convert` |

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
