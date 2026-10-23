# 10 — The shadow corelib (BCL in C#)

csharpc's route to drop-in BCL coverage: library types re-implemented **in C#**, compiled by csharpc
together with the user's program (closed world: monomorphized, inlined and optimized by LLVM along with
user code). User code keeps binding against the **real** .NET reference assemblies, so Roslyn's overload
resolution, nullability, diagnostics and the IDE experience stay exact. Codegen redirects.

```
user code:   var d = new Dictionary<string,int>();  d["x"] = 1;   words.Where(...).OrderBy(...)
Roslyn:      System.Collections.Generic.Dictionary<string,int>.set_Item(string, int)   (real metadata)
csharpc:     CsharpcCoreLib.DictionaryImpl<string,int>.set_Item(string, int)           (corelib C#)
```

## Layout

| Path | What |
|---|---|
| `src/Csharpc/corelib/*.cs` | the corelib sources (copied next to the compiler; parsed into every compilation) |
| `src/Csharpc/CodeGen/CoreLib.cs` | discovery of shadows + symbol mapping (types, methods, properties, fields) |
| `src/Csharpc/CodeGen/FunctionCodeGen.CoreLib.cs` | `CoreIntrinsics` codegen, property lvalue captures, value-type null folds |
| `src/Csharpc/CodeGen/ModuleCodeGen.CoreLib.cs` | arrays / strings as BCL interfaces |
| `src/runtime/collation.c` | culture string comparison (ICU-root approximation) used by `Comparer<string>` |

## Declaring a shadow

```csharp
[Shadows("System.Collections.Generic.List`1")]           // CLR metadata name; same generic arity
public class ListImpl<T> : IList<T>, IReadOnlyList<T> { ... }

[Shadows("System.Collections.Generic.List`1+Enumerator")]  // nested types: '+'
public struct Enumerator { ... }                           // declared inside ListImpl<T>

[ShadowMembers("System.String")]                           // members only: the type itself is not replaced
internal static class StringExtras { public static string Join<T>(string separator, IEnumerable<T> values) ... }
```

* **Matching.** A library member maps to the shadow member with the same metadata name, generic arity,
  static-ness and *canonical signature* (parameter types with shadow types spelled by their real names and
  type parameters by position). Return types are not compared, so `GetEnumerator()` may return the shadow
  struct while the real one returns `List<T>.Enumerator` — they are the same type after mapping.
* **Type arguments are never rewritten**; only the generic definition is swapped
  (`List<List<int>>` → `ListImpl<List<int>>`). That keeps exactly one instantiation per real type.
* **Where the redirect applies:** `FunctionCodeGen.S()` / `TypeOf()` (every symbol/type codegen reads goes
  through substitution, then the redirect), `GetFunction`, `GetLayout` (incl. user classes deriving from a
  shadowed class, e.g. `class ByLength : Comparer<string>`, and override roots), struct layouts/`TypeMapper`
  (`KeyValuePair<K,V>`, `List<T>.Enumerator` as fields/array elements/tuple elements), static fields,
  boxing/type tests (`MethodTableFor`), interface implementation lookup, delegate creation.
* **Priority:** hand-written intrinsics (`Intrinsics.cs`, runtime C) win over shadows.
* **Diagnostics:** a library member with neither intrinsic nor shadow still reports
  `CSLLVM0002 '<member>' has no implementation in the csharpc runtime yet`.
* **Runtime type names:** shadows report their BCL names (`Mangler.RealNameOf`), so
  `new List<int>().ToString()` prints `System.Collections.Generic.List`1[System.Int32]` like .NET.

## Writing corelib code

* Refer to BCL types by their **real** names (`List<T>`, `KeyValuePair<K,V>`, `IEnumerable<T>`); use the
  shadow name only inside its own class (or via a cast through `object`).
* The corelib is parsed with the **user's** parse options, so stay conservative: no primary constructors,
  no collection expressions, and **no static field initializers** (corelib types are not part of the static
  initializer; use lazy init: `static T[] s_x; static T[] X => s_x ??= ...`).
* `#nullable disable` at the top; corelib diagnostics are never shown to users
  (`CSHARPC_CHECK_CORELIB=1` reports them — use it when editing the corelib).

### CoreIntrinsics

`CsharpcCoreLib.CoreIntrinsics` declares `extern` generic helpers whose bodies codegen synthesizes per
instantiation, giving the default comparers .NET semantics without boxing primitives:

