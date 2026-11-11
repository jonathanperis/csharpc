# ADR 0023 — Framework IL first: hand-implement the runtime-coupled core, compile the rest of the framework from its IL

- **Status:** Accepted as policy (2026-11-08, owner decision); the per-library decisions below are measured. System.Text.RegularExpressions
  is flipped (#206, owner: "flip now, accept the size"), System.Linq too (#267); no other default has changed yet. Issues #187 (policy) and #189 (this ADR).
  The order of further CoreLib flips and the NativeAOT column of the table below are set by
  [ADR 0025](0025-optimizer-before-flips.md) (compiler milestone first, NativeAOT as the yardstick).

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

1. `src/Csharpc/upstream-types/` lists the CoreLib types served from the framework's IL, one file per library or flip
   (`System.Collections.Generic.Dictionary.txt` holds ``Dictionary`2``, ``HashSet`1`` and `CollectionsMarshal`), so flips running in parallel never edit
   the same file. A type is added in the PR that deletes its shadow, in a new file named like the flip's `docs/il-first/<library>.md`.
2. After every build of the compiler (`CsharpcUpstreamSlice` in `Csharpc.csproj`, incremental: it runs again only when a list, the slicer or the
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

The per-library table (columns: library (switch); correctness, samples from IL; binary size, IL / shadow; compile cpu, IL / shadow;
speed, IL / shadow (time); lines removed on a flip; decision) has one file per library or CoreLib type in
[`docs/il-first/`](../il-first/): `docs/il-first/<library>.md` (an assembly name such as `System.Linq.md`, or the CoreLib type's full name
such as `System.Text.StringBuilder.md`) holds that library's row under the table's header. A flip adds its own file and edits no
other library's, so flips running in parallel do not conflict (ADR 0025, decision 6). `tools/il-first/table.sh` prints all the
rows as one Markdown table.

**Assemblies that define types and also forward CoreLib types (System.Collections, System.Collections.Concurrent)** needed one more piece of the IL
front-end (#275). The implementation forwards `List<T>`, `Queue<T>`, `ConcurrentQueue<T>`... to System.Private.CoreLib, and binding against it in place of
its reference-pack twin sent each forward through `CoreLibFacade` back to the twin's name, itself. The twin now stays as a renamed copy
(`Frontend/TwinReference.cs`): the reference assembly's Assembly row is pointed at the tail of its own name string ("Collections"), so no heap grows, the
types the implementation does not forward are made non-public in the copy, and the facade forwards the CoreLib types to the copy. One definition of
`List<T>` for the program, the implementation and every other IL assembly. Nothing in it is library-specific; the list of assemblies that use it
(`TwinReference.Supported`) is a validation list, as `MixedWithShadows` is.

**The texts of an IL library's exceptions as literals (#272).** Each shared-framework library has an internal `SR` class whose getters read its embedded
string resources (`internal static string Arg_X => GetResourceString("Arg_X");`). The framework-IL route used to compile every library's whole
`FxResources.X.SR` table into the binary (for a `CollectionBase` program 440 KB of the 707 KB: ResourceManager, culture and formatting code, plus the
strings; `System.ComponentModel.TypeConverter`'s table alone is 30 KB). A getter of exactly that IL shape (`ldstr key; (ldnull | ldstr default); call
SR.GetResourceString; ret`) is now lowered as `ret "<the text>"` (`ModuleCodeGen.ResourceLiterals.cs`), so a program carries the texts of the getters it
reaches (the slicer does the same for CoreLib: +12 KB instead of +120 KB), and the tables are compiled in only when a lowered method looks a text up by a key
it computes (a call of `SR.GetResourceString` that is not such a getter). Measured, stripped -O2: `tests/size` regex 891 472 to 817 080 B,
regexoptions 2 127 976 to 2 053 672 B (on the Dictionary/HashSet flip's main); the `CollectionBase` sample 707 to 269 KB (shadow 251), `ObservableCollection` 824 to 617 KB (shadow 253),
`TypeConverter` 3 700 to 2 546 KB (shadow 679), `Collections` exceptions 232 to 175 KB (shadow 223). The texts are the neutral-culture ones, as
before (csharpc is invariant); `ResourceManager` over the program's own resources is unchanged.

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
