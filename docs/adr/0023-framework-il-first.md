# ADR 0023 — Framework IL first: hand-implement the runtime-coupled core, compile the rest of the framework from its IL

- **Status:** Accepted as policy (2026-11-08, owner decision); the per-library decisions below are measured. System.Text.RegularExpressions
  is flipped (#206, owner: "flip now, accept the size"); no other default has changed yet. Issues #187 (policy) and #189 (this ADR).

## Context

csharpc grew its BCL the other way round: the corelib (`src/Csharpc/corelib`, ~169 000 lines of C#) shadows far more than
`System.Private.CoreLib`: Regex, JSON, LINQ, collections, networking, X.509/`SslStream`, crypto, Xml. Each shadow is a port
that has to be kept equal to .NET's behaviour for ever, and the ports are where the bugs are: this week's were JSON culture
(#89), `Half` and `BigInteger` formatting, `MathF`, vectors. The two native-TLS spikes (#143: 11 constructs, 14 of 14 samples
on real Windows) showed the alternative: the framework's own IL, compiled by the IL front-end (ADR 0010, ADR 0013), is
exact for free.

Against it: size and compile cost (the Windows https client: +1.7 MB, +35 % compile time), compile scale (#93), no mapping of
`Vector128` and the hardware intrinsics yet, and CoreLib internals that other framework assemblies use.

## Decision

1. **Hand-implement only what is coupled to the runtime**: the `System.Private.CoreLib` surface of the pinned .NET version
   (types, GC and exception interface, threads, the `System.Buffers.Text`/`System.Text.Unicode`/`System.Numerics` members
   CoreLib defines, OS boundaries). **Compile everything else in the shared framework from its IL** (ADR 0013), library
   by library.
2. **Per library, by measurement** (priorities: security, then speed, then size): the shadow route and the IL route are
   built from the same samples; correctness against the recordings made on real .NET comes first, then security (every
   guard the shadow had, such as bounds, limits and hardening, must survive the move to upstream IL; the row says how
   that was checked), then run-time speed, then size and compile time. A library flips to IL where IL wins or ties; a
   shadow stays only where it is measurably safer, faster or smaller, with the numbers in the table below.
3. **Only the code that is necessary, no bloat** (owner rule):
   - A flip is one PR that makes IL the default for that library and *deletes* its shadow code (`corelib/<Area>`) and the
     samples that covered only the shadow, and reports the lines removed (`git diff --shortstat`). No dead fallback is kept
     "just in case"; the exception is a library whose decision is explicitly *partial*, and then the remaining shadow is named.
   - `CSHARPC_IL_FIRST` is a **measurement tool**. Once a library is decided its use of the switch is removed and the policy is
     the default.
   - **Nothing library-specific is added for an IL-compiled library.** What the IL route lacks is fixed in the IL front-end or in
     the runtime-coupled core (CoreLib members, codegen), so every assembly benefits. The blockers found while measuring
     System.Text.Json and System.Text.RegularExpressions were all of that kind (below).

### The switch

`CSHARPC_IL_FIRST=<assembly,...>` (environment) or `--il-first <assembly,...>` (driver) drops, for the listed
shared-framework assemblies, the corelib shadows of every type the assembly defines: from the shadow index, and from the
"types csharpc implements" set that decides FrameworkIl eligibility (ADR 0013), so the assembly is compiled from the shared
framework's IL when the program uses it, and everything it needs from other assemblies follows the usual rules. The corelib
assembly and the framework index are keyed on the switch (another cached `CsharpcCoreLib.dll` per value). `System.Private.CoreLib`
and facades that define no types are refused with a diagnostic. Without the switch nothing changes: same corelib, same index,
same binaries. The Windows native-TLS mode (ADR 0022, `CSHARPC_WINDOWS_NATIVE_TLS`) is one use of the same mechanism, with
a hand-picked family of types instead of whole assemblies.

### The measurement

`tools/il-first/ab.sh <assembly> <sample>...` compiles each sample both ways at -O2 (shadow: the default; IL:
`CSHARPC_IL_FIRST=<assembly>`), runs both, compares stdout and the exit code with the recorded `.expected` (real .NET is the
oracle), and reports per sample and route: pass/fail, stripped binary size, csharpc compile time (wall and CPU, children
included), and run time (median of 5). It prints a Markdown table, the sums over the samples both routes pass, and the
blockers of the IL route: the distinct compiler diagnostics with the samples that hit each. CI runs of the same samples with
the switch exported (`ci/il-first-*` branches, `CI-Only:`) give the correctness column on every sample at once.

## Types of System.Private.CoreLib (2026-11-09): the slice is a build product

A library flips by dropping its shadow (above). A *type* of System.Private.CoreLib (`List<T>`, later `Dictionary`, `StringBuilder`...) cannot be
taken the same way: the reference pack and the implementation both define it, and its internals (fields, internal helpers) are not in the reference
pack. The mechanism (docs/21-ownership.md Part B, issues #191, #221 to #224, #252, #256):

1. `src/Csharpc/upstream-types.txt` lists the CoreLib types served from the framework's IL. A type is added in the PR that deletes its shadow.
2. After every build of the compiler (`CsharpcUpstreamSlice` in `Csharpc.csproj`, incremental: it runs again only when the list, the slicer or the
   framework's CoreLib changed) `tools/il-first/slicer` cuts what those types need out of the shared framework's `System.Private.CoreLib.dll`
   (the one the SDK that builds csharpc ships) into `CsharpcUpstream.dll` and writes `CsharpcUpstream.map`. Every type reached that the reference
   pack does not declare moves into the slice under `CsharpcCoreLib.Upstream.*`; references to types that stay outside re-point at the reference pack
   (the contract: members of those types that the reference pack lacks resolve through the corelib's shadows, #222); the primitives' implementations
   of CoreLib-internal interfaces travel in it (#221); `SR` texts are literals and the `UseSystemResourceKeys` switch is false, as ILLink substitutes.
3. Both files ship next to the compiler (build output, publish output, tool package). Every compilation reads the map (`Frontend/UpstreamSlice.cs`),
   adds the slice to its IL assemblies and maps the real type to the slice's, as it maps a type to its shadow. `CSHARPC_UPSTREAM_MAP` remains only
   for measuring a type with `slice.sh` before it joins the list; the build does not use it.
4. Mono.Cecil, which the slicer needs, comes from the `Microsoft.NET.ILLink.Tasks` package (a `PackageDownload` of the SDK's version, like crossgen2). It
   is a build-time tool dependency: the compiler never references it and nothing that ships contains it. The slice is cut from the build machine's CoreLib
   (platform-neutral for the types listed so far; a type that reaches OS code needs one per target OS). wasm32 programs use the same slice.

## Measurements (2026-11-08)

Shared 4-core VPS under load (other jobs ran during the runs), so wall clock is noisy; CPU seconds, sizes and the ratios of
sums are the numbers to read. -O2, x86-64 Linux, stripped binaries, .NET 11 RC2 framework, run time the median of 5.
"Correctness" is the CI run of every sample with the switch exported (`ci/il-first-*` branches, `CI-Only:`), compared with the
recordings made on real .NET; "speed" is a throughput program (`tools/il-first/bench/`, a checksum compared with real .NET), since the
samples are tiny and start-up dominated.

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision |
|---|---|---|---|---|---|---|
| System.Text.Json + System.Text.Encodings.Web (`System.Text.Json,System.Text.Encodings.Web`) | 24 of 37 at -O0 (26 of 26 chosen at -O2 too; was 17): writer, reader, document, encoders, converters, polymorphism, references; the rest stop at the closed-world `MakeGenericType` and framework attribute lists (round 2 below) | 1.89x over 6 samples (236 to 794 KB for the smallest, 622 to 874 for a writer program); 3.0x for the throughput program (286 to 872 KB) | 1.60x | 0.43x on the throughput program (675 to 293 ms: 2.3x faster); 0.60x over the 6 samples | 16 584 (`corelib/Json` without `ArrayBufferWriter.cs`, which is CoreLib surface); 948 more in `Frontend/JsonReflectionContexts*` once the serializer is IL | **No-go today, go when the serializer blockers are closed.** IL wins speed and exactness; size is the price (round 2 row below). |
| System.Text.RegularExpressions | 20 of 22 before the flip (the 2 failures were `[GeneratedRegex]`: csharpc's own generator called into the shadow `Regex`); after linking the SDK's generator every regex sample passes, `[GeneratedRegex]` ones included, plus a new NonBacktracking sample (1290) | 3.35x over 8 samples in the A/B (13.9 against 4.1 MB). Final, stripped -O2 x86-64: 1.43x (744 912 against 520 248 B) for `new Regex(pattern)` programs, 3.6x to 3.8x (1.90 to 2.01 MB against 0.53 MB) once a constructor or static method takes `RegexOptions`, which reaches the NonBacktracking engine (`SymbolicRegexRunnerFactory`, found with `CSHARPC_WHY_LOWERED`); a program using `[GeneratedRegex]` shrinks (415 504 against 548 792 B: the generated matcher replaces the interpreter) | 2.24x in the A/B; final: 0.85x to 2.1x per sample (153 against 75 CPU s for 290, 79 against 73 for 291, 68 against 80 for 305) | 0.71x on the throughput program (992 to 700 ms: 1.4x faster); start-up dominated samples tie | 17 174 (`corelib/Regex` 16 830, `Frontend/GeneratedRegex*.cs` 344); the flip PR's total against main is 17 406 deletions (135 insertions) with the ownership-map rows, the fuzzer's regex cases and docs | **Flipped (#206)**: correct and faster; the size cost is the NonBacktracking engine every options-taking `Regex` construction reaches (follow-up: a tighter closed world or constant propagation of `RegexOptions`). Budgets: `tests/size/regex.cs` 815 104 B and `regexoptions.cs` 1 929 216 B on linux-x86_64; programs that do not use Regex are byte-identical (hello 28 536, linq 164 552, collections 140 856, numbers 129 048, exceptions 36 920, typeinfo 43 792, async 156 096). |
| `System.Collections.Generic.List<T>` and its enumerator (`src/Csharpc/upstream-types.txt`, a CoreLib type: the first type-level flip) | all 184 samples that use `List<T>` or `CollectionsMarshal` pass at -O2 with the shadow gone (CI runs the whole suite on five platforms); the framework's own `Insert`/`RemoveAt` use `Array.Copy` where the shadow moved elements one by one | 1.00x to 1.05x: collections 140 856 to 143 400 B (+1.8 %), linq 164 552 to 170 424 (+3.6 %), a List sort program 131 056 to 137 664 (+5.0 %), a large List program 178 800 to 186 992 (+4.6 %), regex +4.4 %, hello/numbers/exceptions/typeinfo/async unchanged | 1.0x (3.5 to 3.6 s for a small program, 5.9 to 5.2 s for a large one: noise) | instructions, 1 M operations: `Add` 14.3 M to 19.0 M (**+33 %**, the framework's `Add` calls the `NoInlining` `AddWithResize`, so size and version stay in memory: #259), `RemoveAll` +14 %, `Sort` +1 %, indexer, `foreach`, LINQ, `IndexOf`/`Contains` equal (after #258: `Array.IndexOf` uses the runtime's vectorized search), `Insert(0, x)` 800 M to 40 M (**20x faster**) | 598 in `corelib` (`List.cs` 595: `ListImpl`, its enumerator and the old `CollectionsMarshalImpl`; `LinqSpans` loses its `ListImpl` fast path; 55 added: `CollectionsMarshal` over the framework's `List<T>` through `[UnsafeAccessor]`), against 617 deleted and 183 added over `src` and `tests` | **Flipped**: the first CoreLib type served from `System.Private.CoreLib`'s IL, shipped as a build product (`CsharpcUpstream.dll` and its map, cut by `tools/il-first/slicer` after every build of the compiler; Mono.Cecil is a build-time tool dependency only). The owner accepted the `Add` and `RemoveAll` costs and about +5 % size; #259 closes the `Add` gap in the compiler. |
| `System.Collections.Generic.Dictionary<TKey,TValue>`, `HashSet<T>` and `System.Runtime.InteropServices.CollectionsMarshal` (CoreLib types, #274) | the 116 samples that mention them (`Dictionary`, `HashSet`, `ToLookup`, `GroupBy`, `Distinct`, JSON, Linq, Roslyn library members...) pass at -O0 and -O2, 36 Int128/Vector128/BigInteger samples too, `tests/slicer.sh` (11 slice runs) passes; three new samples recorded from real .NET for what the flip exposed (1305: boxed `Index`/`Range` as keys and `object`, an `Int128` in a boxed struct and a `Dictionary<Int128,V>` enumerator boxed through `IEnumerable`, `Comparer<Int128>.Default` which threw `InvalidOperationException`; 1306: `Comparer` of string collections and `ReadOnlySpan<char>` alternate lookups; 1307: the missing-key and duplicate-key messages for 18 key types), found along the way: #277 (SortedDictionary duplicate-key message) | **+5 % to +14 % on programs that format a number**, because the corelib's culture caches are `Dictionary<string, ...>` and now carry the real `Resize`/`Remove`/enumerator code and tables: linux-x86_64 async 156 544 to 172 144 B (+10.0 %), collections 143 448 to 163 032 (+13.7 %), linq 174 600 to 189 864 (+8.7 %), numbers 129 096 to 143 912 (+11.5 %), regex 825 600 to 870 304 (+5.4 %), regexoptions 1 949 520 to 2 093 104 (+7.4 %); hello, exceptions, typeinfo unchanged. A 4-collection program (`Dictionary<string,int>`, `HashSet<int>`, `HashSet<string>`, `Dictionary<int,int>`) 145 056 to 155 744 B (+7.4 %). Without the two profile replacements below it was +46 % (212 368 B) | collections 3.92 to 4.08 s (+4 %), linq 4.59 to 4.95 s (+8 %) CPU | instructions, same program, work beyond start-up: `Dictionary<string,int>` set/get/remove 33.5 M to 33.9 M (**+1.4 %**), `HashSet<int>` add/contains 69.2 M to 67.4 M (-2.5 %), `HashSet<string>` 28.1 M to 31.0 M (**+10 %**), `Dictionary<int,int>` 93.7 M to 84.3 M (**-10 %**); the spike before the type-guarded default comparer (#269) was 21 to 31 % behind | 1 425 deleted and 71 added in `corelib` (`HashSet.cs` and `CollectionsMarshal.cs` deleted, `DictionaryImpl` out of `Dictionary.cs`; added: contract members in `UpstreamSupport.cs`), against 1 431 deleted and 218 added over `src` and `tools` (the compiler side: boxed `Index`/`Range` method tables, 8-aligned `Int128` aggregates, `Comparer<Int128>`, IL folding of `typeof(A) == typeof(B)`, generic forwarding in the slicer) | **Flipped**. Two things the flip decides: (1) the string comparer family stays redirected to the corelib, with a wrapper (`UpstreamWrappedDefaultStringComparer`) for the default comparer so alternate lookups and `Comparer` behave as .NET's; the randomized comparer upstream switches to after many collisions (hash-flooding mitigation) is not there, as it was not there for the shadows: not weaker than before, first thing to add when the string comparers are flipped. (2) The throw helpers that format the key with `string.Format` are replaced (`csharpc` profile) by direct text for `string`, `int`, `long`, `uint`, `ulong` and `char` keys and `ToString`/`IFormattable` for the rest, so a program reading a Dictionary does not carry composite formatting and every number formatter. The size cost is the owner's call under the standing authorisation; a follow-up could replace the culture caches' Dictionaries with something smaller. |
| System.ObjectModel + System.Collections.NonGeneric (`System.ObjectModel,System.Collections.NonGeneric`) | 5 of 5 samples byte-identical to real .NET in the A/B (1104 CollectionBase, 255, 610 ObservableCollection, 991, the throughput program); after the flip also at -O0 and -O2 with the TLS/X509 collection samples (1100, 1101, 1270, 1005), which derive from the real `CollectionBase`. Security: `ObservableCollection`'s reentrancy guard, `CollectionBase`'s `OnValidate`/`OnInsert` hooks and index checks are the framework's code now; checked by the exception-text and hook-order samples above (1104, 610) | 2.21x over the 5 samples (2 627 against 1 188 KB): a `CollectionBase` program 251 to 707 KB, an `ObservableCollection` one 253 to 824 KB, because the framework's `SR` resource lookups (argument-exception texts) reach `ResourceManager` and the culture machinery (about 300 KB; a follow-up for every IL library, #272); programs that use neither are unchanged (`tests/size` +400 B for async) | 1.75x (68 against 39 CPU s) | 0.70x on the throughput program (median of 5: 841 against 1 194 ms) | 580 (`corelib/ObservableCollection.cs` 396 and `CollectionBase.cs` 184) + the `PropertyChanged/ChangingEventArgs` shadows | **Flipped (#271)**, ObjectModel and NonGeneric only. `Delegate.HasSingleTarget` (internal; `ObservableCollection` tests its handler with it) is served by the runtime (`csrt_delegate_has_single_target`), the corelib's X509 certificate collections derive from the real `CollectionBase` (and the framework-IL choice always takes System.Collections.NonGeneric, since the program does not name the base class). **System.Collections and System.Collections.Concurrent stay shadows: blocked** (below). |

**System.Collections and System.Collections.Concurrent are blocked by facades that also define types** (#271). Both assemblies hold real code (Stack,
LinkedList, SortedDictionary, PriorityQueue, OrderedDictionary...; BlockingCollection, ConcurrentBag, ConcurrentStack, Partitioner) and forward other types
(`List<T>`, `Queue<T>`, `ConcurrentQueue<T>`...) to System.Private.CoreLib. Binding the program against the implementation instead of the reference-pack
twin sends every such forward to the CoreLib facade (`CoreLibFacade`), which forwards it to the twin's name, the implementation again: a cycle Roslyn
cannot resolve (`FrameworkIl.MarkFacades`: "forwards types its reference-pack twin defines"). Solutions, neither small: (a) a renamed copy of the
reference twin that holds the definitions the implementation forwards (a metadata rewrite of the assembly identity), or (b) cutting those types out of
their assembly with the slicer of #265 (it takes `--corelib System.Collections.dll`, `analyze` works: Stack is 5 types and 47 methods, but `emit` assumes
every reference resolves into one CoreLib module). Until one of them lands the shadows (`Collections.cs` without `Queue<T>`, `OrderedDictionary.cs`,
`StructuralComparisons.cs`, `Concurrent/*`) stay, named in the ownership map.

A/B of 2026-11-09 (round 2, `tools/il-first/ab.sh --runs 3`, -O2, loaded 4-core VPS, stripped), 8 programs both routes pass byte-identical to real .NET: the writer, reader,
document and node samples, two reflection-serializer samples (BCL-typed members, DateOnly/TimeOnly), the converters sample and the throughput program.
Sums IL / shadow: binary 26 953 / 8 102 KB (3.33x: 0.89 to 0.97 MB against 0.62 to 0.70 for writer/reader/document, 4.2 to 8.4 MB against 1.2 to 1.7 for
the programs that reach the reflection-based serializer), compile cpu 1 903 / 726 s (2.62x), compile wall 1 328 / 612 s (2.17x), run time of the samples
955 / 632 ms (1.51x, start-up dominated; the reflection serializer pays 90 ms against 16 for 1295 and 288 against 67 for 319 on one run each);
the throughput program 216 / 324 ms (0.67x: IL 1.5x faster) at 886 / 286 KB (3.1x). The serializer from IL is the expensive case: the reflection
resolver, the converters it reaches by `MakeGenericType` and the attributes it reads are compiled in, where the shadow had a precomputed table.

The size cost is real and is the part to decide: AGENTS.md allows a measured speed gain to cost size with the numbers in the commit
and the budget raised for that reason, and this is such a case. The levers to measure are general ones (nothing library-specific is
added): a tighter closed world, and propagating a constant argument such as `RegexOptions` into the framework's constructor so the
NonBacktracking branch is dead code.

### The blockers, with counts

A construct stops a method at the first unsupported thing, so each round of fixes uncovers the next ones. Round 0 of System.Text.Json
(samples blocked by each; the CI run of the 29 samples then matching the filter):

- fixed (CoreLib surface, so every IL assembly benefits): `Utf8Formatter.TryFormat` for double, float, decimal, Guid, TimeSpan; `Utf8Parser.TryParse` for
  double, float, decimal, bool, Guid, TimeSpan; `Base64.EncodeToUtf8`, `DecodeFromUtf8`, `DecodeFromUtf8InPlace`; `Utf8.IsValid`;
  `MemoryExtensions.Sort(keys, items)`, `LastIndexOfAnyExcept`; `CollectionExtensions.AddRange(List<T>, ReadOnlySpan<T>)`;
  UTF-8 `TryFormat` of Half, Int128, UInt128, Version; UTF-8 `TryParse` of Int128, UInt128, Version; `Dictionary` alternate lookups
  (18 samples) (#194, #201);
- fixed (IL front-end): `starg` on a by-ref argument (4 samples, `Marvin.ComputeHash32`); a constrained call of a generic static
  abstract method was lowered open (the only construct that stopped the Regex NonBacktracking engine) (#196);
- fixed (a shadow bug the IL route exposed): `ArrayBufferWriter<T>` growth dropped the bytes written past the committed index, which
  turned the first 220 bytes of any `Utf8JsonWriter(Stream)` output over 256 bytes into NULs (#194); `StringComparer.Ordinal`'s span
  `GetHashCode` recursed into itself (#201);
- open, explicit diagnostics (the serializer path, 14 samples each): the .NET 11 numeric structs `BFloat16`, `Decimal32`, `Decimal64` and
  `Decimal128` (named by the converters table through `INumberBase<T>` and `IUtf8SpanFormattable`; a layout-only shadow is not enough);
  `Activator.CreateInstance(Type, bool)`; `Assembly.GetCustomAttributesData()`; at run time, `Type.GetCustomAttributes` on CoreLib types
  (`NotSupportedException`, 5 samples: csharpc keeps reflection metadata only for types in `[DynamicallyAccessedMembers]` locations);
  `Half.TryParse(ReadOnlySpan<byte>)` (3);
- open, JSON encoder (#203): the System.Text.Encodings.Web shadow differs for lone surrogates (2 samples) when only System.Text.Json is
  from IL; with both from IL they are byte-identical, so the shadow goes away with the flip.

Round 2 (2026-11-09, #216, #218, #228, #229, #234, #237, #243): the numeric structs, `Activator`, `Assembly.GetCustomAttributesData()`,
`Half.TryParse` and `NullabilityInfoContext` (dotnet/runtime's algorithm over the kept `[Nullable]` attributes, with the attributes of return
values in the method records and the open generic definition's members) are done, as are the reflection queries the resolver makes of every
type of a graph (constructors of primitives, arrays, string and interfaces; `Type.GetCustomAttributes(inherit: true)` over class hierarchies,
worked out at compile time; a `Type` used as `ICustomAttributeProvider`; the untyped attribute list of a CoreLib type asked from the
framework's own IL answers with the attributes csharpc models) and an invalid-IR error for a missing struct constructor overload (#233).
With the switch, 24 of the 37 samples pass at -O0 (1009 1221 1253 1280 1283 1292 310 311 312 313 314 315 319 320 321 322 323 326 482 542
663 834 835 966), the two samples added for this work (1295, 1296) too, and all 26 at -O2; with `--via-il` (the program itself as IL) 22 of the 26 pass at
-O0: 319 (`Reflection of type JsonStringEnumConverter`1[]`, the open generic named by `[JsonConverter]`), 320 (the public constructors of a
`[JsonDerivedType]` type are not kept), 326 (`Exception.Source`) and 323 (the unsupported-root diagnostics are not raised for a program given as IL) fail
only there. Still open, in order of weight:
- the closed world: `MakeGenericType` of the framework's parameterized-constructor converters
  (`SmallObjectWithParameterizedConstructorConverter<T, TArg0..3>`, `LargeObject...WithReflection<T>`) over user types, which csharpc finds
  only if the program created the instantiation (1160, 316, 317, 535, 740, 1250, 1251); NativeAOT has the same limit for value-type arguments,
  so this wants a decision (root the instantiations from the serializer call's types, as `KeepJsonReflectionRoots` roots the members, or
  route reflection calls through the generated contexts);
- attribute lists of framework types csharpc neither describes nor can rule out: `Uri`, `SortedDictionary<,>` and `JsonElement`/`JsonNode`
  through `Attribute.GetCustomAttribute` (965, 972, 325, 318, 968, 971), and the unlisted attributes of a member (968, 971: records, nullable
  annotations): IL types of the framework assemblies should be described from their metadata like the program's types;
- `NullabilityInfoContext` stops explicitly on by-ref parameters and on members of open definitions typed `List<T>` or `T[]` (open
  instantiations have no Type objects).
Samples 323 and 535 encode the generated-metadata route's diagnostics for anonymous and file-local roots; the IL route reports the same.

The reflection items are the runtime-coupled part of the policy: they belong to the core csharpc keeps (metadata, `Type`, `Activator`), and
each closes for every IL-compiled assembly at once.

## Consequences

- This ADR changed no default; the first flip followed (#206): System.Text.RegularExpressions comes from the framework's IL, the SDK's regex
  generator is linked into csharpc, and the shadow, csharpc's generator and its hooks are deleted (every regex sample is a behavioural one
  and now tests the real engine, so none was removed). The next steps per library are flips of the same kind, each one PR that makes IL the
  default for that library, deletes its shadow and the samples that covered only the shadow (the lines are reported with
  `git diff --shortstat`), and removes its use of the switch: System.Text.Json and System.Text.Encodings.Web together after the
  reflection and numeric-struct items above.
- The order of #187 continues: System.Linq, System.Collections(.Concurrent), System.Net.Http and Sockets pieces, crypto/X.509/`SslStream`
  (Windows is the native-TLS mode of ADR 0022, one use of the same switch), System.Xml; SIMD (`Vector128/256` and the common hardware
  intrinsics to LLVM vectors) is the item real framework code needs to be competitive; the CoreLib-internals audit follows from the blocker
  lists.
- A library whose measured decision is *partial* keeps the named part of its shadow (the mixed-assembly mechanism of ADR 0013 and the
  partial set of `IlFirst` already carry that), with the reason in this table.
