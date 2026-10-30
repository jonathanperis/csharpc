# 18 — Real-world compatibility report

How much real C# code does csharpc compile and run exactly like .NET today, and which missing
features block the most programs? This is the first measurement for milestone M4 (real-world
validation, [06-roadmap.md](06-roadmap.md)). The differential samples and the conformance corpus
([09-conformance.md](09-conformance.md)) are written *for* csharpc. This corpus is not: it is
unmodified console programs from Microsoft's documentation and sample repositories.

Latest run of 2026-10-25: csharpc `parity-blockers` at `21556a7` (with `main`'s m8-kestrel merged) on the CI `Compatibility` workflow (hosted
`ubuntu-24.04` runner), .NET SDK `11.0.100-rc.2.26504.105`, Linux x86-64; every program is also published
with NativeAOT ([ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)). The first run (csharpc `integ` at
`8183dfb`, 2026-10-22) is the baseline the blocker tables below were measured on;
[Progress](#progress-since-the-first-run) lists what changed since.

## NativeAOT parity

The headline metric: of the programs NativeAOT (`dotnet publish -p:PublishAot=true`) builds and runs exactly
like the JIT, how many does csharpc match? See [Method](#method), step 5.

| | first parity run (`ac17a87` + harness) | `aot-parity` (`29cff62`) | `parity-blockers` (`21556a7`) | latest run (`ci/typeconverter-primitives`, `b46bff2`) |
|---|---:|---:|---:|---:|
| compared (not skipped under .NET) | 282 | 276 | 274 | 279 |
| NativeAOT-unsupported (out of the denominator) | 34 | 28 | 28 | 29 |
| **NativeAOT-working (the denominator)** | **248** | **248** | **246** | **250** |
| **parity: csharpc PASS among them** | **200 (80.6 %)** | **220 (88.7 %)** | **236 (95.9 %)** | **243 (97.2 %)** |
| csharpc COMPILE-UNSUPPORTED among them | 45 | 27 | 7 | 5 |
| csharpc COMPILE-ERROR among them | 3 | 1 | 1 | 1 |
| csharpc RUNTIME-DIFF / RUNTIME-CRASH / COMPILE-CRASH among them | 0 | 0 | 0 / 1 / 1 (both reflection, below) | 0 / 0 / 1 (expression trees) |

(The first parity run counted some clock-dependent programs as NativeAOT differences; the harness now
re-runs the JIT before blaming NativeAOT or csharpc, which moved a few programs to SKIPPED.)

- **97.2 % parity** (run 40424843703, 243 of 250), and no wrong output. The seven left: expression trees
  (2; one is the compile timeout), `dynamic` (1), C# 15 `union` declarations (2; csharpc's Roslyn predates
  them), `System.Data` (1) and deriving from `AssemblyLoadContext` (1). Earlier, **95.9 % parity**, and no wrong output: every NativeAOT-working program csharpc compiles and runs matches
  .NET. By corpus: dotnet/samples 43 of 44 (97.7 %), dotnet/docs 193 of 202 (95.5 %). (A run on `7860d73`,
  before the merge, measured 238 of 248, 96.0 %; the corpus' skip set moves a little between runs, as
  programs are re-checked for determinism under .NET.)
- **NativeAOT-unsupported, by reason** (28): crashes 15 (most are `System.Text.Json`'s reflection-based
  serializer, which NativeAOT disables; trim/AOT warnings `IL2026`/`IL3050`), output differs 11 (stack-trace
  lines: NativeAOT prints other frames and no file/line; exception messages), publish fails 2 (an XML
  documentation file NativeAOT's publish can't copy). **csharpc passes 21 of these 28** — programs that work
  with csharpc but not with NativeAOT. The one JIT-baseline RUNTIME-DIFF is among them and differs from
  NativeAOT the same way: `aesdecrypt` prints a `FileNotFoundException` without the BCL's internal frames
  (csharpc records frames of the program only, [ADR 0012](adr/0012-stack-traces.md)).
- 7 in-scope programs publish with trim/AOT warnings but run identically; they stay in scope (the run is
  the evidence that NativeAOT supports them).
- In scope, contrary to ADR 0016's expectation: NativeAOT runs the corpus' expression-tree programs
  (`lambda-expressions`, `ExpressionTreeExample`, interpreted) and `using-dynamic` identically to the JIT, so
  those count against csharpc's parity.
- **Faster and smaller than NativeAOT** (236 programs both handle): compile time median 5.7 s vs 7.1 s
  (csharpc faster on 166 of 236; the max is csharpc's, 111.7 s for a System.Xml program vs 24.3 s),
  executable size median 136 KiB vs 1,335 KiB (a tenth; smaller on 235 of 236; max 4,908 KiB vs
  7,515 KiB). Compile time is `csharpc App.csproj -c Release` against
  `dotnet publish -c Release -r linux-x64 -p:PublishAot=true`, wall clock, 4 programs in parallel on the
  runner; size is the executable alone (NativeAOT's symbols are in a separate `.dbg`).

**The 10 in-scope programs csharpc doesn't pass yet**, by what blocks them (programs; "only" = the sole
blocker). Everything else of the `aot-parity` table (XML, cryptography, text elements, compression,
`BufferedStream`, `IdnMapping`, `RuntimeInformation`, explicit layouts, synchronization contexts,
`Thread.Interrupt`, PLINQ) passes now ([Progress](#progress-since-the-first-run)).

| # | blocker | programs | only |
|---:|---|---:|---:|
| 1 | reflection / configuration binding (`ConfigurationIgnore` PASS on `ci/parity-typeconverter-bind` — TypeConverter/TypeDescriptor shadows, `Activator.CreateInstance(Type)`, `Type.GetInterfaces`, Bind DAM for concrete `object` args; `attributes` and `TracingConfiguration` pass on `ci/parity-hasflag-attrs`) | 0 | 0 |
| 2 | expression trees (compiled or interpreted `Expression<T>`; `ExpressionTreeExample` is an explicit `CSLLVM0001` on DynamicMethod / expression trees — the Nullable invoker NRE is fixed on `ci/parity-hasflag-attrs`) | 2 | 2 |
| 3 | `dynamic` (supported on `ci/dynamic`, [ADR 0019](adr/0019-dynamic.md): `using-dynamic` and `built-in-types` print what `dotnet run` prints; awaiting the compatibility run) | 1 | 1 |
| 4 | C# 15 unions (csharpc's Roslyn binds them differently from the SDK's) | 2 | 2 |
| 5 | `System.Data` (`DataTable`/`DataRow` through `System.Text.Json`) | 1 | 1 |
| 6 | `AssemblyLoadContext` (plugin unloading) | 1 | 1 |

Rows 1-3 are member reflection (ADR 0018, `main`'s m8 work, handled separately from these blockers), row 4
waits for a Roslyn with the SDK's union binding, and rows 5-6 are whole subsystems.

## Summary (JIT baseline, kept for continuity)

| | first run (`8183dfb`) | `36fd745` (2026-10-22) | `51ef64e` (2026-10-23) | `b7a36a6` (2026-10-24) | `29cff62` | latest run (`21556a7`) |
|---|---:|---:|---:|---:|---:|---:|
| corpus (candidate console programs) | 323 | 323 | 320 | 320 | 320 | 320 |
| skipped (non-deterministic or broken under .NET itself) | 40 | 43 | 40 | 40 | 44 | 46 |
| **compared** | **283** | **280** | **280** | **280** | **276** | **274** |
| **PASS** (byte-identical stdout, same exit code) | **115 (40.6 %)** | **147 (52.5 %)** | 182 (65.0 %) | 189 (67.5 %) | 238 (86.2 %) | **257 (93.8 %)** |
| COMPILE-UNSUPPORTED (explicit `CSLLVM0001/0002` diagnostic) | 135 (47.7 %) | 95 (33.9 %) | 88 (31.4 %) | 88 (31.4 %) | 36 (13.0 %) | 13 (4.7 %) |
| COMPILE-ERROR (compile fails some other way) | 22 (7.8 %) | 22 (7.9 %) | 3 (1.1 %) | 3 (1.1 %) | 2 (0.7 %) | 1 (0.4 %) |
| COMPILE-CRASH (internal compiler error, crash) | 3 (1.1 %) | 2 (0.7 %) | 0 | 0 | 0 | 1 (0.4 %) |
| RUNTIME-DIFF (wrong output or exit code) | 7 (2.5 %) | 11 (3.9 %) | 7 (2.5 %) | **0** | **0** | 1 (0.4 %; out of NativeAOT scope, see above) |
| RUNTIME-CRASH (native binary crashed, .NET didn't) | 1 (0.4 %) | 1 (0.4 %) | 0 | 0 | 0 | 1 (0.4 %; reflection, see above) |

- **Latest run (`21556a7`): 93.8 %** of all compared programs pass; 257 of the 259 programs csharpc
  compiles behave exactly like .NET (the other two: the NativeAOT-unsupported `aesdecrypt` above, and
  `attributes`, which stops with an explicit `NotSupportedException` from the reflection metadata).
- At `29cff62`: 86.2 % of all compared programs pass, and all 238 programs csharpc compiles
  behave exactly like .NET. The gain over `b7a36a6` comes from `main`'s `DateTimeOffset`/`MemoryPool`/
  `Convert` and m8-harness work and this branch's fixes ([Progress](#progress-since-the-first-run)).
- At `b7a36a6` (2026-10-24): 67.5 %, and **every program csharpc compiles (189 of 189) behaves exactly like
  .NET**: no wrong output, no crash. By corpus: dotnet/samples 40 of 45 (88.9 %), dotnet/docs 149 of
  235 (63.4 %). The seven differences of the previous run were all missing stack-trace lines; csharpc
  now records .NET's stack traces ([ADR 0012](adr/0012-stack-traces.md)). Of the 3 remaining
  COMPILE-ERRORs, two use `Microsoft.Extensions.Configuration` types csharpc's references don't
  resolve yet, and one uses C#'s new union types, which csharpc's Roslyn binds differently from the
  SDK's compiler. Top remaining blockers: `CultureInfo` (9 programs; globalization in progress),
  `MemoryPool<T>` (5), `DateTimeOffset` (5), `Task` members (4). Compile time per passing program:
  median 6.7 s, max 36.5 s.
- At `51ef64e` (2026-10-23): 65.0 %; 182 of the 189 compiled programs matched, the other 7 printed
  stack traces. The three scheduling-dependent AsyncBreakfast variants left the corpus (`excluded.tsv`).
- At `36fd745`: **52.5 %** of the compared programs pass (first run: 40.6 %). By corpus: dotnet/samples 38 of 45
  (84.4 %), dotnet/docs 109 of 235 (46.4 %). The docs snippets exercise far more of the BCL per
  program.
- csharpc compiled 159 programs, and **147 (92.5 %)** of them behave exactly like .NET. The other
  12 are the bugs listed below. No new kind of bug appeared: the four additional RUNTIME-DIFFs are
  programs that compile now and print `Exception.ToString()` (the known missing stack-trace lines),
  and the additional COMPILE-CRASH is the known extension-block call crash, reached now that
  `Enum.GetValues(Type)` compiles.
- The skipped count moves between runs by a few programs: they are non-deterministic under .NET
  (thread scheduling) and sometimes produce the same output twice.
- In the first run, 135 of the 160 compile failures were the intended outcome for unsupported code:
  an explicit diagnostic naming the construct or API. The blocking surface is a long tail. In 83 of
  the 135 programs, a single type or construct was the only blocker; in 97, a single area (one
  namespace, or "language").
- Compile time per passing program, including MSBuild evaluation, `-O2` and linking: median 7.9 s,
  max 20.8 s in the latest run (first run, on a much more loaded machine: median 19 s, max 48 s).

## Method

**Corpus.** Two official Microsoft repositories, pinned by commit and downloaded as tarballs
(`tools/compat/fetch.sh`):

| repository | commit | directories searched |
|---|---|---|
| [dotnet/samples](https://github.com/dotnet/samples) | `ab42c8a4251cb90787aa5d3c94b78d28d85bc46b` | `core/console-apps`, `core/linq`, `core/tutorials`, `csharp` |
| [dotnet/docs](https://github.com/dotnet/docs) | `705acf6d28963fdb60e49ddd1c5437b3e0419a66` | `docs/csharp`, `docs/standard`, `docs/fundamentals`, `docs/core` |

`tools/compat/discover.py` statically selects every SDK-style project (`Microsoft.NET.Sdk`) with
`OutputType` `Exe` that targets .NET 5 or later. It excludes the following:

- NuGet `PackageReference`s (130 projects). csharpc compiles from source only, so these are out of
  scope until the IL front-end lands (M5).
- Libraries (60).
- Worker, Web and WindowsDesktop SDKs.
- GUI and Windows-only projects.
- .NET Framework targets.
- Programs whose sources use networking (20), hosting/ASP.NET, or process/assembly loading.

The result is 323 candidate programs: 268 from dotnet/docs and 55 from dotnet/samples.
`tools/compat/programs.tsv` lists each with its source URL at the pinned commit.
`tools/compat/excluded.tsv` lists every rejected project with the reason.

**Retargeting.** Only the .NET 11 runtime is installed, and a global `-p:TargetFramework` breaks
restore. So the corpus is copied to a work tree, and every `<TargetFramework(s)>` is rewritten to
`net11.0`. Nothing else in the programs changes.

**Per program** (`tools/compat/run.sh`, at most `-j 2`):

1. Real .NET: `dotnet build -c Release`, then run the program twice with `dotnet app.dll`. Both
   runs use invariant globalization, `TZ=UTC`, stdin from `/dev/null`, a 30 s timeout, and a fresh
   copy of the project directory as the working directory. The path is the same for every run, so
   programs that read data files or print paths compare equal.
2. A program is **SKIPPED** in any of these cases:
   - The two .NET runs differ in stdout or exit code (30 programs: `Random`, `Guid.NewGuid`, clocks,
     timing, thread scheduling, hash codes, addresses).
   - It times out under .NET (5 programs, all interactive menus).
   - It doesn't build under .NET 11 (4 programs, all broken upstream: removed
     `AppDomain.DefineDynamicAssembly`, missing `AllowUnsafeBlocks`, an accessibility error).
   - .NET throws `PlatformNotSupportedException` (1 program).
3. csharpc: `csharpc App.csproj -c Release -o app` (the same configuration as the .NET build; `-O2`), with a
   600 s timeout. The result is classified as follows:
   - **COMPILE-CRASH**: a timeout, a signal, exit 70, `CSLLVM9999`, or a managed stack trace.
   - **COMPILE-UNSUPPORTED**: a `CSLLVM0001`, `CSLLVM0002` or `CSLLVM0005` diagnostic.
   - **COMPILE-ERROR**: any other failure.
4. The native binary runs in the same environment. The result is one of the following:
   - **PASS**: stdout is identical and the exit code matches.
   - **RUNTIME-CRASH**: a signal or timeout that .NET didn't have.
   - **RUNTIME-DIFF**: anything else that differs.
5. NativeAOT ([ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)): after the csharpc step (so its
   RID-specific restore can't change what csharpc evaluates), `dotnet publish -c Release -r <host rid>
   -p:PublishAot=true` with the program's own project properties (only `PublishAot`, the RID and the same
   warning overrides as the .NET build are forced), then the native binary runs twice in the same
   environment. The program is **NATIVEAOT-UNSUPPORTED**, with the reason recorded, when the publish fails,
   the binary crashes or times out, its two runs differ, or its stdout or exit code differs from the
   JIT's; otherwise it is **NATIVEAOT-OK**. Trim/AOT analysis warnings (`IL2xxx`/`IL3xxx`) are recorded
   but don't exclude a program whose binary behaves exactly like the JIT: the run is the evidence.
   The NativeAOT publish time and executable size (without the separate `.dbg` file) are recorded next to
   csharpc's.
6. When csharpc's or NativeAOT's output differs, the JIT runs a third time; if that run differs from the
   first, the program is SKIPPED as non-deterministic (two runs can agree by chance, e.g. a clock printed
   to the minute).

**Parity** is csharpc PASS among the NativeAOT-OK programs; it is the headline number. The JIT-based
pass rate (PASS among all compared programs) is kept for continuity.

`tools/compat/analyze.py` aggregates the results. It parses every unsupported diagnostic in each
program's compile log, and normalizes it to its kind, its owning type or construct, its area
(namespace), and its member. It then ranks each of these by the number of programs it blocks.
`tools/compat/results.tsv` is the per-program result of this run.

## Top blockers

Measured on the first run (rows 1, 3, 4, 6, 9, 12, 13, 15 and 16 are largely done since, see
[Progress](#progress-since-the-first-run)). Counted in programs; numbers in parentheses are programs too. A program counts once per row, and
many programs hit several rows. "Only"
means the programs for which this row is the sole blocker, so implementing the row alone would
let them compile.

| # | blocker | programs | only | details | where to implement |
|---:|---|---:|---:|---|---|
| 1 | **`System.Text.Json`** | 20 | 15 | `JsonSerializer.Serialize/Deserialize` (11), `new JsonSerializerOptions` (8), `Utf8JsonReader/Writer`, `JsonDocument`, `JsonConverter<T>` subclasses | corelib C# (large: reflection-free serializer driven by compile-time type info, like the System.Text.Json source generator) |
| 2 | **`<StartupObject>` ignored** | 18 | 18* | projects with several `Main` methods pick one with `StartupObject`. csharpc ignores the property and reports `CSLLVM0003 program has no entry point` | Driver `ProjectLoader` (read the property) + Frontend (`CSharpCompilationOptions.WithMainTypeName`), small |
| 3 | **deriving from a BCL class** | 14 | 2 | `EventArgs` (4), `Attribute` (3), `JsonConverter<T>` (3), `SynchronizationContext`, `ExpressionVisitor`, `AssemblyLoadContext`, `JsonSerializerContext` | corelib shadows for `EventArgs`/`Attribute` first (trivial classes); codegen for library base classes in general |
| 4 | **`System.String` members** | 11 | 3 | `Split(ReadOnlySpan<char>)` (2), `StartsWith(char)`/`EndsWith(char)`, `StartsWith(string, StringComparison)`, `EndsWith(string, StringComparison)`, `Split(string[], options)`, `Split(char, int, options)`, `LastIndexOf(string)`, `IndexOf(char, StringComparison)`, `CopyTo(Span<char>)`, `string.Create`, `EnumerateRunes` | corelib C# (`String` shadow) |
| 5 | **spans, memory and `System.Buffers`** | 11 | 7 | `MemoryPool<char>.Rent` (5), formatting `Memory<char>` (3)/`ReadOnlySpan<char>` in interpolation, `MemoryExtensions.SequenceEqual/Contains` with comparer, `SearchValues`, `Utf8Formatter` | corelib C# |
| 6 | **reflection-lite** | 10 | 5 | `Enum.GetValues(Type)` (3), `ValueType.ToString()` on structs (2), `Type.GetMethod/GetProperty/GetProperties`, `Activator.CreateInstance(Type)`, `Assembly.Location` | codegen metadata (`Enum.GetValues`, `ValueType.ToString` are statically resolvable). The rest need trimming-style metadata (docs/08 gap 4) |
| 7 | **`System.Globalization`** | 10 | 3 | `CultureInfo.Name` (3), `new CultureInfo(..)`, `GetCultureInfo`, setting `CurrentCulture`, `IdnMapping` | done on `globalization` (port of .NET's ICU-based globalization, [19](19-globalization.md)): cultures, `CultureInfo`/`RegionInfo`, culture formatting/parsing, collation and casing; `IdnMapping` remains |
| 8 | **`System.Threading`** | 10 | 3 | `Task.Run(Action, CancellationToken)`, `Task.Wait(int)`, `WaitAsync`, `ContinueWith`, `Task.Id`, `new CancellationTokenSource`, `CreateLinkedTokenSource`, `SynchronizationContext`, `Timer`, `Thread.Interrupt` | corelib C# (tasks) + runtime (timers) |
| 9 | **`Console` input and encoding** | 9 | 7 | `Console.ReadKey()` (6), `ConsoleKeyInfo`, setting `Console.OutputEncoding` (2) | runtime + corelib: `ReadKey` with redirected stdin must throw `InvalidOperationException` like .NET. `OutputEncoding` = UTF-8 can be a no-op |
| 10 | **`System.Text.RegularExpressions`** | 9 | 3 | `MatchCollection` enumeration and `Count` (4), `Capture.Value`, `new Regex`, `Regex.IsMatch/Match/Replace` | done on `regex` (port of .NET's engine): 5 more programs pass; the others stop at `ThreadLocal<T>`, `IdnMapping`, `Convert.ToUInt16(char)`, formatting `ReadOnlySpan<char>`, `DateTimeOffset`, `JsonConverter<T>` |
| 11 | **non-generic and concurrent collections** | 8 | 2 | `Hashtable`, `ArrayList`, `ReadOnlyCollection<T>`, `BlockingCollection<T>`, `ConcurrentDictionary` members, `OrderedDictionary<,>.TryAdd` | corelib C# |
| 12 | **`StringBuilder.Append($"...")`** | 8 | 6 | `StringBuilder.AppendInterpolatedStringHandler` (7), `DefaultInterpolatedStringHandler` (1) | codegen (lower the handler pattern like plain interpolation) or a corelib shadow struct. High value: 6 programs need nothing else |
| 13 | **`DateTime.Parse`, `DateOnly`, `TimeOnly`** | 7 | 4 | `DateTime.Parse(string)` (5), `DateOnly` (2), `TimeOnly` | done on `globalization`: .NET's `DateTimeParse` / `DateTimeFormat` with the culture's `DateTimeFormatInfo`, for `DateTime`, `DateOnly` and `TimeOnly` |
| 14 | **`System.IO` extras** | 7 | 5 | `BufferedStream`, `FileSystemInfo.CreationTime`, `Stream.WriteAsync`, `File.WriteAllTextAsync`, `ZipArchive`, `BrotliStream`, tar | corelib/IO C# over libc ([16-interop-io.md](16-interop-io.md)) |
| 15 | **argument throw helpers** | 7 | 3 | `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`, `Exception.StackTrace`, `GetBaseException` | corelib C#, trivial |
| 16 | **deconstruction into properties** | 6 | 4 | `(X, Y) = (x, y)` where the targets are properties (the common constructor idiom). `CSLLVM0001 deconstruction other than (a, b, ...) = (x, y, ...)` | codegen partial (`FunctionCodeGen.Deconstruction`) |

\* These programs stop at the missing entry point, so later blockers are not visible yet.

Smaller rows, at 4 programs or fewer: `new Random` (4), `System.Xml.Linq` (4),
`Debug.Assert(bool, string)` (3), type tests against tuples (3), `System.Linq.Expressions`
(1 program, 13 members), `dynamic` (2), list patterns on `List<T>` (2), `Guid` (2), `Uri` (2),
`Aes`/`SHA256`. For the full tables (by kind, area, type and member), run the harness or
`python3 -I tools/compat/analyze.py /tmp/compat-work`.

Other COMPILE-ERROR causes:

| cause | programs | note |
|---|---:|---|
| `<StartupObject>` ignored | 18 | row 2 above |
| `<FrameworkReference Include="Microsoft.AspNetCore.App">` ignored | 2 | console programs using `Microsoft.Extensions.Configuration`. Supported since the `m8-harness` branch: the ASP.NET Core assemblies a program uses are compiled from the shared framework's IL ([ASP.NET Core and the TechEmpower app](#aspnet-core-and-the-techempower-app)) |
| C# 15 union types | 1 | Roslyn binding error `CS0117` (the union feature is bound differently from the SDK's compiler), see also COMPILE-CRASH |
| `ProjectReference` folding changes C# semantics | 1 | see the bug list |

## Progress since the first run

Step 3 of the recommendations (small corelib gaps), one commit each on `libgaps`, each with a
regression sample (`tests/samples/250`–`258`). 32 programs that failed in the first run pass now.

| gap (blocker row) | done | sample |
|---|---|---|
| `StringBuilder.Append($"...")` (12) | already supported by the conformance2 merge; pinned by a sample | 250 |
| `Console.ReadKey` (9) | `ReadKey`/`KeyAvailable` throw .NET's `InvalidOperationException` when stdin is redirected (raw-mode key reading on a terminal), `ConsoleKeyInfo`, `IsInputRedirected`/…, `Clear`, UTF-8 `OutputEncoding`/`InputEncoding` (other encodings throw `NotSupportedException`) | 251 |
| argument throw helpers (15) | `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty/WhiteSpace`, all `ArgumentOutOfRangeException.ThrowIf*`, `ObjectDisposedException.ThrowIf`, with .NET's messages; fixed the dropped `Object name: 'X'.` line of `ObjectDisposedException` | 252 |
| `System.String` members (4) | `Split(params ReadOnlySpan<char>)` and the `string[]`/count overloads, windowed `IndexOf`/`LastIndexOf`, `IndexOf(char, StringComparison)`, `Replace(…, StringComparison)`, `Compare`/`CompareOrdinal` on substrings, `CopyTo(Span<char>)`, `string.Create` | 253 |
| reflection-lite (6) | `Enum.GetValues/GetNames/IsDefined/Parse/GetUnderlyingType(typeof(E))`, enum `D`/`X`/`F` formats (they printed the name: a silent bug), boxed enums as `IFormattable` with .NET's hash codes, `ValueType.ToString/Equals/GetHashCode` | 254 |
| deriving from a BCL class (3) | `EventArgs` (+`Empty`), `Attribute`, `PropertyChangedEventArgs`, `CancelEventArgs`; field-like event accessors lowered for interface maps; events of library interfaces | 255 |
| deconstruction into properties (16) | properties, indexers and get-only auto-properties as deconstruction targets, in C#'s evaluation order | 256 |
| `DateTime.Parse`, `DateOnly`, `TimeOnly` (13) | `ParseExact`/`TryParseExact` (custom and standard formats, styles, offsets, Kind); `Parse`/`TryParse` for the common shapes, `NotSupportedException` for the rest of .NET's lenient grammar; `DateOnly`/`TimeOnly` structs | 257 |
| (found on the way) | `val as int?` threw `InvalidCastException` on a box of another type; `Nullable<T>.Value` in generic code with `T = bool` crashed the compiler | 258 |

**`System.Text.Json` (row 1)** — ported to the corelib on the `json` branch ([12-corelib.md](12-corelib.md#systemtextjson),
samples `tests/samples/310`–`326`). Re-run (before the `regex` merge) of the 25 programs matching `json` or `whats-new-dotnet-10`
(`tools/compat/run.sh json whats-new-dotnet-10`): **10 pass** (none did in the first run), none
differs from .NET. The datetime `JsonSerializer`/`Utf8JsonReader` snippets, `nullable-annotations`,
`required-properties`, `populate-properties`, `converters/how-to` and `source-generation` pass.
The 14 others stop at explicit diagnostics: `DateTimeOffset` (4 programs), `Debug.Assert(bool, string)`
(3), `System.Text.Json.Schema` (2), `DateTime.ToString(string, IFormatProvider)`,
`MemberInfo.IsDefined`, `StringComparer.Create(CultureInfo, …)`, `Regex` and, for the JSON API itself,
anonymous types and a `file`-local type with the reflection-style serializer.

**M2 blockers (`datetimeoffset` branch, samples `tests/samples/480`–`487`).** Re-run of the programs they blocked
(`tools/compat/run.sh`, filters `system-text-json-support`, `how-to-display-milliseconds`, `how-to-6-0`,
`memory-and-spans`, `linq-equality`): the 5 `DateTimeOffset` programs (`DateTimeCode`, the three
`system-text-json-support` `JsonDocument`/`Utf8JsonReader` snippets, `how-to-6-0`), the 5 `MemoryPool<T>`
programs (`memory-and-spans/usage-guidelines` `owner`, `owner-using`, `task-returning`, `task-returning-async`,
`void-returning`), `ownerless` (formatting `Memory<char>`) and `linq/equality` (`SequenceEqual` with a comparer)
pass. Along the way: `DateTimeOffset` in `System.Text.Json` (reader, `JsonElement`, writer, the serializer's
converter, nodes); `MemoryExtensions` compared the bytes of strings, floating-point numbers and structs (a silent
difference: equal strings at different addresses didn't match); `Convert` (port; some inline conversions threw
other exceptions than .NET's); `Task.Wait` with timeouts and tokens, `WaitAsync`; a user
`extern` method without `[DllImport]` (`void-returning` declares one it never calls) crashed the compiler with
invalid IR and now throws .NET's `BadImageFormatException` when called. Still open from these rows: `Task.Id`,
`Task.Status`, `Uri`, `System.Xml.Linq`.

Still open from step 3: `new Random(seed)` (2 programs) and `Debug.Assert(bool, string)` (3).
`Array.GetEnumerator()` (non-generic `foreach` over an `Array` returned by `Enum.GetValues`)
needs the element type at run time and stays a diagnostic.

**NativeAOT-parity blockers (`aot-parity` branch, samples `tests/samples/490`–`499`, `513`–`524`).** The first
parity run (80.6 %) ranked the in-scope blockers; fixed since, each with a regression sample recorded from
`dotnet run`:

| fix | programs | sample |
|---|---|---|
| boxed `decimal`/`DateTime`/`TimeSpan` as `IFormattable`/`IComparable`/`IComparable<T>`/`IEquatable<T>` (`o is IFormattable` was silently false) | (silent-difference risk) | 490 |
| `Task.Status` (exact: WaitingForActivation / WaitingToRun / Running for runtime and corelib-started tasks) and `Task.Id`; the runtime task gains `id` and status flags | — | 491 |
| `Convert`'s object/`IConvertible` overloads, `GetTypeCode`, `ChangeType`; `decimal.ToByte` & co. | — | 492 |
| boxed enums as `IComparable`; `Comparer<object>.Default` as .NET's `ObjectComparer` (`List<object>.Sort()` crashed) | — | 493 |
| small-integer `CompareTo` returns the difference (`(byte)200` vs `0` is 200), in every form — a silent difference | — | 494 |
| `TaskExtensions.Unwrap`, `ConfigureAwait(IAsyncDisposable)` | `ContinueWith-comparison`, `dispose-async` | 495 |
| async `TextReader`/`TextWriter` members, `File` token overloads, `StartsWith(string, bool, CultureInfo)`, `Index`/`Range` formatting | `FileReader`, `consoleasync`, `turkish` | 496 |
| `System.Uri` (simple absolute URIs; anything else is `NotSupportedException`) | `explicitargs` | 497 |
| `UriFormatException`'s message and inner exception were dropped (its parameters are `textString`, `e`) — a silent difference | — | 498 |
| partial properties with `field` / semi-auto accessors | `PartialClassesAndMethods` | 499 |
| `CancellationTokenSource(TimeSpan)`, `CancelAfter(TimeSpan)` | `CoalescingCancellationTokensFromTimeouts` | 513 |
| list patterns on `List<T>` and other countable classes; `new Index(..)`, `Index`/`Range.Equals` | `patterns`, `RangesIndexes` | 514 |
| `Stream` async members, `FileStream(..., useAsync)` | `file-access` | 515 |
| `ArrayList`; arrays' non-generic `ICollection.CopyTo` (a null slot: crashed) | `HowToCollections` | 516 |
| `System.Threading.Timer` | `AsyncWrappersForSync` | 517 |
| `Hashtable` (ported bucket layout: .NET's enumeration order), `CreationTime` per OS | `indirect1`, `HowToFilesAndDirectories` | 518 |
| `SearchValues` (bytes, chars, strings) | `everythingelse` | 519 |
| fixed-size buffers in C# source; function pointers in generic code | `UnsafeCodePointers` | 520 |
| `OrderedDictionary<TKey, TValue>` | `whats-new/dotnet-10` | 521 |
| `AggregateException` keeps every inner exception (`ToString()` dropped the `(Inner Exception #i)` parts — a silent difference), `InnerExceptions`, `Handle` | — | 522 |
| `Task.WaitAll` / `WaitAny` | — | 523 |
| `ExceptBy`/`IntersectBy`/`UnionBy`, three-way `Zip`; IL tuple creation with a `bool` element (invalid IR) | — | 524 |

**NativeAOT-parity blockers, second round (`parity-blockers` branch, samples `tests/samples/530`–`555`).**
88.7 % → 95.9 %. Each fix has a regression sample recorded from `dotnet run`:

| fix | programs | sample |
|---|---|---|
| `Environment.Version`, `RuntimeInformation`, the SDK's assembly attributes | — | 530 |
| `BufferedStream` (port) | `BufferedStreamWriteByteFlush` | 531 |
| `IdnMapping` (port; the OS's ICU on each platform) | — | 532, 533 |
| `Thread.Interrupt` (and an interruptible `Thread.Join` on Windows, as there) | `InterruptThread` | 534 |
| unreachable unsupported source methods compile as stubs, like IL methods | — | 535 |
| `[StructLayout(LayoutKind.Explicit)]` / `[FieldOffset]` | — | 536 |
| `System.IO.Compression` (`BrotliStream`, `ZipArchive`, `ZipFile`) and `System.Formats.Tar` from IL, over .NET's native shims | `zip-tar-best-practices` | 537, 540 |
| `Aes`, `CryptoStream`, `RandomNumberGenerator`, HMAC, PBKDF2 over the OS's crypto library | `aesencrypt` | 538 |
| exceptions overriding `GetObjectData` | — | 539 |
| `StringInfo`, `TextElementEnumerator`, `Rune` text elements | `RuneSamples` | 541 |
| `Utf8Parser`/`Utf8Formatter` for `DateTime`/`DateTimeOffset` | — | 542 |
| `SynchronizationContext` (derivable, posting continuations), `ExecutionContext.Capture/Run`, `BlockingCollection<T>` | `ExecutionContextAndSyncContext`, `SyncContextConsoleApps` | 543, 544 |
| `Encoding` rebuilt on .NET's model: fallbacks, encoders/decoders, derived encodings | — | 545 |
| `System.Xml` / `System.Xml.Linq` from IL (an in-memory CoreLib facade for IL that names `System.Private.CoreLib`; System.Xml's http downloads are a CSLLVM0008 boundary) | the 4 `XElement`/`XDocument` programs | 546, 547 |
| `Trace`/`Debug` listeners and providers | — | 548 |
| `AsyncLocal<T>` on the execution-context flow | — | 549 |
| `Partitioner`, `Barrier`, `Parallel.ForEach` over enumerables, `AggregateException.Flatten` | `ParallelGrep` | 550 |
| created tasks (`new Task`, `Start`, `RunSynchronously` — which waits for attached children), `Task.CurrentId` | `RaceConditionExample` | 551 |
| PLINQ from IL: generic virtual methods overridden in generic classes (dispatched over every instantiation), attached child tasks, `TrySetException(IEnumerable)` | `samples/parallel/PLINQ` (out of scope: NativeAOT differs) | 552, 324 |
| `RSA` over OpenSSL on Linux (key generation, `RSAParameters`, PKCS#1/PSS signatures, PKCS#1/OAEP encryption, the signature formatters) | `cryptographic-signatures` | 553 |

Along the way: `Monitor.Wait` lost a pulse that met its interrupt-poll timeout (a rare hang of
`SemaphoreSlim`/`BlockingCollection` producers, sample 555); `Task.RunSynchronously` now waits for attached
children (PLINQ's merge disposed its barriers early); event waits on WebAssembly run queued work; P/Invoke
`bool` parameters into the C runtime trapped on WebAssembly; `UnobservedTaskExceptionEventArgs` and
`GC.WaitForPendingFinalizers` (sample 554). Still open in this area: RSA (and Aes) on macOS and Windows,
which need Security.framework and CNG.

## Bugs: wrong output and crashes

Each bug has a minimal reproducer in `tools/compat/repros/`. Run `tools/compat/repro.sh FILE.cs`,
which runs the file under `dotnet run` and under csharpc and diffs the results. The **status**
column shows what changed after the report (branch `compat-fixes`, regression samples
`tests/samples/220`–`229`, project tests in `tests/projects/cli`).

### RUNTIME-DIFF / RUNTIME-CRASH (silent differences — the worst category)

| repro | found in | bug | status |
|---|---|---|---|
| [`oftype_object_array.cs`](../tools/compat/repros/oftype_object_array.cs) | samples `core/linq/csharp/conversion` (RUNTIME-CRASH) | Arrays don't implement non-generic `System.Collections.IEnumerable` at run time. `o is IEnumerable` is silently `False`, and a cast throws `InvalidCastException`, so `OfType<T>()`/`Cast<T>()` on arrays crash | fixed: arrays get exact per-element-type interface maps, including non-generic `IEnumerable`/`IList` (sample 224) |
| [`nan_constant_pattern.cs`](../tools/compat/repros/nan_constant_pattern.cs) | docs `language-reference/statements/{jump,selection}-statements` (2 programs) | `case double.NaN:` / `is float.NaN` never matches. Patterns use `Equals` semantics, but csharpc uses ordered `fcmp oeq` | fixed (sample 223) |
| [`tfm_define_constants.cs`](../tools/compat/repros/tfm_define_constants.cs) | docs `fundamentals/program-structure/preprocessor-directives` | `NET`, `NET11_0`, `NET10_0_OR_GREATER`, `NETCOREAPP` … are not defined. The SDK adds them in a build target, after the evaluation that csharpc reads, so `#if` silently picks another branch | fixed: projects run the SDK's `AddImplicitDefineConstants` target, and single files get the same set plus `TRACE` and `DEBUG`/`RELEASE` (sample 220, `tests/projects/cli/TfmDefines`) |
| [`static_ctor_eager.cs`](../tools/compat/repros/static_ctor_eager.cs) | docs `programming-guide/classes-and-structs/constructors` | Static constructors run eagerly at startup. .NET runs them on first use of the type, and never for an unused type, so output appears early or spuriously | fixed: lazy initialization with CoreCLR's triggers, `TypeInitializationException` (samples 227, 228) |
| [`ordinal_ignore_case_non_ascii.cs`](../tools/compat/repros/ordinal_ignore_case_non_ascii.cs) | docs `standard/base-types/best-practices-strings/comparison2` | `StringComparison.OrdinalIgnoreCase` folds only ASCII. Cyrillic, Greek and Latin-1 case variants compare unequal (`Compare` returns 32 instead of 0) | fixed: full simple case mapping, including surrogate pairs (sample 225) |
| [`array_copy_negative_length_message.cs`](../tools/compat/repros/array_copy_negative_length_message.cs) | docs `standard/exceptions/how-to-use-finally-blocks` | `Array.Copy` with a negative length gives an old-style message ("Non-negative number required.") instead of .NET 11's `ThrowNegative` text, and `ParamName` is empty | fixed, together with the other `Array` and collection argument checks (samples 221, 222) |
| [`exception_tostring_stack_trace.cs`](../tools/compat/repros/exception_tostring_stack_trace.cs) | docs `standard/exceptions/how-to-use-the-try-catch-block-to-catch-exceptions`, and since the latest run the `BankAccount` tutorials (docs `fundamentals/tutorials/{introduction-to-classes,object-oriented-programming,xml-documentation}`, samples `classes-quickstart`) | `Exception.ToString()` has no stack-trace lines. The program prints `$"... {e}"` | fixed: stack traces with .NET's frames, names and lines ([ADR 0012](adr/0012-stack-traces.md), samples 410-416); the seven programs pass. Library frames are reproduced for common members only, with warning `CSLLVM0006` |

### COMPILE-CRASH and compile bugs

| repro | found in | bug | status |
|---|---|---|---|
| [`ice_lambda_in_extension_block.cs`](../tools/compat/repros/ice_lambda_in_extension_block.cs) | docs `linq/HowToExtend` | SIGSEGV, with no diagnostic, on a lambda inside a member of a C# 14 `extension(...)` block, even when the member is never called | fixed (sample 226) |
| [`ice_extension_block_static_call.cs`](../tools/compat/repros/ice_extension_block_static_call.cs) | docs `programming-guide/classes-and-structs/ExtensionMembers` | Invalid IR (`Global is external, but doesn't have external or weak linkage`) when an extension-block method is called in static form, `E.WordCount(s)` | fixed (sample 226) |
| [`ice_union_collection_expression.cs`](../tools/compat/repros/ice_union_collection_expression.cs) | docs `whats-new/tutorials/.../SmartHome.App` | C# 15 `union` types crash the compiler (SIGSEGV / stack overflow in `LLVMBuildStore` via `EmitCollectionExpression`) instead of producing `CSLLVM0001` | now `CSLLVM0001` (union types) |
| [`ice_object_is_double_constant.cs`](../tools/compat/repros/ice_object_is_double_constant.cs) | (found while minimizing the NaN bug) | `object o; o is 1.5` / `o is double.NaN` emits `icmp eq ptr, double`, which is invalid IR, because the operand is never type-tested or unboxed | fixed (sample 223) |
| [`startup_object/`](../tools/compat/repros/startup_object/) | 18 docs projects | `<StartupObject>` ignored, giving `CSLLVM0003` (blocker row 2) | fixed (`tests/projects/cli/StartupObject`) |
| [`projectref_struct_definite_assignment/`](../tools/compat/repros/projectref_struct_definite_assignment/) | docs `compiler-messages/WarningWaves` | Folding `ProjectReference` sources into one compilation (ADR 0006) changes C# semantics. Definite-assignment rules are relaxed for structs imported from another assembly (warning `CS8882`), but strict within one compilation (error `CS0177`) | fixed: diagnostics come from per-project compilations, files are parsed with their own project's symbols, and other merged-only errors are explicit (`tests/projects/cli/ProjectRefs`) |

### Re-run of the affected programs after the fixes

All 31 programs listed above as COMPILE-CRASH, COMPILE-ERROR (except the two `FrameworkReference`
programs) or RUNTIME-DIFF/CRASH were re-run with `tools/compat/run.sh` at the end of the fix
branch. **17 now pass**: `samples core/linq/conversion`, the NaN, preprocessor, constructors,
`comparison2`, extension-member and `WarningWaves` programs, and 10 of the 18 `<StartupObject>`
programs. The other 14 now stop at the following causes:

- **Explicit diagnostics**, most of them blockers already listed above:
  `Exception.GetType()`, `ulong.TryParse`, `Console.ReadKey` (2), deriving from `EventArgs` and
  `JsonConverter<T>`/`JsonSerializerContext`, `DateTimeOffset`, `GC.SuppressFinalize`, and C# 15
  unions.
- **Unions**: Roslyn binding error `CS0117`, as before.
- **Stack traces** (2 programs): only the `   at ...` lines differ. In the `CatchException`
  program, the `File name: '...'` line that `FileNotFoundException.ToString()` adds is also
  missing.
- **New bugs**, previously hidden behind `<StartupObject>` (both fixed on branch `followups`):
  - `object-collection-initializers` crashed with `InvalidCastException`. A corelib struct
    enumerator (`List<T>.Enumerator`), returned as the non-generic `IEnumerator`, wasn't castable
    to it. The corelib's struct enumerators now implement `IEnumerator<T>`, `IEnumerator` and
    `IDisposable` like .NET's, and the program passes (sample 280).
  - The JSON `how-to` project crashed the compiler (SIGSEGV while computing class layouts). A
    `DateTimeOffset` property left a half-built base-class layout cached, and the derived class
    built invalid IR from it. The project now stops with explicit diagnostics for
    `DateTimeOffset` and the `System.Text.Json` APIs (samples 283–285).
- **Fixed during the re-run**: the `lambda-expressions` program silently printed an empty line
  for an `Expression<Func<...>>`. Expression trees are now `CSLLVM0001`. The `WarningWaves`
  program needed `CS0269` in the set of merged-compilation definite-assignment errors. It then
  produced invalid IR for user-defined operators lifted to nullable structs, which are now
  lowered (sample 229), and it passes.

## Recommendations

In order of programs gained per unit of work:

1. **Fix the silent bugs first** (done on `compat-fixes`, see the status column above). These break the one rule. The fixes: arrays as non-generic
   `IEnumerable`, NaN constant patterns, TFM define constants, lazy static constructors (with
   `beforefieldinit` semantics), non-ASCII `OrdinalIgnoreCase`, the `Array.Copy` message. Each
   gets a sample in `tests/samples/`. The extension-block and union crashes should at least become
   `CSLLVM0001` diagnostics.
2. **`<StartupObject>`** (done). One property affects 18 programs, 6.4 % of the corpus. For now they stop
   at `CSLLVM0003`, so other blockers may still surface behind it.
3. **Small corelib gaps with a high "only" count** (mostly done, see
   [Progress](#progress-since-the-first-run)): `StringBuilder.Append($"...")` (6 programs
   with no other blocker), `Console.ReadKey` with redirected input (7), the argument throw
   helpers, the missing `String` overloads, `DateTime.Parse`, `Enum.GetValues(Type)`,
   `ValueType.ToString()`, `EventArgs`/`Attribute` as base classes, `new Random(seed)`,
   `Debug.Assert`, and deconstruction into properties. Each is small. 37 programs have no
   diagnostic outside this set. With items 1 and 2, that is up to 55 more programs, which would take
   the pass rate from 41 % toward 60 %. That is an upper bound, because further gaps can surface
   once a program compiles.
4. **`System.Text.Json`** is the largest single area (20 programs, 15 with no other blocker). It
   is also by far the largest piece of work. It fits the closed-world model if the serializer's
   type metadata is generated at compile time, the way the System.Text.Json source generator
   does it. Done on the `json` branch, see [Progress](#progress-since-the-first-run).
5. Then Regex, Globalization (done: [19](19-globalization.md)), the remaining
   threading surface, and the IO extras. All of these are already on the M2 corelib list.

Re-measure after each batch. The blocker tables above are the first-run baseline; the summary has
the latest numbers.

## Re-running

On CI: the `Compatibility` workflow (`.github/workflows/compat.yml`, manual and weekly) runs the whole
corpus on a hosted runner (~20 minutes with the NativeAOT publishes) and publishes the summary in the
job summary and `results.tsv` / `summary.md` / the csharpc logs and diffs of every program that doesn't
pass as an artifact; copy the artifact's `results.tsv` into `tools/compat/` to publish a run.
`gh workflow run compat.yml --ref <branch> -f filter=<id substring>` re-runs a few programs.
`results.tsv` columns: id, csharpc category, detail, NativeAOT verdict, NativeAOT detail, csharpc ms,
NativeAOT ms, csharpc bytes, NativeAOT bytes. Locally (NativeAOT's ILCompiler is heavy: prefer CI):

```sh
export PATH=$HOME/.dotnet11:$PATH
tools/compat/run.sh -j 2                    # fetch the pinned corpus, run all programs (hours on 4 shared cores)
tools/compat/run.sh --no-fetch -j 2 linq    # only program ids containing "linq"
tools/compat/run.sh --no-aot linq           # without the NativeAOT publishes
python3 -I tools/compat/analyze.py /tmp/compat-work    # re-print the tables (also writes summary.md there)
tools/compat/repro.sh tools/compat/repros/*.cs         # check the single-file reproducers
```

The corpus is downloaded to `/tmp/compat-corpus`. Builds, logs and results go to `/tmp/compat-work`:
`results/<id>.tsv`, and `logs/<id>/` with the build log, both .NET outputs, the csharpc log, the
native output and the diff. None of this goes into git. To move the corpus forward, change the
commits in `fetch.sh`, re-run, then commit the new `programs.tsv`, `excluded.tsv` and
`results.tsv` together with an updated report.

The corpus is untrusted input. The harness never executes scripts from it. It only builds and runs
the C# programs themselves, with `dotnet` and with csharpc, which is the point of the exercise.

## IL front-end survey

What nuget.org packages need from csharpc when they reach it as compiled assemblies
([ADR 0010](adr/0010-il-frontend.md)). `tools/il-survey/survey.py` takes the 52 packages in
`tools/il-survey/packages.txt` (the most downloaded pure-IL library packages, pinned), downloads each
with its dependency closure from nuget.org's flat container (only `lib/*.dll` and the `.nuspec` are
extracted; no MSBuild, so package scripts and targets never run), and compiles a trivial consumer with
`csharpc --il-survey -O0 --emit llvm-bc`, which lowers every method of the package that needs no type
arguments, reachable or not. The `IL survey` workflow runs it whenever the harness changes and
publishes the full report as an artifact.

First run (2026-10-23, CI, branch `il-phase2`): 26 030 diagnostics over 52 packages — 9 980 sites of
2 150 distinct missing library members, 12 335 unsupported constructs. 2 packages lower completely
(System.Linq.Async, System.Interactive.Async); DocumentFormat.OpenXml exceeded the survey's per-compile
memory cap. No package produced IR the verifier rejected. The 3 699 internal errors in that run were
one survey-only artifact (Roslyn's synthesized default struct constructors, which have no metadata
row, were handed to the IL decoder); they are diagnosed now.

**What blocks packages** (number of packages affected):

| Area | Packages | Top items |
|---|---|---|
| Reflection over `System.Type` | 40 | `Type.Assembly` (25), `IsAssignableFrom` (23), `IsGenericType` (22), `MethodBase.GetParameters` (20), `MemberInfo.DeclaringType` (18), `MakeGenericType` (16), `GetTypeInfo` (15), `Activator.CreateInstance` (13) |
| BCL value types without a layout | 29 | `DateTimeOffset` (29), `Guid` (25), `StreamingContext` (17, serialization constructors), `ArraySegment<byte>` (13), `ReadOnlySequence<byte>` (7), `CancellationTokenRegistration` (7, implemented since) |
| Small BCL members | 10–23 | `GC.SuppressFinalize` (23), `Array.SetValue` (17), `DateTime.ToString(string, IFormatProvider)` (15), `ArrayPool<byte>.Shared` (12), `Convert.From/ToBase64String` (12/11), `RuntimeHelpers.GetHashCode` (12), `Array.Clone`/`CopyTo` (10), `Buffer.BlockCopy` (9) |
| Library classes created or derived from | 8–17 | `new Uri` (17), `Version` (9), `Hashtable` (8), `Random` (8); overriding `Stream.ReadAsync` (6), `Exception.GetObjectData` (5); `EventSource` (8, implemented since) |
| Code generation at run time | 9–10 | `System.Linq.Expressions` (10), `Reflection.Emit` (9) — out of scope (closed world), as in NativeAOT |

The IL side itself is not what blocks: the unsupported constructs are library types and members, and
the rest of the list is the BCL surface (roadmap M2). Reflection is the largest area by far; the
closed-world answer is compile-time metadata for what is statically known (`typeof(X).Assembly`,
`IsGenericType`, `GetGenericArguments` on known types) and diagnostics for the rest, as M6 item 3 plans.

Re-run: `python3 -I tools/il-survey/survey.py --jobs 1 --out il-survey.md` (a fresh `/tmp` work dir by
default; `--only Id1,Id2` for a subset).

### Roslyn and csharpc

The same survey over M6's bootstrap closure: Roslyn (`Microsoft.CodeAnalysis`,
`Microsoft.CodeAnalysis.CSharp`, the 5.9.0 assemblies csharpc itself references) and `csharpc.dll`.
`--il-survey=A,B` lowers every non-generic method of just those assemblies (framework assemblies
they use are compiled from IL on demand, [ADR 0013](adr/0013-framework-il.md)), and
`--il-survey-shard i/n` splits the types into shards, so the manual `IL survey` workflow run with
`target: roslyn` compiles 16 shards in parallel jobs (~3–4 minutes each) and merges them into one
report ranked by the number of methods each item stops (`--rank-by methods`; a method reports its
first failure, so fixing an item can reveal the next one in the same method). Re-run locally for a
slice: `python3 -I tools/il-survey/survey.py --no-packages --local "roslyn+csharpc=<dlls>"
--survey-assemblies Microsoft.CodeAnalysis,Microsoft.CodeAnalysis.CSharp,csharpc --shards 16
--shard-index 0 --rank-by methods`.

| Survey run (2026-10-24, CI) | Roslyn | csharpc | framework IL | total methods that fail to lower |
|---|---:|---:|---:|---:|
| start of the `m6-roslyn` branch | 994 | 102 | 25 | 1 111 |
| after the first fixes (layouts, memories, StrongBox ...) | 293 | 74 | 29 | 396 |
| final (`m6-roslyn`, corelib compiled to IL as on `main`) | 170 | 77 | 41 (+1 corelib) | 289 |

What blocked the most Roslyn methods at the start, and is fixed on the branch (methods in the
first survey): `ExtendedSpecialType`'s explicit layout that is the sequential one (413), the
`HashAlgorithmName` and `RSAParameters` value types (105), `StrongBox<T>` (38), conditional weak
tables and weak references (37), `RuntimeHelpers.GetHashCode`/`ReferenceEqualityComparer` (34),
devirtualization to an open generic base method (26 — a bug C# programs could hit too), boxing,
hashing and comparing `ReadOnlyMemory<char>` keys (23 methods, 204 sites), `decimal` constants read as
fields (19), `CollectionsMarshal` (13), `GetTypeInfo` (13), fixed-size buffers (10), `Task.Run` with a
token and `LazyInitializer` (19), rectangular arrays, `[InlineArray]` structs, unions, interface calls
on array fields, IntPtr operators, SHA-1/SHA-256 and `IncrementalHash`, Base64/hex, delegates bound to
library methods, and `Assembly.GetCustomAttribute<T>()` as compile-time metadata.

Ranked list now (final run; methods stopped, all in one target):

| # | Methods | Item | Where |
|---:|---:|---|---|
| 1 | 12 + 4 + 4 + 2 | `RegexRunner`/`RegexRunnerFactory` subclasses (the SDK's `[GeneratedRegex]` output) | csharpc |
| 2 | 6 | `new Task(...)` | System.Linq.Parallel (csharpc's PLINQ) |
| 3 | 5 | `Environment.GetFolderPath` | Roslyn, csharpc |
| 4 | 5 | `Assembly.Location` | Roslyn |
| 5 | 5 | deriving from `DeflateStream` (embedded text, PDB) | Roslyn |
| 6 | 4 | `OperationCanceledException.CancellationToken` | Roslyn, PLINQ |
| 7 | 4 | `Convert.ToInt64(object)` | csharpc |
| 8 | 4 | `Task.CurrentId` | PLINQ |
| 9 | 4 | `XmlReaderSettings`/`XmlReader` (XML doc comments) | Roslyn |
| 10 | 4 | boxing `CancellationToken` | Roslyn |
| 11 | 3 | `AppContext.BaseDirectory` | csharpc |
| 12 | 3 | `Environment.FailFast` | Roslyn |
| 13 | 3 | `MemoryMarshal.AsBytes` | System.Reflection.Metadata |
| 14 | 3 | `RuntimeEnvironment.GetRuntimeDirectory` | csharpc |
| 15 | 3 | `Version.TryParse` | csharpc |
| 16 | 3 | `SHA384.Create` | Roslyn |
| 17 | 3 | `Parallel.For` with `ParallelOptions` | csharpc |
| 18 | 3 | `AppDomain.CurrentDomain` / `AssemblyLoadContext.Default` (analyzer loading) | Roslyn, csharpc |
| 19 | 3 | `ImmutableArray<T>.Enumerator` typing in iterators | Roslyn |
| 20 | 3 | `ParallelLoopResult` | Roslyn |

The survey only counts methods that fail to *lower*. A program that parses C# with Roslyn from IL
(`CSharpSyntaxTree.ParseText(...)` and a walk over the tree) is the other measure: the methods it can
reach that csharpc can't compile went from 121 to 33 on the branch; the rest are `StringBuilder.GetChunks`
(its chunk layout is observable and csharpc's builder has one buffer), `MemoryMarshal.TryGetArray` over
`ReadOnlyMemory<T>` (memories don't keep their owner array), `DeflateStream`, `SHA384`,
`Vector256`, `BigInteger` byte conversions, encoding providers and a few single members.

### ASP.NET Core and the TechEmpower app

M7's measure ([06-roadmap.md](06-roadmap.md#m7--techempower-csharpc-beats-nativeaot)):
`benchmarks/techempower/app` is the plaintext and JSON test types of the TechEmpower Framework Benchmarks
in the style of the aspnetcore entries that publish with NativeAOT (Kestrel, `WebApplication.CreateSlimBuilder`,
middleware rather than minimal-API endpoints, System.Text.Json source generation). The `TechEmpower`
workflow (`.github/workflows/techempower.yml`, `benchmarks/techempower/run.sh`) builds it with
NativeAOT, csharpc and the JIT, checks every build with TechEmpower's test requests (`verify.py`:
status, `Content-Type`, `Content-Length`, `Server`, `Date`, bodies, 16 pipelined requests;
csharpc's normalized responses must equal NativeAOT's), loads it with wrk the way TechEmpower does and
writes requests/s, latency, peak RSS, binary size and start-up time; when csharpc can't build the app it
lists what stops it (`blockers.py`).

csharpc reads `<FrameworkReference Include="Microsoft.AspNetCore.App">` (implicit in `Microsoft.NET.Sdk.Web`)
and `--framework Microsoft.AspNetCore.App`: Roslyn binds against the ASP.NET Core reference pack, and the
framework-IL choice ([ADR 0013](adr/0013-framework-il.md)) indexes the ASP.NET Core shared framework too,
so for the app it takes 95 assemblies from IL — Hosting, Kestrel, DI, configuration, logging, routing,
System.IO.Pipelines, System.Diagnostics.DiagnosticSource. A type forward makes an assembly ineligible
only when it leads into `System.Private.*`; ASP.NET Core's assemblies forward types to their neighbours
and DiagnosticSource forwards only `IsExternalInit`.

The survey's `aspnetcore` target (`tools/il-survey/aspnetcore.sh`, the `IL survey` workflow with
`target: aspnetcore`, 16 shards) lowers every method of those of the app's assemblies that are
`Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Microsoft.Net.Http.Headers`, `System.IO.Pipelines`
and `System.Net.*` (not the Windows-only IIS and HTTP.sys servers, nor the obsolete WebRequest stack) —
58 assemblies — ranked by the methods each item stops.

| Survey run (2026-10-24, CI) | methods that fail to lower | of them in the 48 assemblies of both runs |
|---|---:|---:|
| start of the `m8-harness` branch | 2 974 | 2 888 |
| after the fixes below (`m8-harness`) | 1 806 | 1 691 |

Fixed on the branch, by methods stopped in the first survey: the whole virtual surface of `Stream`
(`ReadAsync`/`WriteAsync`/`BeginRead`/`CanTimeout`/`FlushAsync`/`DisposeAsync` overrides: ~490),
**runtime-async methods** (190: .NET 11 compiles Kestrel, Hosting and System.Net.Http with
`MethodImplOptions.Async`, whose IL awaits through `AsyncHelpers.Await`; csharpc lowers them as LLVM
coroutines like C# async methods, [10-async.md](10-async.md#runtime-async-methods-from-il)),
`ReadOnlySequence<T>`/`SequenceReader<T>`/`SequencePosition` (155), `SearchValues<byte/char>` (64),
`Collection<T>`, TextWriter/TextReader span and async members, MarshalByRefObject, `System.Text.Ascii`,
`TaskToAsyncResult`, `Rune`, `UrlEncoder`, `Uri.EscapeDataString`, `Timer`/`TimeProvider`, `Task.Status`,
`ConfigureAwait(ConfigureAwaitOptions)`, `Buffer.BlockCopy`, span comparisons with a `StringComparison`,
the start-up members of hosting and DI (`AppContext.BaseDirectory`, `Environment.ProcessPath`,
`RuntimeFeature.IsDynamicCode*` = false as under NativeAOT, `TryEnsureSufficientExecutionStack`), and
NativeAOT's folding of the dynamic-code feature switches in IL (DI's Reflection.Emit resolver is no
longer lowered). Found on the way: `x is { } v` on a nullable library struct (`SequencePosition?`,
`Guid?`, `KeyValuePair<,>?`) was always false — fixed, sample 506 — and the IL front-end crashed on
`Span<T>.op_Implicit(ArraySegment<T>)` called as a method (8 DataProtection methods; fixed after the last
survey run).

Ranked list now (methods stopped, items of one API grouped; *other branch*: work in progress elsewhere):

| # | Methods | Item | Where |
|---:|---:|---|---|
| 1 | 524 | reflection: `MethodInfo`/`ParameterInfo`/`PropertyInfo` members, `Type.GetProperty`/`GetMethod`/`GetConstructors`/`MakeGenericType`/`GetInterfaces`, deriving from `ParameterInfo`, `Reflection.Emit` labels | DI, System.Linq.Expressions, Http.Extensions (request delegates), Hosting |
| 2 | 170 | TLS: `SslStream`, `SslClientHelloInfo`, X509 certificates and stores | Kestrel.Core (HTTPS), DataProtection, System.Net.Http |
| 3 | 156 | `DateTimeOffset` (*other branch*) | System.Net.Http, DiagnosticSource, Logging |
| 4 | 120 | deriving from `MemoryPool<T>` / `MemoryManager<T>` (*other branch*) | Kestrel's pinned block pool, its transports |
| 5 | 78 | `Uri` (`op_Equality`, `TryCreate`, `IdnHost`, `IsAbsoluteUri`, `SchemeDelimiter`, ...) | System.Net.Http, Http.Extensions, Cors |
| 6 | 58 | `SafeHandle`: deriving from `SafeHandleZeroOrMinusOneIsInvalid`, `SafeHandleMarshaller<T>`, `handle` | Cryptography.Internal (Windows CNG) |
| 7 | 30 | `RegexRunner`/`RegexRunnerFactory` (the SDK's `[GeneratedRegex]` output) | Routing |
| 8 | 20 | `ExceptionDispatchInfo.SetCurrentStackTrace` | System.Net.Http |
| 9 | 16 | deriving from `SocketAsyncEventArgs` (async socket I/O) | Kestrel.Transport.Sockets |
| 10 | 15 | deriving from `TaskCompletionSource(<T>)` | Kestrel.Core, System.Net.Http |
| 11 | 15 | `XName` (XML key ring) | DataProtection |
| 12 | 14 | `AsyncLocal<T>` | Hosting, logging scopes |
| 13 | 13 | overriding `Exception.GetObjectData` | Routing, Kestrel.Core, System.Net.Http |
| 14 | 13 | `Hashtable`, `ArrayList` | System.Collections.NonGeneric users |
| 15 | 13 | `IPNetwork` | HttpOverrides |
| 16 | 8 | `OSPlatform.Windows`, `HttpVersion.Version11`, `OperationCanceledException.CancellationToken` (8 each) | Kestrel.Core, Hosting, System.Net.Http |
| 17 | 6 | `RuntimeHelpers.GetUninitializedObject` | Http.Extensions, Configuration.Binder |
| 18 | 6 | `StringBuilder.Append(IFormatProvider, $"...")` | Kestrel.Core, Routing |
| 19 | 6 | `MemoryExtensions.CopyTo(T[], Span<T>)` | Kestrel.Core, WebUtilities |
| 20 | 5 | explicit layout of `Internal.PaddingFor32`; deriving from `CancellationTokenSource`; `StackTrace`; `AsyncFlowControl` (5 each) | System.IO.Pipelines, Kestrel.Core, Hosting |

The survey counts methods that fail to *lower*. Compiling the app itself is the other measure: the
methods it reaches that csharpc can't compile yet are 43 (`blockers.py` on `csharpc -v` output; a
method stops at its first failure, so fixing one can reveal more behind it). The largest group is
dependency injection's reflection (`Type.GetConstructors`, `ConstructorInfo.Invoke`, `MakeGenericType`,
`Array.CreateInstance`, `Delegate.Method`), which NativeAOT serves from compile-time metadata kept for
the types DI's `[DynamicallyAccessedMembers]` annotations name; then `DateTimeOffset` (the console
logger), file-system watching for configuration reload (`FileSystemWatcher`, `FileSystemInfo.Attributes`/
`LinkTarget`), `PosixSignalRegistration` (SIGTERM handling), `SemaphoreSlim`,
`ObservableCollection<T>` (System.ObjectModel is ineligible because the corelib implements
`PropertyChangedEventArgs`) and type tests against the task-handle structs (`CancellationToken`, the
configured awaiters). Beyond compiling: Kestrel's socket transport needs `Socket.ReceiveAsync`/
`SendAsync` with `SocketAsyncEventArgs` on an epoll engine (csharpc's sockets are synchronous today).

NativeAOT baseline (`TechEmpower` workflow, three runs on GitHub `ubuntu-24.04` runners, 4 vCPUs shared by
server and wrk, 2026-10-24; runners differ by up to 40 %, so compare builds within one run):

| | NativeAOT | JIT |
|---|---:|---:|
| plaintext, 256 connections, pipeline 16 | 529 000–754 000 req/s | 693 000–972 000 req/s |
| plaintext, 1024 connections, pipeline 16 | 475 000–655 000 req/s | 640 000–841 000 req/s |
| JSON, 16 connections | 68 000–101 000 req/s (p99 0.3–0.5 ms) | 75 000–105 000 req/s |
| JSON, 64 connections | 83 000–116 000 req/s (p99 1.3–1.9 ms) | 92 000–124 000 req/s |
| JSON, 256 connections | 86 000–119 000 req/s (p99 4.1–5.4 ms) | 97 000–127 000 req/s |
| peak RSS, idle → after load | 17 → 65 MiB | 54 → 140 MiB |
| executable | 8.9 MiB | (framework-dependent) |
| start-up to the first response | 23–27 ms | 125–156 ms |

On these shared runners the JIT is ahead of NativeAOT by 5–30 % on throughput; NativeAOT starts 5x
faster and uses half the memory.

Since 2026-10-25 (branch `m8-kestrel`) csharpc builds the app with no stubbed method. Its responses pass
TechEmpower's checks identically to NativeAOT. In its first load run it was at 49 % of NativeAOT's
pipelined plaintext throughput and 55–63 % of its JSON throughput. It started faster (20 ms vs 26 ms) and
used less memory idle (13.0 vs 17.7 MiB), but more under load (80.3 vs 64.9 MiB), with a 14.5 MiB binary
(8.9 MiB for NativeAOT). The numbers are in [06-roadmap.md](06-roadmap.md). Still open on this path:
- Logging configured through the host (`AddConsole` with configuration binding) needs `Array.CreateInstance`
  of a type the program has no array of (`IEnumerable<LoggingConfiguration>` from DI). The app clears
  its providers, as TechEmpower's entries do.
- Async sockets are blocking calls on the thread pool on macOS and Windows (no kqueue/IOCP engine yet).
- `MemberInfo` members called on a `Type` through a `MemberInfo`-typed reference throw
  `NotSupportedException`. Through a `Type`-typed reference they work.
