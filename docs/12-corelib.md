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
| `src/Csharpc/corelib/*.cs` | the corelib sources (copied next to the compiler; compiled once into `CsharpcCoreLib.dll`, [ADR 0015](adr/0015-corelib-il.md)) |
| `src/Csharpc/Frontend/CoreLibAssembly.cs` | builds and caches that assembly (`~/.cache/csharpc/corelib-<key>.dll`) and its shadow index |
| `src/Csharpc/CodeGen/CoreLib.cs` | discovery of shadows + symbol mapping (types, methods, properties, fields) |
| `src/Csharpc/CodeGen/FunctionCodeGen.CoreLib.cs` | `CoreIntrinsics` codegen, property lvalue captures, value-type null folds |
| `src/Csharpc/CodeGen/ModuleCodeGen.CoreLib.cs` | arrays / strings as BCL interfaces |
| `src/Csharpc/corelib/Globalization/` | .NET's globalization (cultures, formats, calendars, collation, casing) over ICU, see [19](19-globalization.md) |

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
* The corelib is compiled on its own, as `CsharpcCoreLib.dll`, once per symbol set and cached
  ([ADR 0015](adr/0015-corelib-il.md)); programs reference it and its methods are lowered from IL as
  they are reached. `#if` may test `CSHARPC_TARGET_*`, the globalization modes and any program symbol:
  each distinct set of tested symbols is its own assembly. `--corelib source` (or
  `CSHARPC_CORELIB=source`) still parses the sources into the program's compilation instead; keep the
  code valid both ways and stay conservative (no primary constructors, no collection expressions).
* Prefer lazy static data (`static T[] s_x; static T[] X => s_x ??= ...`) to static field
  initializers: an initializer becomes a `.cctor` checked on every static field access of the type.
* Code generated into the program (`[GeneratedRegex]`, the reflection-style JSON contexts) can only use
  **public** corelib members; the corelib reaches generated program code through `CoreIntrinsics`
  hooks (`JsonConfigureHook`/`JsonBuiltInHook` call `CsharpcCoreLib.ProgramHooks` when the program has it).
* `#nullable disable` at the top; corelib diagnostics are never shown to users. A corelib that doesn't
  compile fails every compile with Roslyn's errors; `CSHARPC_CHECK_CORELIB=1` binds the sources inside
  a program's compilation (the source corelib) and reports them all, warnings included.
* Lowering is flow-insensitive: everything a reached method references is lowered and linked, and its
  virtual call sites keep their targets. Keep rarely needed work (big tables, culture-sensitive
  fallbacks, composite formatting in exception messages) out of common paths, or behind something only
  the programs that need it reference. A trailing `StringComparison` argument that is a constant
  `Ordinal`/`OrdinalIgnoreCase` binds to a `<Name>Ordinal` / `<Name>OrdinalIgnoreCase` (or, against an
  ASCII constant, `<Name>AsciiIgnoreCase`) method of the same class when there is one
  (`FunctionCodeGen.OrdinalComparisons.cs`).
* `[OptimizeForSize]` on a class compiles its methods with LLVM `minsize`: for code that runs once
  (building culture data), not per operation.

### CoreIntrinsics

`CsharpcCoreLib.CoreIntrinsics` declares `extern` generic helpers whose bodies codegen synthesizes per
instantiation, giving the default comparers .NET semantics without boxing primitives:

| Intrinsic | Semantics |
|---|---|
| `AreEqual<T>(a, b)` | `EqualityComparer<T>.Default.Equals`: inline for primitives (`double.NaN` equals NaN), ordinal for strings, `IEquatable<T>` if implemented, else virtual `Equals(object)`; element-wise for tuples |
| `HashOf<T>(v)` | `EqualityComparer<T>.Default.GetHashCode` (inline for primitives; 0 for null) |
| `Compare<T>(a, b)` | `Comparer<T>.Default.Compare`: primitives inline (NaN first), strings with culture collation, `IComparable<T>` / `IComparable`, null first; otherwise `InvalidOperationException("Failed to compare two elements in the array.")` |
| `IsValueType<T>()`, `Box<T>(v)`, `Unbox<T>(o)` | helpers for generic code (boxing of types without a boxed form throws `NotSupportedException` at run time only) |
| `EnumToUInt64<T>`, `EnumFromUInt64<T>`, `IsSignedEnum<T>`, `IsFlagsEnum<T>`, `EnumMemberJsonNames<T>` | enum values and names for `JsonStringEnumConverter` (names and `[JsonStringEnumMemberName]` read at compile time) |
| `IsEnumType(Type)`, `CreateEnumConverter(Type, ...)` | a compile-time switch over the program's enums (`JsonStringEnumConverter` as a factory) |
| `IsAbstractType<T>`, `IsInstanceOfType`, `IsTypeAssignableTo`, `IsInterfaceTypeObject` | the slice of reflection the serializer needs (base-chain and interface-map walks in `src/runtime/type.c`) |
| `BuiltInJsonConverter<T>()` | `JsonMetadataServices`' converter for a built-in `T`, chosen at compile time so unused primitive converters aren't linked |

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
* **Value types:** `Guid` (port of .NET's `Guid.cs`: same field layout, so `ToByteArray`/`TryWriteBytes`,
  hashing and ordering match; every parse format and its compat forms with .NET's exception types and
  messages; `NewGuid` from OS entropy as on Unix; `CreateVersion7()`), `ArraySegment<T>` (+`Enumerator`, the
  `IList<T>`/`IReadOnlyList<T>` views, conversions to `Span`/`Memory`, `MemoryExtensions.AsSpan`/`AsMemory`),
  `DateOnly`/`TimeOnly`, `HashCode`.