| Intrinsic | Semantics |
|---|---|
| `AreEqual<T>(a, b)` | `EqualityComparer<T>.Default.Equals`: inline for primitives (`double.NaN` equals NaN), ordinal for strings, `IEquatable<T>` if implemented, else virtual `Equals(object)`; element-wise for tuples |
| `HashOf<T>(v)` | `EqualityComparer<T>.Default.GetHashCode` (inline for primitives; 0 for null) |
| `Compare<T>(a, b)` | `Comparer<T>.Default.Compare`: primitives inline (NaN first), strings with culture collation, `IComparable<T>` / `IComparable`, null first; otherwise `InvalidOperationException("Failed to compare two elements in the array.")` |
| `IsValueType<T>()`, `Box<T>(v)`, `Unbox<T>(o)` | helpers for generic code (boxing of types without a boxed form throws `NotSupportedException` at run time only) |

## Arrays and strings as interfaces

All arrays share one runtime method table (two, since the GC split primitive arrays), so they can't carry
interface maps. Instead, when the program converts `T[]` (or `string`) to a BCL interface, codegen builds a
slot table of `CsharpcCoreLib.SZArrayHelper<T>` (`StringInterfaces`) static methods — named
`<interface metadata name without backtick>_<member>` — and stores it in the **interface's** identity table
(`interfaces` field for arrays, `base` field for strings). `csrt_interface_slots` / `csrt_isinst_class` fall
back to it. Calls whose receiver is statically an array/string are bound directly to the helper.
Limitation: `object is IEnumerable<X>` on an array can't check the element type at run time.

## What's implemented

* **Collections:** `List<T>` (+`Enumerator` struct, version checks), `Dictionary<TKey,TValue>` (+`KeyCollection`,
  `ValueCollection`, enumerators), `KeyValuePair<K,V>` (+`KeyValuePair.Create`), `HashSet<T>` (full set algebra),
  `Queue<T>`, `Stack<T>`, `LinkedList<T>`/`LinkedListNode<T>`, `PriorityQueue<TElement,TPriority>`,
  `SortedList<K,V>`, `SortedDictionary<K,V>`, `SortedSet<T>`, `EqualityComparer<T>`, `Comparer<T>`
  (`Default`, `Create`, subclassing), `CollectionExtensions` (`GetValueOrDefault`, `TryAdd`, `Remove`).
* **Observable behavior follows .NET's implementations:** `List` growth (`Capacity`), `Dictionary`/`HashSet`
  bucket+entries+free-list design (iteration order after removals, prime sizes, FastMod), `PriorityQueue`'s
  4-ary heap (dequeue order of equal priorities), the introsort port (order of equal keys in `List.Sort` /
  `Array.Sort`), stable `OrderBy`, `GroupBy` first-appearance order, exception types and messages.
* **LINQ** (`System.Linq.Enumerable`): Where/Select (+index), SelectMany (3 forms), Take/Skip/TakeWhile/
  SkipWhile/TakeLast/SkipLast, Concat/Append/Prepend/Reverse, Zip (2 forms), Chunk, DefaultIfEmpty,
  Distinct/DistinctBy/Union/Intersect/Except, OrderBy/ThenBy (+Descending, comparers), Order/OrderDescending,
  GroupBy (4 forms), ToLookup, CountBy, Join/GroupJoin, ToList/ToArray/ToDictionary/ToHashSet, Range/Repeat/
  Empty, Count/LongCount/Any/All/Contains/SequenceEqual, First/Last/Single(+OrDefault, +default values),
  ElementAt(+OrDefault), Sum/Average/Min/Max (int/long/double/float, selectors, generic), MinBy/MaxBy,
  Aggregate (3), Cast/OfType, Index; query syntax (via `ITranslatedQueryOperation`). Lazy operators are
  hand-written iterator classes with .NET's deferred-execution semantics.
* **Members-only shadows:** `string.Join<T>/Join(IEnumerable)/Concat<T>/Compare`, `double`/`float`
  `IsNaN`/`IsInfinity`/`IsFinite`..., generic `System.Array` helpers (`Sort` incl. keys+items, `Reverse`,
  `IndexOf`, `Resize`, `Fill`, `Find*`, `Exists`, `TrueForAll`, `ConvertAll`, `BinarySearch`, `Empty`).
