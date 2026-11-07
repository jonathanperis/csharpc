# 09 — Conformance: measuring "every C# feature works"

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

All **195 of 195** programs pass in CI (`tests/conformance.sh` is part of the
`test (<platform>)` gate, [05-testing.md](05-testing.md)), and the corpus stays at 100 %: a new feature or fix
that breaks one is not merged.

| area | programs |
|---|---:|
| async | 4 |
| bcl | 12 |
| classes | 15 |
| collections | 8 |
| delegates | 5 |
| exceptions | 10 |
| expressions | 20 |
| formatting | 5 |
| generics | 8 |
| interfaces | 9 |
| iterators | 4 |
| lambdas | 6 |
| lexical | 10 |
| linq | 6 |
| localfunctions | 4 |
| members | 18 |
| patterns | 8 |
| programs | 6 |
| statements | 12 |
| types | 25 |
| **total** | **195** |

## Reading a failure

When a program fails, `tests/conformance/results.md` ranks the failure reasons by the number of programs each one
blocks. Reasons are extracted from csharpc's diagnostics and normalized: user symbol names become `*` and generic
arguments collapse to `List<>`. Two things to keep in mind:

- csharpc reports only the first unsupported construct per method, so fixing a reason can uncover further ones in the
  same program: treat the ranking as a guide, not an exact forecast.
- A failure is one of three kinds: a compile-time diagnostic (`CSLLVM0001`/`CSLLVM0002`, the intended outcome for
  unsupported code), an internal compiler error (`invalid LLVM IR`: a plain bug), or a run-time difference (the worst
  category: a silent wrong output, fixed before anything else).

The second corpus, of adversarial edge cases, is [15-conformance2.md](15-conformance2.md); the larger real-world
programs and the compatibility corpus of unmodified Microsoft samples are [18-compatibility.md](18-compatibility.md).