* **Members-only shadows:** `string.Join<T>/Join(IEnumerable)/Concat<T>/Compare`, `double`/`float`
  `IsNaN`/`IsInfinity`/`IsFinite`..., generic `System.Array` helpers (`Sort` incl. keys+items, `Reverse`,
  `IndexOf`, `Resize`, `Fill`, `Find*`, `Exists`, `TrueForAll`, `ConvertAll`, `BinarySearch`, `Empty`).
* **`System.Text.RegularExpressions`** (`corelib/Regex/`, see [Regular expressions](#regular-expressions)).
* **Compiled-code support for the IL front-end** (docs/adr/0010-il-frontend.md): async method builders,
  `AsyncIteratorMethodBuilder`, `ManualResetValueTaskSourceCore<T>`, `ConfiguredCancelableAsyncEnumerable<T>`,
  `ExceptionDispatchInfo` (`AsyncBuilders.cs`, `AsyncIterators.cs`, `ExceptionDispatchInfo.cs`, see
  [10-async.md](10-async.md#async-state-machines-compiled-from-il)); `ResourceManager` over the IL
  assemblies' embedded string resources and `AppContext` over runtimeconfig data (`Resources.cs`; both
  read constants codegen builds through the `ResourceStrings()`/`RuntimeConfig()` core intrinsics).
* **`System.Text.Json`** (`corelib/Json/`, see [System.Text.Json](#systemtextjson)).

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
* **Culture:** `IgnoreCase` uses .NET's case equivalence tables with the culture's casing behaviour
  (Turkish/Azeri vs. others): the current culture's name at construction, or `CultureInvariant`, or
  `[GeneratedRegex(..., cultureName)]`, as in .NET.
* **Adaptations** (`RegexSupport.cs`): resource strings, `ValueStringBuilder`/`ValueListBuilder` without
  `ArrayPool`, small stand-ins for `Hashtable`, `SearchValues`, `ReadOnlyMemory<char>` segments and the
  `CultureInfo` name, lazily created static tables (corelib types get no static constructor), large
  tables as UTF-16 string constants, no C# 12+ syntax. `AppContext`'s `REGEX_DEFAULT_MATCH_TIMEOUT` is not
  read (the default timeout is infinite). Deriving from `RegexRunner`/`RegexRunnerFactory` and
  `Regex.CompileToAssembly` report `CSLLVM0002`.
* **Cost:** nothing when unused (hello world is byte-identical). A program using `Regex` links the whole
  engine (~450 KB) and takes longer to compile (roughly 15–25 s at `-O2`: the parser, optimizer and
  interpreter are large methods).

### System.Text.Json

`src/Csharpc/corelib/Json/` ports System.Text.Json and System.Text.Encodings.Web from dotnet/runtime
`release/11.0` (license headers kept; [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)):

* **Writer, reader, DOM.** `Utf8JsonWriter` (validation state machine, indentation options, every
  `Write*` overload but `DateTimeOffset`/`Guid` values), `JsonEncodedText`, `JavaScriptEncoder`
  (`Default`, `UnsafeRelaxedJsonEscaping`, `Create(ranges)`, `TextEncoderSettings`) with .NET's escaping
  tables, `Utf8JsonReader` (ported nearly verbatim; `ReadOnlySequence` input is not supported),
  `JsonDocument`/`JsonElement`/`JsonProperty` (same metadata database, so property order, duplicate-key
  lookups and `WriteTo` output match), `JsonNode`/`JsonObject`/`JsonArray`/`JsonValue`, and every
  `JsonException`/`JsonReaderException` message with its `Path`/`LineNumber`/`BytePositionInLine`.
* **Serializer over source-generated metadata.** csharpc already runs the JSON source generator;
  `JsonSerializerContext`, `JsonTypeInfo<T>`, `JsonPropertyInfo`, `JsonMetadataServices` and the
  converters are reimplemented over its output. Supported: objects (properties, fields with
  `IncludeFields`/`[JsonInclude]`, `[JsonPropertyName]`, `[JsonPropertyOrder]`, `[JsonIgnore]` and the
  ignore conditions, `[JsonExtensionData]`), parameterized constructors and records, `required`/
  `[JsonRequired]`/init-only members, callbacks, collections and dictionaries (non-string keys), nullable
  values, enums (`JsonStringEnumConverter`, `JsonNumberEnumConverter`, `[JsonStringEnumMemberName]`,
  flags), custom `JsonConverter<T>`s and factories, polymorphism (`[JsonPolymorphic]`/`[JsonDerivedType]`,
  string/int discriminators, unknown-derived-type handling, `AllowOutOfOrderMetadataProperties`),
  `ReferenceHandler.Preserve`/`IgnoreCycles`, naming policies, `NumberHandling`, `WriteIndented` and
  friends, trailing commas, comments, `MaxDepth`, `JsonObjectCreationHandling.Populate`, contract
  modifiers on `DefaultJsonTypeInfoResolver`, resolver chains, streams and the async APIs (buffered),
  serialization-only contexts (the generated fast path).
* **Reflection-based calls without reflection.** For `JsonSerializer` calls that take no
  `JsonTypeInfo`/context, the front end (`Frontend/JsonReflectionContexts.cs`) collects the root types —
  generic arguments, `typeof` arguments, the static types of values, instantiated through generic
  methods and types — and synthesizes `[JsonSerializable]` contexts for them (nested in the declaring
  class for private types); a corelib partial method wires them in as the stand-in for
  `DefaultJsonTypeInfoResolver`. Types read from the stand-in follow reflection semantics where the
  generator's differ (required/init-only members are set after construction). The
  `JsonSerializerIsReflectionEnabledByDefault` switch is honored: projects evaluate it, file-based apps
  default to `PublishAot` and therefore to disabled reflection, like `dotnet run app.cs`.
* **Code size.** The object converter's work is in a non-generic core, the built-in metadata
  (every primitive converter) is linked only when reflection-based metadata can be requested, and
  `BuiltInJsonConverter<T>` resolves dictionary-key converters at compile time. Hello world is
  unchanged; a program serializing a few types through a source-generated context is about 1 MB, one
  using the reflection-style API about 1.5 MB, and these compile in 20–60 s (most of it in LLVM).
* **Explicit diagnostics (CSLLVM0001/0002)** instead of run-time surprises: anonymous types with the
  reflection-style API, generic virtual methods overridden in generic classes (a codegen limit the
  node and converter classes are written around), `DateTimeOffset`/`Guid` members and BCL types csharpc
  doesn't implement yet (`ConcurrentDictionary`, `ReadOnlyCollection`, immutable collections), and
  `System.Text.Json.Schema`.

Codegen improvements made along the way (general, not corelib-specific): generic structs, structs
implementing interfaces (constrained calls on struct receivers), property/indexer lvalue captures
(`dict[k] = c ? a : b`), `t == null` folds for value-type `T`, generic interface methods excluded from slot
tables, value-type `ToString` through `FormatValue`.

## Type metadata (System.Type reflection)

`System.Type` objects are runtime objects (`src/runtime/type.c`): one per method table, so
`typeof(X) == obj.GetType()` is a pointer comparison, and `Name` / `Namespace` / `ToString()` come from
the method table's CLR name. Everything else reflection answers about a type is **generated at compile
time** (closed world, [ADR 0006](adr/0006-closed-world-compilation.md)) by `ModuleCodeGen.TypeInfo.cs`,
and only for programs that ask:

* **`csrt_TypeInfo` table** (layout in `src/runtime/csrt.h`): one entry per method table the program has
  — classes, boxed structs and enums, interface identity tables, arrays, delegates, boxed primitives,
  name-only tables for types that are never instantiated (`typeof(List<>)`, `typeof(int*)`,
  `typeof(int[,])`, generic parameters) — plus, transitively, every type an entry points to (base type,
  element type, generic definition and arguments, declaring type). Each entry has the boolean
  properties as flag bits (`IsClass`, `IsValueType`, `IsGenericType`, `IsNestedPublic`, ... computed from the
  Roslyn symbol of the *real* BCL type, not its corelib shadow), the related method tables, the
  implemented interfaces, generic variance, and the assembly's full name.
  Codegen emits the table when lowering the first member that needs it (`BclValueTypes.Reflection.cs`
  passes it to the runtime function as an argument) and fills it in after the last function is lowered,
  alternating with the worklists until no new method table appears (`Generate()`; describing a type can
  create the method table of a base class or generic argument). Programs that use none of these members
  carry no table and no extra code (hello world and `typeof(X).Name` programs are byte-for-byte the same
  size as before).
* **Interface lists** keep only interfaces a `Type` object can exist for (the program's interface identity
  tables), plus variant instantiations whose definition the program uses elsewhere (`IEnumerable<string>`
  for `typeof(IEnumerable<object>).IsAssignableFrom(typeof(List<string>))`). `int`'s thirty generic-math
  interfaces don't reach the binary unless the program names one. Arrays list `System.Array`'s
  interfaces; `IList<T>` and its relatives are answered from the element type, covariance included.
* **`csrt_TypeNameTable`** for `Type.FullName` / `AssemblyQualifiedName`: only the types whose `FullName`
  differs from `ToString()` (generic instantiations print assembly-qualified arguments,
  `List`1[[System.Int32, System.Private.CoreLib, Version=11.0.0.0, ...]]`; generic parameters have none),
  so `FullName` of non-generic types costs a few hundred bytes of table at most.
* **Assemblies**: the program's own types report the project's assembly name and the version the SDK
  stamps (`Version` / `AssemblyVersion`, default `1.0.0.0`; projects folded into one program keep their
  own names); IL assemblies report their metadata identity; BCL types report the shared framework
  *implementation* assembly .NET loads them from, found by following the type forwards of
  `shared/Microsoft.NETCore.App/11.*` with System.Reflection.Metadata (`Frontend/SharedFramework.cs`):
  `List<T>` binds against the `System.Collections` reference assembly but lives in
  `System.Private.CoreLib`. `Assembly`, `AssemblyName` and `Version` are corelib C#
  (`corelib/Reflection/Assembly.cs`), one `Assembly` object per name so `==` holds.
* **Assignability** (`type.c`, `assignable`) follows the CLR: base chains, interfaces, generic variance
  (reference-type arguments only), array covariance for reference elements and for primitives sharing
  storage (`int[]`/`uint[]`/enum arrays), `T[]` as `IList<U>`, `Nullable<T>` from `T`, delegates.

Implemented: `IsAssignableFrom`/`IsAssignableTo`/`IsInstanceOfType`/`IsSubclassOf`, every `Is*` predicate
of the kind/visibility/generic families, `BaseType`, `GetElementType`, `GetArrayRank`, `DeclaringType`,
`GetGenericTypeDefinition`, `GetGenericArguments`, `GenericTypeArguments`, `GenericParameterPosition`,
`UnderlyingSystemType`, `Nullable.GetUnderlyingType`, `FullName`, `AssemblyQualifiedName`, `Assembly`,
`Assembly.GetExecutingAssembly`/`GetEntryAssembly`, with .NET's exception types and messages.
Members that would need code or metadata the closed world doesn't have — `MakeGenericType`,
`GetMethods`/`GetProperties`/`GetFields`, `GetInterfaces`, `Activator.CreateInstance`, `IsSerializable`,
`Version.Parse` — are compile-time "not supported by csharpc yet" errors. Where an entry can't be
described faithfully (open generic types' base types and interfaces, corelib-internal types with no
.NET counterpart, signed assemblies), the query throws `NotSupportedException` at run time rather than
answering wrong. Samples: `tests/samples/400_type_assignability` … `403_type_assembly`.

## Cost

Hello world: identical binary size. Compile time: the corelib assembly is built once per machine,
corelib version and symbol set (~6 s); afterwards a compile stats the sources (~45 ms) and lowers the
corelib methods the program reaches from IL, with no parsing or binding of the corelib
([ADR 0015](adr/0015-corelib-il.md)).

## Gaps

* Non-generic `IEnumerator.Current` / `IComparer` on tuple element types throw
  `NotSupportedException` when executed. (Boxed structs do have interface maps: the corelib's struct
  enumerators are `IEnumerator<T>`/`IEnumerator`/`IDisposable` when boxed, as in .NET.)
* Anonymous types (multi-`from` queries with `let`/`where`), `ImmutableArray`/`Immutable*`,
  `ConcurrentDictionary`, `BitArray`, `ObservableCollection`, `Tuple<...>` classes not yet shadowed.
* `SortedDictionary`/`SortedSet` use sorted arrays (same order, O(n) insertion).