* **`System.Text.RegularExpressions`** (`corelib/Regex/`, see [Regular expressions](#regular-expressions)).

### Regular expressions

`corelib/Regex/` is a port of .NET's own engine (dotnet/runtime release/11.0, commit `bc123d0`,
MIT, [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)): `RegexParser`, the `RegexNode` tree and its
optimizer, `RegexFindOptimizations` / `RegexPrefixAnalyzer`, `RegexCharClass` (+ its Unicode tables),
`RegexCaseEquivalences`, `RegexWriter`, `RegexInterpreter` / `RegexRunner`, and the public API
(`Regex` static and instance methods, `Match`/`Group`/`Capture` and their collections, `Replace` with
substitutions and `MatchEvaluator`, `Split`, `Escape`/`Unescape`, `Count`, `EnumerateMatches`/`ValueMatch`,
`EnumerateSplits`, `RegexMatchTimeoutException`, `RegexParseException`, the static cache). The files keep
upstream's names and structure; the mechanical adaptations are listed below, and changes of substance are
marked `csharpc:` in the code. Results, captures, exception types
and messages are .NET's, which the differential fuzzer (`tools/fuzz/corelib.sh`, random patterns with
all option combinations) checks.

* **Engine choice:** every regex runs on the interpreter. `RegexOptions.Compiled` has the same results
  (it only selects .NET's Reflection.Emit code generator). `RegexOptions.NonBacktracking` throws
  `NotSupportedException("RegexOptions.NonBacktracking is not supported by csharpc yet.")` when the
  `Regex` is constructed: its engine (symbolic automata) is not ported and its capture semantics differ.
* **`[GeneratedRegex]`:** csharpc's own source generator (`Frontend/GeneratedRegex.cs`) replaces the
  SDK's, whose output is built on `RegexRunner` internals. Each member returns a cached interpreter
  instance for the attribute's pattern, options, timeout and culture; the SDK generator's diagnostics
  (SYSLIB1040–1043) are reproduced. Only `GetType()` differs (`Regex` instead of the generated class).
* **Culture:** csharpc is invariant-culture, so `IgnoreCase` uses .NET's invariant case equivalences
  (`CultureInfo.CurrentCulture` is invariant, as with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`);
  `[GeneratedRegex(..., cultureName)]` honours the culture's casing behaviour (Turkish/Azeri vs. others).
* **Adaptations** (`RegexSupport.cs`): resource strings, `ValueStringBuilder`/`ValueListBuilder` without
  `ArrayPool`, small stand-ins for `Hashtable`, `SearchValues`, `ReadOnlyMemory<char>` segments and the
  `CultureInfo` name, lazily created static tables (corelib types get no static constructor), large
  tables as UTF-16 string constants, no C# 12+ syntax. `AppContext`'s `REGEX_DEFAULT_MATCH_TIMEOUT` is not
  read (the default timeout is infinite). Deriving from `RegexRunner`/`RegexRunnerFactory` and
  `Regex.CompileToAssembly` report `CSLLVM0002`.
* **Cost:** nothing when unused (hello world is byte-identical). A program using `Regex` links the whole
  engine (~450 KB) and takes longer to compile (roughly 15–25 s at `-O2`: the parser, optimizer and
  interpreter are large methods).

Codegen improvements made along the way (general, not corelib-specific): generic structs, structs
implementing interfaces (constrained calls on struct receivers), property/indexer lvalue captures
(`dict[k] = c ? a : b`), `t == null` folds for value-type `T`, generic interface methods excluded from slot
tables, value-type `ToString` through `FormatValue`.

## Cost

Hello world: identical binary size; compile-time overhead is parsing the corelib (~0.1–0.2 s warm; shadow
discovery is syntax-only, and only the members a program uses get bound and lowered).

## Gaps

* Non-generic `IEnumerator.Current` / `IComparer` on tuple element types throw
  `NotSupportedException` when executed. (Boxed structs do have interface maps: the corelib's struct
  enumerators are `IEnumerator<T>`/`IEnumerator`/`IDisposable` when boxed, as in .NET.)
* Anonymous types (multi-`from` queries with `let`/`where`), `ImmutableArray`/`Immutable*`,
  `ConcurrentDictionary`, `BitArray`, `ObservableCollection`, `Tuple<...>` classes not yet shadowed.
* `string` culture comparison is exact for ASCII/Latin-1 only (ICU root approximation).
* `SortedDictionary`/`SortedSet` use sorted arrays (same order, O(n) insertion).
