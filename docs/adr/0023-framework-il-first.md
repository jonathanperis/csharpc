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
2. **Per library, by measurement** (priorities: speed, then security, then size): the shadow route and the IL route are
   built from the same samples; correctness against the recordings made on real .NET comes first, then run-time speed,
   then size and compile time. A library flips to IL where IL wins or ties; a shadow stays only where it is measurably
   smaller or faster, with the numbers in the table below.
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

## Measurements (2026-11-08)

Shared 4-core VPS under load (other jobs ran during the runs), so wall clock is noisy; CPU seconds, sizes and the ratios of
sums are the numbers to read. -O2, x86-64 Linux, stripped binaries, .NET 11 RC2 framework, run time the median of 5.
"Correctness" is the CI run of every sample with the switch exported (`ci/il-first-*` branches, `CI-Only:`), compared with the
recordings made on real .NET; "speed" is a throughput program (`tools/il-first/bench/`, a checksum compared with real .NET), since the
samples are tiny and start-up dominated.

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision |
|---|---|---|---|---|---|---|
| System.Text.Json + System.Text.Encodings.Web (`System.Text.Json,System.Text.Encodings.Web`) | 17 of 37: the whole writer, reader, document and encoder surface; the serializer family is blocked by explicit diagnostics (below) | 1.89x over 6 samples (236 to 794 KB for the smallest, 622 to 874 for a writer program); 3.0x for the throughput program (286 to 872 KB) | 1.60x | 0.43x on the throughput program (675 to 293 ms: 2.3x faster); 0.60x over the 6 samples | 16 584 (`corelib/Json` without `ArrayBufferWriter.cs`, which is CoreLib surface); 948 more in `Frontend/JsonReflectionContexts*` once the serializer is IL | **No-go today, go when the serializer blockers are closed.** IL wins speed and exactness; size is the price. |
| System.Text.RegularExpressions | 20 of 22 before the flip (the 2 failures were `[GeneratedRegex]`: csharpc's own generator called into the shadow `Regex`); after linking the SDK's generator every regex sample passes, `[GeneratedRegex]` ones included, plus a new NonBacktracking sample (1290) | 3.35x over 8 samples in the A/B (13.9 against 4.1 MB). Final, stripped -O2 x86-64: 1.43x (744 912 against 520 248 B) for `new Regex(pattern)` programs, 3.6x to 3.8x (1.90 to 2.01 MB against 0.53 MB) once a constructor or static method takes `RegexOptions`, which reaches the NonBacktracking engine (`SymbolicRegexRunnerFactory`, found with `CSHARPC_WHY_LOWERED`); a program using `[GeneratedRegex]` shrinks (415 504 against 548 792 B: the generated matcher replaces the interpreter) | 2.24x in the A/B; final: 0.85x to 2.1x per sample (153 against 75 CPU s for 290, 79 against 73 for 291, 68 against 80 for 305) | 0.71x on the throughput program (992 to 700 ms: 1.4x faster); start-up dominated samples tie | 17 174 (`corelib/Regex` 16 830, `Frontend/GeneratedRegex*.cs` 344); the flip PR's total against main is 17 406 deletions (135 insertions) with the ownership-map rows, the fuzzer's regex cases and docs | **Flipped (#206)**: correct and faster; the size cost is the NonBacktracking engine every options-taking `Regex` construction reaches (follow-up: a tighter closed world or constant propagation of `RegexOptions`). Budgets: `tests/size/regex.cs` 815 104 B and `regexoptions.cs` 1 929 216 B on linux-x86_64; programs that do not use Regex are byte-identical (hello 28 536, linq 164 552, collections 140 856, numbers 129 048, exceptions 36 920, typeinfo 43 792, async 156 096). |

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
