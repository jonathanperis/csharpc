# 15 — Conformance corpus 2 (adversarial edge cases)

The first corpus ([09-conformance.md](09-conformance.md), 195 programs) reached 100 % — but it was
written alongside the compiler, so it mostly tested what we already thought of. `tests/conformance2/`
is a second, deliberately adversarial corpus: **258 short programs** aimed at C# spec corners and
BCL edge cases where a compiler can silently get the semantics wrong.

## Methodology

- One program per edge case, grouped by spec area (`tests/conformance2/<area>/NNN_name.cs`).
- Expected stdout and exit code are recorded from real .NET 11 (`dotnet run file.cs`), exactly like
  corpus 1. Programs that did not compile under .NET were fixed (never the expectation).
- `tests/conformance2.sh` is `tests/conformance.sh` pointed at the second corpus
  (`CONF_CORPUS=conformance2`); same flags (`--regen`, `-j`, `--opt`, area filters).
- Failures are triaged by root cause; **wrong output** (silent miscompilation) is fixed first, then
  "not supported" diagnostics. Every wrong-output bug got a regression sample in `tests/samples/140_*`+.

## Results

| | programs | passing |
|---|---:|---:|
| first run (241 compiled under .NET; 17 needed fixing) | 241 | 190 (78.8 %) — **14 wrong-output** |
| after fixes | 258 | **256 (99.2 %)** |

Coverage per area after the fixes: 100 % in every area except `closures` (one library method-group
conversion) and `exceptions` (filter ordering), see below.

Areas: evalorder, overloads, conversions, interpolation, switches, structs, boxing, dispatch,
interfaces, generics, generics2, exceptions, closures, lambdas2, iterators, async, linq, records,
tuples, nullable, strings, formatting, collections, spans, operators, misc, misc2, patterns, members,
numerics, inheritance, initializers.

## Wrong-output bugs found and fixed

| Bug | Regression sample |
|---|---|
| `c[i++] += v` evaluated the indexer arguments twice (once for get, once for set) | `140_indexer_compound_eval_once` |
| Calling a mutating method on an `in` / `ref readonly` struct mutated the caller's value (no defensive copy) | `141_in_param_defensive_copy` |
| `long.MaxValue.ToString("N0")` went through `double` (lost precision); `-0.0` lost its sign in `G17`/`E` | `142_numeric_format_exact_integers` |
| Custom numeric formats ignored quoted literals, `\` escapes, `,` scaling, interleaved literals (`(###) ###-####`) | `143_custom_format_literals_scaling` |
| Negative currency used `¤-3` instead of the invariant pattern `(¤3)` | `144_currency_negative_pattern` |
| `Type.Name` of generic types printed `Int32]`; `typeof(List<string>)` dropped type arguments | `145_generic_type_names` |
| Static field initializers in interfaces never ran | `146_interface_static_initializers` |
| `x is IComparable` / `x is IEnumerable<int>` were false for boxed primitives, strings and typed arrays | `147_is_interface_on_builtins` |
| `char.IsDigit('٣')`, `char.IsUpper('Ω')` and case mapping beyond Latin-1 were wrong | `148_unicode_char_classes` |
| Runtime-raised `ArgumentOutOfRangeException`s had no `ParamName` | `149_argument_exception_paramname` |
| `"x".Replace("", "y")` threw `ArgumentNullException` instead of `ArgumentException` | `150_replace_empty_argument_exception` |
| Arrays of different tuple types shared one method table (cache keyed by `System.ValueTuple`2[]`), breaking interface casts | `151_tuple_array_method_tables` |

Other fixes (previously "not supported"): generic virtual methods (closed-world type-switch dispatch),
static abstract interface properties (`T.Zero` on user types), tuple `Equals`/`CompareTo`/`GetHashCode`
and boxing, nested tuple deconstruction, `Nullable<T>.Equals/GetHashCode`, user-defined `++`/`--`,
`sizeof(T)` in generic code, type tests against arrays/tuples, `list[^1]` on any countable type,
`StringBuilder.Append($"…")` (the interpolated-string handler is represented by the builder itself),
generic variance through builtin types, interface dispatch on boxed primitives (`IComparable`,
`IComparable<T>`, `IEquatable<T>`, `IFormattable`), `Delegate.GetInvocationList`,
`Environment.ExitCode`, `StringComparer`, `MemoryExtensions.Min/Max` (what .NET 11 binds for
`int[].Max()`), and a long tail of string/char/Math/BitConverter/generic-math members. Span lowering
is now substitution-aware (spans of `T` inside generic methods).

## Remaining failures

| Program | Root cause | Plan |
|---|---|---|
| `closures/009_method_group_conversions` | A method group converted to a delegate that targets a *library* method implemented as an intrinsic (`Func<string,int> f = int.Parse`). Intrinsics are expansions over call sites, so there is no function to point the delegate at. | Synthesize per-method thunks that build a call through the intrinsic table. |
| `exceptions/003_filter_side_effect_order` | Exception filters run during the second (unwinding) pass, after inner `finally` blocks; .NET runs all filters in the first pass, before any `finally`. Documented deviation (ADR 0005). | True two-phase EH: run filters from the personality routine's search phase. |

## Intentional deviations (documented, not counted as failures)

- **Static initialization** is eager (before `Main`), not lazy per type (`beforefieldinit`). Corpus
  programs only print values that are identical under both models.
- **Culture**: formatting/parsing/comparison use the invariant culture.
- **Reflection** is limited to `GetType()`/`typeof`/names; `FullName` of generic types is not
  assembly-qualified.
- **Threading** semantics follow [14-threading.md](14-threading.md).
