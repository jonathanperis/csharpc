# 12 — The shadow corelib (BCL in C#)

> Policy ([ADR 0023](adr/0023-framework-il-first.md)): the shadows are for the runtime-coupled `System.Private.CoreLib` surface; a
> library of the rest of the framework is compiled from its IL (ADR 0013) where a per-library measurement
> (`CSHARPC_IL_FIRST`, `tools/il-first/ab.sh`) says so, and its shadow is deleted in the same change.

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
| `src/Csharpc/corelib/Dynamic/` | ExpandoObject's and DynamicObject's side of `dynamic` (the meta-object protocol's calls and binders), see [ADR 0019](adr/0019-dynamic.md) |

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
  they are reached. `#if` may test `CSHARPC_TARGET_*` (the OS: `LINUX`/`MACOS`/`WINDOWS`/`WASI`; the architecture:
  `X64`/`ARM64`/`WASM32`), the globalization modes and any program symbol (never `DEBUG`, which is left out):
  each distinct set of tested symbols is its own assembly. `--corelib source` (or
  `CSHARPC_CORELIB=source`) still parses the sources into the program's compilation instead; keep the
  code valid both ways and stay conservative (no primary constructors, no collection expressions).
* Prefer lazy static data (`static T[] s_x; static T[] X => s_x ??= ...`) to static field
  initializers: an initializer becomes a `.cctor` checked on every static field access of the type.
* Code generated into the program (the reflection-style JSON contexts) can only use
  **public** corelib members; the corelib reaches generated program code through `CoreIntrinsics`
  hooks (`JsonConfigureHook`/`JsonBuiltInHook` call `CsharpcCoreLib.ProgramHooks` when the program has it).
* `#nullable disable` at the top; corelib diagnostics are never shown to users. A corelib that doesn't
  compile fails every compile with Roslyn's errors; `CSHARPC_CHECK_CORELIB=1` binds the sources inside
  a program's compilation (the source corelib) and reports them all, warnings included.
* Lowering is flow-insensitive: everything a reached method references is lowered and linked, and its
  virtual call sites keep the overrides of the classes the program constructs (pruning, docs/03-lowering.md;
  `CSHARPC_WHY_LOWERED=<substring>` shows what referenced a function). Keep rarely needed work (big tables, culture-sensitive
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
* **Concurrent collections** (`corelib/Concurrent/`): `ConcurrentDictionary<TKey,TValue>` is a port of .NET's
  (same striped-lock table, lock count from `Environment.ProcessorCount`, per-lock budget, growth to the next
  prime of twice the size, nodes prepended to their bucket, non-randomized hashing of string keys under the
  default/Ordinal/OrdinalIgnoreCase comparers), so it enumerates in .NET's order on the same machine (.NET's
  own order depends on the processor count unless a concurrency level is given); `ConcurrentStack<T>` is a
  port; `ConcurrentQueue<T>` and `ConcurrentBag<T>` are lock-based with .NET's observable order (FIFO; per
  thread LIFO, stealing oldest-first from other threads, snapshots newest queue first) and snapshot
  enumeration. `BlockingCollection<T>` is a port (its blocking waits are cancelable SemaphoreSlim waits
  over the runtime's events; `AddToAny`/`TakeFromAny` wait on several of them with
  `WaitHandle.WaitAny`, `corelib/Threading/WaitHandles.cs`: the runtime has no multi-object wait, so a wait that
  has to block sleeps on the first handle in 1-4 ms slices and re-checks the others, and `WaitAll` takes an
  auto-reset event back (`Set`) when another handle isn't signaled yet; `CancellationToken.WaitHandle` and
  `SemaphoreSlim.AvailableWaitHandle` are `ManualResetEvent`s kept in step as .NET's). `Partitioner`,
  `OrderablePartitioner<T>` and the range/array/list/enumerable partitioners are ports (`corelib/Concurrent/Partitioner.cs`).
  `Parallel.For` / `ForEach` with a `ParallelLoopState`, an index, thread-local state, `ParallelOptions` or a
  partitioner, `Parallel.Invoke(options, ...)`, `ForEachAsync` and `ForAsync` are corelib C#
  (`corelib/Tasks/ParallelState.cs`: .NET's flags: `Stop` starts nothing new, `Break` lets lower iterations finish and
  starts none above it, an orderable partitioner's keys are the iteration indexes); the plain loops run on the
  runtime's pool loop and throw an `AggregateException` around the body's exception, as .NET's. Differences: workers claim
  indexes one by one (not in .NET's growing ranges), so how many iterations above a `Break` run differs, and
  `Enumerable.Range` is not an `IList<int>` here (.NET loops over its positions: a `Break` in a
  `ForEach` over a range stops at a different iteration); `ForEachAsync` over an `IAsyncEnumerable<T>`
  has no implementation yet (explicit CSLLVM0002).
* **Observable behavior follows .NET's implementations:** `List` growth (`Capacity`), `Dictionary`/`HashSet`
  bucket+entries+free-list design (iteration order after removals, prime sizes, FastMod), `PriorityQueue`'s
  4-ary heap (dequeue order of equal priorities), the introsort port (order of equal keys in `List.Sort` /
  `Array.Sort`), stable `OrderBy`, `GroupBy` first-appearance order, exception types and messages.
* **LINQ:** not in the corelib. `System.Linq` is the framework's own IL (ADR 0023, #267), with `Vector<T>` served as 16-byte LLVM vectors
  (`FunctionCodeGen.Simd.cs`) for its vectorized `Sum`/`Min`/`Max`; query syntax is Roslyn's (`ITranslatedQueryOperation`).
* **Value types:** `Guid` (port of .NET's `Guid.cs`: same field layout, so `ToByteArray`/`TryWriteBytes`,
  hashing and ordering match; every parse format and its compat forms with .NET's exception types and
  messages; `NewGuid` from OS entropy as on Unix; `CreateVersion7()`, also from a `DateTimeOffset`),
  `ArraySegment<T>` (+`Enumerator`, the `IList<T>`/`IReadOnlyList<T>` views, conversions to `Span`/`Memory`,
  `MemoryExtensions.AsSpan`/`AsMemory`), `DateOnly`/`TimeOnly`, `HashCode`, `DateTimeOffset` (port of .NET's
  `DateTimeOffset.cs`: same layout, equality and ordering on the UTC instant, formatting and parsing through the
  ported `DateTimeFormat`/`DateTimeParse` with the offset, the local zone from libc like `DateTime`; binary
  serialization is not implemented).
* **`System.Buffers`:** `ArrayPool<T>` (keeps returned arrays as .NET's shared pool does: one per size bucket per
  thread, then 32 per bucket shared, up to 1 Mi elements), `MemoryPool<T>` (port of System.Memory's `ArrayMemoryPool`, buffers from
  `ArrayPool<T>.Shared`, so sizes are .NET's), `IMemoryOwner<T>`.
* **`MemoryExtensions`:** searches and comparisons compare integers, chars, bools and enums byte-wise in the
  runtime and every other element type with `EqualityComparer<T>.Default` semantics (`SpanEquality.cs`), plus the
  `IEqualityComparer<T>` overloads.
* **`System.Convert`:** port of .NET's `Convert.cs` and `ParseNumbers.cs` for the primitive, string (providers and
  bases 2/8/10/16) and `DateTime` overloads; the always-failing `IConvertible` conversions throw .NET's
  `InvalidCastException`. Hex and Base64 are in `ConvertExtras.cs`; the `object` overloads but `ToString(object)` and `ChangeType` are
  not implemented (CSLLVM0002).
* **Members-only shadows:** `string.Join<T>/Join(IEnumerable)/Concat<T>/Compare`, `double`/`float`
  `IsNaN`/`IsInfinity`/`IsFinite`..., generic `System.Array` helpers (`Sort` incl. keys+items, `Reverse`,
  `IndexOf`, `Resize`, `Fill`, `Find*`, `Exists`, `TrueForAll`, `ConvertAll`, `BinarySearch`, `Empty`).
* **Reflection handles and member lookup** (`corelib/Reflection/Handles.cs`, `MemberLookup.cs`): `ParameterModifier` is .NET's struct;
  `RuntimeTypeHandle` holds the runtime's Type object (`Type.TypeHandle`, `GetTypeFromHandle`, `GetTypeHandle`, equality, `Value`; an IL
  `ldtoken` passed as a handle becomes one), and `RuntimeHelpers.RunClassConstructor(typeof(X).TypeHandle)` runs X's lazy static
  initialization (a handle only known at run time is a diagnostic). `DefaultValueAttribute` is a shadow of .NET's class
  (all constructors, the `IsSupported` switch). `Type.GetMember(s)` and TypeInfo's `GetDeclared*`/`Declared*` are composed from the
  per-kind queries in RuntimeType's order (methods, constructors, properties, events, fields) with its name filter (exact, `IgnoreCase`,
  trailing `*`) and always return a `MemberInfo[]` (csharpc arrays carry their element type, and a `MethodInfo[]` would not be an
  `IEnumerable<MemberInfo>`). Nested types are not described by the reflection metadata: asking for them alone, or for
  `DeclaredNestedTypes`, throws `NotSupportedException`, while `MemberTypes.All`, `GetMembers` and `DeclaredMembers` leave them out.
* **`System.MathF` and floating-point math** (`CodeGen/Intrinsics.cs` `AddMathF`, `corelib/FloatingPointMath.cs`): what .NET calls
  through the C runtime is libm's float function (`sinf`, `powf`, `acoshf`, ...) or an exact LLVM intrinsic (`Sqrt`, `Floor`, `Round`,
  `FusedMultiplyAdd`, `CopySign`, `Max`/`Min` as IEEE 754-2019); the members that are C# in .NET are ports of
  dotnet/runtime's own (`BitIncrement`, `IEEERemainder`, `ILogB`, `ScaleB`, `Round(x, digits, mode)` exact on the decimal value,
  `MaxMagnitude`, `Sign`...). The float and double generic-math statics (`float.Hypot`, `double.Lerp`, `T.SinPi(x)`,
  `RootN`, `Exp2`/`Exp10`, the degree conversions, `MaxNumber`, `IsNormal`, ... that `IFloatingPointIeee754<T>` needs) are
  copied from `Single.cs`/`Double.cs` of the .NET 11 runtime and checked against it bit for bit (a differential run of every
  member over edge values and a few hundred pseudo-random inputs). `ReciprocalEstimate` / `ReciprocalSqrtEstimate` return
  the exact `1 / x` (.NET uses `RCPSS`/`FRECPE` where the hardware has them, so its answer differs between CPUs and is
  only an estimate). Results of the libm functions follow the platform's libm (glibc here, as .NET on Linux), so a few
  differ in the last bit on WebAssembly's libc; `Math.Log`/`Log2` of a negative number return the positive quiet NaN on Unix as .NET does (Windows passes its C runtime's through).
* **`System.Text.RegularExpressions`** is not a shadow: it is compiled from the framework's IL ([Regular expressions](#regular-expressions)).
* **Compiled-code support for the IL front-end** (docs/adr/0010-il-frontend.md): async method builders,
  `AsyncIteratorMethodBuilder`, `ManualResetValueTaskSourceCore<T>`, `ConfiguredCancelableAsyncEnumerable<T>`,
  `ExceptionDispatchInfo` (`AsyncBuilders.cs`, `AsyncIterators.cs`, `ExceptionDispatchInfo.cs`, see
  [10-async.md](10-async.md#async-state-machines-compiled-from-il)); `ResourceManager` over the IL
  assemblies' embedded string resources and `AppContext` over runtimeconfig data (`Resources.cs`; both
  read constants codegen builds through the `ResourceStrings()`/`RuntimeConfig()` core intrinsics).
* **`TypeConverter` / `TypeDescriptor`**: not in the corelib. System.ComponentModel, System.ComponentModel.Primitives and
  System.ComponentModel.TypeConverter come from the framework's IL (ADR 0023, #276); the corelib serves what that code asks of the
  runtime: the attributes of framework types (`Type.GetCustomAttributes` over the IL assemblies' metadata, the `[Serializable]`
  pseudo-attribute, a `[TypeConverter(string)]` type named in it kept for creation), `Type.Attributes`, `Enum.Format(Type, ...)`
  and `Enum.GetValuesAsUnderlyingType` for run-time Types, `ArrayList.Adapter`, and the negative answers of COM
  (`Marshal.IsComObject`, `ComWrappers.TryGetComInstance`; `Marshal.Release` is a `PlatformNotSupportedException`: no COM object
  exists). The front end (`Frontend/ResourceConverters.cs`) still generates `ProgramHooks.ResourceObject` for the types a program's
  preserialized `.resources` name, which `ResourceManager.GetObject` creates with `ConvertFromInvariantString` as
  `DeserializingResourceReader` does. Resources stored as BinaryFormatter data, byte arrays or activator streams (`Bitmap`, `Icon`,
  `ResXFileRef`) are explicit `NotSupportedException`s naming the type.
* **`ResourceSet`, `GetResourceSet` and `GetStream`** (`corelib/ResourceSets.cs`, `Resources.cs`): `ResourceManager` keeps
  the neutral culture's set (`RuntimeResourceSet`) as .NET's does, so `GetString`/`GetObject` and `GetResourceSet` share it
  (closing it closes it for the manager until `ReleaseAllResources`). A culture other than the invariant one gets the
  neutral set only with `tryParents` (there are no satellite assemblies), and a set found through the fallback is
  remembered under the cultures asked for. `ResourceSet` has `GetString`/`GetObject` with `ignoreCase`, the enumerator,
  `GetDefaultReader`/`GetDefaultWriter`, `Close`/`Dispose`. Values stored by type code (`ResourceWriter`'s primitives)
  are boxed once and cached (reading one as a string afterwards is an `InvalidCastException`), byte arrays and
  `Stream` entries are created on every read, objects `DeserializingResourceReader` creates from preserialized types are
  made again each time; `GetString` of a non-string names the type code (`'Int32'`, `'ByteArray'`, `'Stream'`) for the former and
  the type for the latter. `GetStream` is a new read-only `UnmanagedMemoryStream` over a native copy of the bytes
  (shared per entry, never freed, as the assembly's image is), `GetObject` of a `Stream` entry its
  `UnmanagedMemoryStreamWrapper`. The front end now keeps `Stream` entries' bytes (`ResourcesFile`). Not provided (rejected
  at compile time): `new ResourceSet(file|stream|reader)` and subclassing `ResourceSet`. A resource set the closed world
  has no table for is still the explicit `NotSupportedException` of `GetString`.
* **`System.Text.Json`** (`corelib/Json/`, see [System.Text.Json](#systemtextjson)).
* **`EventSource` and `EventListener`** (`corelib/Tracing/EventSource.cs`, `EventListener.cs`): a csharpc executable
  has no EventPipe/ETW session, so what enables an `EventSource` is an in-process `EventListener` (derivable). Without
  one `IsEnabled()` is false and `WriteEvent`/`WriteEventCore`/`Write` return at once. A listener's `EnableEvents`
  builds the event table from the `[Event]` methods (the compiler records them as text, `EventSourceAttribute(type, 2)`
  in `ModuleCodeGen.EventSource.cs`, instead of .NET's reflection), checks it as .NET checks a manifest (positive
  ids, no duplicate ids, keywords defined in the nested `Keywords` class, defined opcodes, supported parameter types:
  an error reaches the listener as an `EventSourceMessage` event and the source stays disabled), calls
  `OnEventCommand`, and then `WriteEvent` dispatches to every listener whose level and keyword filter lets the event
  through, newest listener first: `EventWrittenEventArgs` has .NET's id, name, level, keywords (with ETW's session
  bits), opcode, task (an event with neither task nor opcode gets 0xFFFE minus its id), version, channel, tags,
  message, payload names and values (the declared parameter types for the fixed-arity `WriteEvent` overloads, the
  boxed arguments for `params object[]`), activity ids, thread id and time stamp. `OnEventSourceCreated` runs for
  the sources that exist when a listener is created (from the listener's base constructor, before the derived
  constructor, as in .NET) and for later ones; a new `EventSourceCreated` handler hears of the existing sources
  at once. `Write(name)` / `Write(name, options)` dispatch (null `Payload` and `PayloadNames`). `Write<T>` reads the
  payload as .NET does (`corelib/Tracing/SelfDescribing.cs`): the public instance properties of an anonymous type or a
  type marked `[EventData]` (derived type first, `[EventIgnore]` honored), as scalars (primitives, enums, `Guid`,
  `DateTime`, `DateTimeOffset`, `TimeSpan`, `decimal`, nullables), strings (null becomes `""`), nested payload types (a
  read-only `IDictionary<string, object>`), and arrays (the array itself for primitives and `Guid`, an `object[]` of the
  converted elements otherwise), through csharpc's reflection metadata (`T` is annotated `PublicProperties`; the compiler
  adds the types of its properties, `MarkEventPayloadGraph`; an anonymous type has a Type object and getter functions
  for it). The analysis runs whenever the source is enabled and fails as .NET does (`ArgumentException` for a
  non-compliant type, `ArgumentNullException` for a primitive or enumerable `T`, `NotSupportedException` for string and
  nested arrays, an `EventSourceMessage` event when the data can't be read); a self-describing event is checked
  against the source's widest level and keywords and then goes to every listener that enabled the source. Not
  reproduced (an explicit `NotSupportedException`): multi-dimensional array and non-array enumerable properties,
  recursive payload types. Differences: a listener does not see the EventSources .NET's runtime
  and libraries create (`Microsoft-Windows-DotNETRuntime`, `System.Runtime`, `System.Threading.Tasks.TplEventSource` ...),
  so `GetSources()` lists only the program's own; the manifest check that .NET makes by reading each event method's IL
  ("Event X was assigned event ID n but m was passed to WriteEvent") is not made; `SendCommand`, `GenerateManifest`
  and the event counters are not implemented (CSLLVM0002), `WriteEventCore` of a `DateTime` parameter is a NotSupportedException (boxing one would link the whole date formatter), and a program that writes events with typed payloads links the boxed-primitive formatters its payload values need (the 446 sample grows from 204 to 274 KB). Without `#:property EventSourceSupport=true` (the file-based
  app default) `EventSource.IsSupported` is false, a source never registers and no listener sees it, as in .NET.
  What a program can observe without a listener is .NET's: the `IsSupported` feature switch, `Name` and `Guid` from
  `[EventSource]` or derived from the name with .NET's SHA-1 name hashing, settings validation, traits,
  `ConstructionException`, `ToString`.

* **`System.Uri`** (`corelib/Net/Uri.cs`, `UriRelative.cs`, `UriMembers.cs`): the URIs whose .NET behaviour needs no
  escaping rules (absolute http, https, ftp, ws and wss URIs with a DNS name or canonical IPv4 host, plain file
  paths, relative references of plain text); everything else is a `NotSupportedException` naming the case. On
  those it is exact, down to messages: dot-segment removal (`Uri.Compress`), `MakeRelativeUri` / `MakeRelative`
  (`PathDifference`, the case-insensitive DOS-path comparison, the `./` guard for a ':' in the first segment),
  `new Uri(Uri, string/Uri)` and `Uri.TryCreate(Uri, ...)` (`CombineUri`), `GetComponents` / `GetLeftPart` /
  `Compare` / `IsBaseOf`, `IsWellFormedOriginalString`, `TryFormat`, `CheckHostName` for DNS and IPv4 names,
  `EscapeUriString`, `TryEscapeDataString`, `HexEscape` / `HexUnescape` / `FromHex`, `UriCreationOptions`
  (the canonicalization-off option is a `NotSupportedException`). Resolving against a file URI, references
  with a ':' in the first segment (`g:h`, `http:g`) and IPv6/IDN/user info hosts are not covered.

### Regular expressions

`System.Text.RegularExpressions` has no corelib port: the framework's own assembly is compiled from its IL
(ADR 0013, [ADR 0023](adr/0023-framework-il-first.md)), so every engine, option, `Match`/`Group`/`Capture` type,
`Replace`/`Split`/`Count`/`EnumerateMatches`, the static cache, `RegexParseException` and
`RegexMatchTimeoutException` are .NET's code, and deriving from `RegexRunner` works. The port that stood here
(16 830 lines, interpreter only) was deleted by the flip.

* **Engines:** whatever the framework picks: the interpreter, and `RegexOptions.NonBacktracking`'s symbolic engine.
  `RegexOptions.Compiled` selects the Reflection.Emit code generator in .NET; run-time code generation is not
  possible under csharpc (as in NativeAOT), so the framework's own fallback to the interpreter applies and the
  results are the same.
* **`[GeneratedRegex]`:** the SDK's own generator (`System.Text.RegularExpressions.Generator.dll` of the targeting
  pack, linked into csharpc like the JSON and `LibraryImport` generators, `Frontend/SourceGenerators.cs`) runs, and
  its `RegexRunner`-derived classes run against the real `Regex`. Its diagnostics (SYSLIB1040-1043) and `GetType()`
  are .NET's. In a program given as IL the SDK's generator already ran and its output is ordinary IL.
* **Cost:** nothing when unused (hello world is byte-identical). A program that uses `Regex` links the framework's
  engine: about 1.4x the size the port had for `new Regex(pattern)`, and about 3.6x once it calls a constructor or
  static method that takes `RegexOptions`, which reaches the NonBacktracking engine (ADR 0023 has the table). The
  owner accepted the size for exactness and speed (the framework's code is 1.4x faster on the throughput program).

### System.Text.Json

`src/Csharpc/corelib/Json/` ports System.Text.Json and System.Text.Encodings.Web from dotnet/runtime
`release/11.0` (license headers kept; [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)):

* **Writer, reader, DOM.** `Utf8JsonWriter` (validation state machine, indentation options, every
  `Write*` overload), `JsonEncodedText`, `JavaScriptEncoder`
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
  A program given as IL (`csharpc app.dll`) gets the same contexts, its roots read from the method
  bodies (`Frontend/JsonReflectionContexts.Il.cs`: type arguments, the static type a value had before
  csc passed it as its base or `object`, `typeof`/`GetType()` arguments, generic instantiations). The
  generator only describes what its context can access, and the context lives in csharpc's binding
  compilation, not in the program's assembly: the program grants that compilation internals access
  (an `[InternalsVisibleTo]` Roslyn is told about through reflection, before anything binds), and the
  compilation imports private members, so internal types, internal and private `[JsonInclude]` members
  look as they do from source. Private nested types, which a source context reaches by being nested
  beside them, are a `CSLLVM0001` where the call is lowered.
* **Culture-invariant text.** Numbers, dates and GUIDs are written and parsed with the invariant culture whatever
  the current culture is (a decimal comma, a Unicode minus, other digits or calendars): the writer, the converters
  (including `JsonNumberHandling` strings and numeric dictionary keys, which are written unescaped like .NET's) and
  the DOM. `1250_json_culture_real` (ICU cultures) and `1251_json_culture_synthetic` (cultures built from the invariant
  one, every platform) compare the output of 12 and 5 cultures with the invariant run.
* **JSON schema export.** `JsonSchemaExporter.GetJsonSchemaAsNode` (over `JsonSerializerOptions` or a
  `JsonTypeInfo`) with `JsonSchemaExporterOptions` (`TreatNullObliviousAsNonNullable`,
  `TransformSchemaNode`) and `JsonSchemaExporterContext`, ported from `release/11.0`
  (`corelib/Json/SchemaExporter.cs`). Each built-in converter reports its own schema
  (`JsonConverterImpl.GetSchema`: numbers with their string patterns under `NumberHandling`, enums,
  `format`s, `contentEncoding`, the `true` schema for opaque converters); objects, collections,
  dictionaries, polymorphism (`anyOf` with the discriminator), recursive types (`$ref`), constructor
  defaults, required members and nullability follow the contract. `deprecated` comes from `[Obsolete]`: a
  type's at compile time, a member's through the generator's `AttributeProviderFactory`, which csharpc
  otherwise drops, kept only for members carrying the attribute.
* **Code size.** The object converter's work is in a non-generic core, the built-in metadata
  (every primitive converter) is linked only when reflection-based metadata can be requested, and
  `BuiltInJsonConverter<T>` resolves dictionary-key converters at compile time. Hello world is
  unchanged; a program serializing a few types through a source-generated context is about 1 MB, one
  using the reflection-style API about 1.5 MB, and these compile in 20–60 s (most of it in LLVM).
* **Explicit diagnostics (CSLLVM0001/0002)** instead of run-time surprises: anonymous types with the
  reflection-style API and the BCL members csharpc doesn't implement.

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
  (`corelib/Reflection/Assembly.cs`), one `Assembly` object per name so `==` holds. The program's own and the IL assemblies
  also have a compiled-in type list (`GetTypes`, `ExportedTypes`, `Module.GetTypes`, in metadata order, csc's synthesized
  types included: the program is emitted once with Roslyn to read them), a manifest `Module`, `EntryPoint` and, for the program,
  `GetReferencedAssemblies` ([ADR 0018](adr/0018-reflection-metadata.md)).
* **Assignability** (`type.c`, `assignable`) follows the CLR: base chains, interfaces, generic variance
  (reference-type arguments only), array covariance for reference elements and for primitives sharing
  storage (`int[]`/`uint[]`/enum arrays), `T[]` as `IList<U>`, `Nullable<T>` from `T`, delegates.

Implemented: `IsAssignableFrom`/`IsAssignableTo`/`IsInstanceOfType`/`IsSubclassOf`, every `Is*` predicate
of the kind/visibility/generic families, `BaseType`, `GetElementType`, `GetArrayRank`, `DeclaringType`,
`GetGenericTypeDefinition`, `GetGenericArguments`, `GenericTypeArguments`, `GenericParameterPosition`,
`UnderlyingSystemType`, `Nullable.GetUnderlyingType`, `FullName`, `AssemblyQualifiedName`, `Assembly`,
`Assembly.GetExecutingAssembly`/`GetEntryAssembly`, with .NET's exception types and messages.
Member reflection — constructors, methods, properties, fields, events, parameters, attributes, `Invoke`,
`GetValue`/`SetValue`, `CreateDelegate` (closed over a target, static, or open instance with the receiver as first parameter), `GetInterfaces`, `Activator.CreateInstance(Type)`, and
`MakeGenericType` over the instantiations the program has — works over metadata kept for what
`[DynamicallyAccessedMembers]` and constant `typeof(X).GetMethod("Name")` patterns reach, as under
NativeAOT ([ADR 0018](adr/0018-reflection-metadata.md)). Members that would need code or metadata the
closed world doesn't have (`IsSerializable`) are compile-time "not supported by csharpc yet" errors. Where an entry can't be
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
* `System.Collections.Immutable` is not shadowed: it is compiled from the shared framework's IL
  ([ADR 0013](adr/0013-framework-il.md)).
* `SortedDictionary`/`SortedSet` use sorted arrays (same order, O(n) insertion).
* **`ObjectiveCMarshal`** (`corelib/ObjectiveCMarshal.cs`, the hooks of the .NET for macOS / iOS bindings): off macOS
  every member throws `PlatformNotSupportedException` as .NET does. On macOS the reference tracking works as in
  NativeAOT: `Initialize` registers the begin/end, `isReferenced` and entered-finalization callbacks with the
  collector; a class with `[ObjectiveCTrackedType]` and a finalizer gets tagged memory (`GetOrCreateReferenceTrackingMemory`)
  and reference-tracking handles (`CreateReferenceTrackingHandle`); each collection asks `isReferenced(taggedMemory)`
  about the tracked objects nothing else reached (a referenced one survives with what it reaches), and announces an
  object entering the finalizer queue ([04-runtime.md](04-runtime.md#objective-c-reference-tracking),
  [ADR 0021](adr/0021-finalizers.md)). `SetMessageSendCallback` overrides libobjc's `objc_msgSend` family (the P/Invoke
  stubs of those entry points call the registered function) and `SetMessageSendPendingException` stores an exception
  the stub throws when the call returns. Verified on the macOS runner against real .NET (samples 970, 977, 978). The
  unhandled-exception propagation handler is stored but never called (an exception escaping an `[UnmanagedCallersOnly]`
  method ends the process).
