# 18 — Real-world compatibility report

How much real C# code does csharpc compile and run exactly like .NET (and like NativeAOT), and what blocks the rest?
The differential samples and the conformance corpus ([09-conformance.md](09-conformance.md)) are written *for*
csharpc. This corpus is not: it is unmodified console programs from Microsoft's documentation and sample
repositories, run by the `Compatibility` workflow ([Re-running](#re-running)). The report also covers the two other
measures of real-world use: the IL front-end survey over the most downloaded NuGet packages, and ASP.NET Core with
TechEmpower's Platform entry.

## NativeAOT parity

The headline metric ([ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)): of the programs NativeAOT
(`dotnet publish -p:PublishAot=true`) builds and runs exactly like the JIT, how many does csharpc match? See
[Method](#method), step 5.

Latest run (scheduled `Compatibility` run of 2026-11-01 on `main`, hosted `ubuntu-24.04` runner, .NET SDK
`11.0.100-rc.2.26504.105`, Linux x86-64, 320 programs):

| | |
|---|---:|
| compared (not skipped under .NET) | 275 |
| NativeAOT-unsupported (out of the denominator) | 29 |
| **NativeAOT-working (the denominator)** | **246** |
| **parity: csharpc PASS among them** | **242 (98.4 %)** |
| csharpc COMPILE-ERROR among them | 4 |
| csharpc COMPILE-UNSUPPORTED / COMPILE-CRASH / RUNTIME-DIFF / RUNTIME-CRASH among them | 0 / 0 / 0 / 0 |

- **No wrong output** among the in-scope programs. The 4 failures are compile errors (#163): three projects whose
  `LangVersion` is 6 or 7.2 stop in the built-in regex generator's output (`RegexGenerator.g.cs(3,2): error
  CS8059/CS8320: Feature 'nullable reference types' is not available`: the generated file begins with
  `#nullable enable annotations` whether or not the program has a `[GeneratedRegex]`), and `trimming/ConsoleApp1`
  makes csharpc print its usage text and exit 2. The run before it, on 2026-10-30, was 249 of 249 (100 %). Fixed in #163
  (the next Compatibility run confirms it; regression sample `1236_langversion7_no_regex_generator_output` and the
  `ConfigBindingRefs` / `Net8Generators` project tests): the regex generator now emits nothing without `[GeneratedRegex]`
  members and `#nullable` only from C# 8; the `ConsoleApp1` exit was Roslyn's "Inconsistent syntax tree features" (the
  root project's csc line carries `/features:InterceptorsNamespaces=...`, its library's does not), now one union of features
  for all trees; and behind it, the SDK's generators of a `net8.0` project were bound to the newer
  `Microsoft.Interop.SourceGeneration` linked into csharpc (`TypeLoadException`, CS8784), now loaded from their own directory.
- **NativeAOT-unsupported, by reason** (29): crashes 15 (most are `System.Text.Json`'s reflection-based serializer,
  which NativeAOT disables; trim/AOT warnings `IL2026`/`IL3050`), output differs 11 (stack-trace lines: NativeAOT
  prints other frames and no file/line; exception messages), publish fails 2 (an XML documentation file NativeAOT's
  publish can't copy), non-deterministic 1. **csharpc passes 22 of these 29**: programs that work with csharpc but not
  with NativeAOT. Three of the other seven differ from .NET (the JIT baseline below): `aesdecrypt` prints a
  `FileNotFoundException` without the BCL's internal frames (csharpc records frames of the program and of IL-compiled
  code only, [ADR 0012](adr/0012-stack-traces.md)), and two async programs print other output (one of them is
  non-deterministic under NativeAOT too); four stopped at explicit diagnostics on that date (`Enum.GetName` with a
  non-constant `Type`, and three `System.Text.Json` APIs: custom contracts, the schema exporter, `IAsyncEnumerable`
  metadata).
- 7 in-scope programs publish with trim/AOT warnings but run identically; they stay in scope (the run is the evidence
  that NativeAOT supports them). NativeAOT runs the corpus' expression-tree programs (interpreted) and the `dynamic`
  ones identically to the JIT, so those count against csharpc ([Expression trees](#expression-trees), [ADR 0019](adr/0019-dynamic.md)).
- **Smaller, but slower to compile, than NativeAOT** (the 242 programs both handle): executable size median 143 KiB vs
  1,338 KiB (smaller on 239 of 242; max 6,177 KiB vs 7,515 KiB); compile time median 10.3 s vs 8.6 s (csharpc faster on 53
  of 242; max 111.2 s vs 24.2 s). Compile time is `csharpc App.csproj -c Release` against
  `dotnet publish -c Release -r linux-x64 -p:PublishAot=true`, wall clock, 4 programs in parallel on the runner; size is
  the executable alone (NativeAOT's symbols are in a separate `.dbg`). Earlier runs measured csharpc faster (median 5.7 s vs 7.1 s
  in the run of 2026-10-25), so part of the difference may be the runner; it is not yet explained.

### JIT baseline

The pass rate against the JIT alone (PASS among all compared programs) is kept for continuity: **264 of 275 (96.0 %)**
in the latest run, 4 COMPILE-UNSUPPORTED and 4 COMPILE-ERROR (the ones above), 3 RUNTIME-DIFF (all NativeAOT-unsupported
programs: two non-deterministic-by-nature async programs and `aesdecrypt`). Of the 267 programs csharpc compiled, 264 (98.9 %)
behave identically to .NET.

### History of the numbers

| run | JIT pass rate | NativeAOT parity |
|---|---|---|
| first, 2026-10-22 (283 compared) | 40.6 % (115) | n/a |
| after globalization and JSON | 52.5 %, then 65.0 %, then 67.5 % | n/a |
| first parity run | | 80.6 % (200 of 248) |
| NativeAOT-parity fixes, round 1 | 86.2 % | 88.7 % (220 of 248) |
| round 2 | 93.8 % | 95.9 % (236 of 246) |
| round 3 | | 97.2 % (243 of 250) |
| 2026-10-30 | | **100 % (249 of 249)** |
| 2026-11-01 (latest) | 96.0 % | 98.4 % (242 of 246) |

At 67.5 % every program csharpc compiled (189 of 189) already behaved exactly like .NET: no wrong output, no crash; the
seven differences before that were all missing stack-trace lines, which csharpc now records ([ADR 0012](adr/0012-stack-traces.md)).
The gains came from the library surface (`System.Text.Json`, regex, globalization, `DateTimeOffset`, `MemoryPool<T>`,
`Convert`, tasks, `Uri`, compression, cryptography, XML), from `dynamic`, expression trees and C# 15 unions, and from
the fixes listed under [What the corpus drove](#what-the-corpus-drove).

## Method

**Corpus.** Two official Microsoft repositories, pinned by commit and downloaded as tarballs
(`tools/compat/fetch.sh`):

| repository | commit | directories searched |
|---|---|---|
| [dotnet/samples](https://github.com/dotnet/samples) | `ab42c8a4251cb90787aa5d3c94b78d28d85bc46b` | `core/console-apps`, `core/linq`, `core/tutorials`, `csharp` |
| [dotnet/docs](https://github.com/dotnet/docs) | `705acf6d28963fdb60e49ddd1c5437b3e0419a66` | `docs/csharp`, `docs/standard`, `docs/fundamentals`, `docs/core` |

`tools/compat/discover.py` statically selects every SDK-style project (`Microsoft.NET.Sdk`) with
`OutputType` `Exe` that targets .NET 5 or later. It excludes the following:

- NuGet `PackageReference`s (130 projects). The corpus predates the IL front-end ([ADR 0010](adr/0010-il-frontend.md)),
  which can compile packages; the projects stay excluded.
- Libraries (60).
- Worker, Web and WindowsDesktop SDKs.
- GUI and Windows-only projects.
- .NET Framework targets.
- Programs whose sources use networking (20), hosting/ASP.NET, or process/assembly loading.

The result is 323 candidate programs (320 in the latest run): 268 from dotnet/docs and 55 from dotnet/samples.
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

## What the corpus drove

The first run ranked what blocked programs (`System.Text.Json` in 20, a `<StartupObject>` that csharpc ignored in 18,
deriving from BCL classes in 14, `System.String` members in 11, spans and `System.Buffers` in 11, reflection-lite in 10,
globalization in 10, threading in 10, console input in 9, regex in 9, ...). Each blocker became a regression sample recorded from
.NET. The small corelib gaps came first (samples 250–258; 32 programs passed after them):

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

**`System.Text.Json`** was the largest single blocker (20 programs, 15 with no other blocker) and the largest piece of
work. It fits the closed-world model because the serializer's type metadata is generated at compile time, the way the
System.Text.Json source generator does it: ported to the corelib ([12-corelib.md](12-corelib.md#systemtextjson), samples
310–326). Its first run took the corpus' 25 JSON programs from none to 10 passing; the others stopped at explicit
diagnostics (`DateTimeOffset`, `Debug.Assert(bool, string)`, `System.Text.Json.Schema`, anonymous types and `file`-local types with the
reflection-style serializer), all implemented since except the last two shapes.

**Library members (samples 480–487).** Re-run of the programs they blocked
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
invalid IR and now throws .NET's `BadImageFormatException` when called. `Array.GetEnumerator()` (non-generic
`foreach` over an `Array` returned by `Enum.GetValues`) needs the element type at run time and stays a diagnostic.

**NativeAOT parity, round 1 (samples 490–499, 513–524).** The first parity run (80.6 %) ranked the in-scope blockers;
each was fixed with a regression sample recorded from `dotnet run`:

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

**NativeAOT parity, round 2 (samples 530–555).** 88.7 % → 95.9 %. Each fix has a regression sample recorded from
`dotnet run`:

| fix | programs | sample |
|---|---|---|
| `Environment.Version`, `RuntimeInformation`, the SDK's assembly attributes | — | 530 |
| `BufferedStream` (port) | `BufferedStreamWriteByteFlush` | 531 |
| `IdnMapping` (port; the OS's ICU on each platform) | — | 532, 533 |
| `Thread.Interrupt` (and an interruptible `Thread.Join` on Windows, as there) | `InterruptThread` | 534 |
| unreachable unsupported source methods compile as stubs, like IL methods | — | 535 |
| `[StructLayout(LayoutKind.Explicit)]` / `[FieldOffset]` | — | 536 |
| `System.IO.Compression` (`BrotliStream`, `ZipArchive`, `ZipFile`) and `System.Formats.Tar` from IL, over .NET's native shims | `zip-tar-best-practices` | 537, 540, 1211 |
| `Aes`, `CryptoStream`, `RandomNumberGenerator`, HMAC, PBKDF2 over the OS's crypto library (OpenSSL on Linux, CommonCrypto on macOS, CNG on Windows) | `aesencrypt` | 538 |
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
| `RSA` over OpenSSL on Linux and macOS (key generation, `RSAParameters`, PKCS#1/PSS signatures, PKCS#1/OAEP encryption, the signature formatters) | `cryptographic-signatures` | 553 |

Along the way: `Monitor.Wait` lost a pulse that met its interrupt-poll timeout (a rare hang of
`SemaphoreSlim`/`BlockingCollection` producers, sample 555); `Task.RunSynchronously` now waits for attached
children (PLINQ's merge disposed its barriers early); event waits on WebAssembly run queued work; P/Invoke
`bool` parameters into the C runtime trapped on WebAssembly; `UnobservedTaskExceptionEventArgs` and
`GC.WaitForPendingFinalizers` (sample 554).

## Bugs: wrong output and crashes

Each bug has a minimal reproducer in `tools/compat/repros/`. Run `tools/compat/repro.sh FILE.cs`,
which runs the file under `dotnet run` and under csharpc and diffs the results. The **status**
column shows what changed after the report (regression samples `tests/samples/220`–`229`, project tests in
`tests/projects/cli`).

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
| [`ice_union_collection_expression.cs`](../tools/compat/repros/ice_union_collection_expression.cs) | docs `whats-new/tutorials/.../SmartHome.App` | C# 15 `union` types crash the compiler (SIGSEGV / stack overflow in `LLVMBuildStore` via `EmitCollectionExpression`) instead of producing `CSLLVM0001` | fixed: C# 15 unions are lowered |
| [`ice_object_is_double_constant.cs`](../tools/compat/repros/ice_object_is_double_constant.cs) | (found while minimizing the NaN bug) | `object o; o is 1.5` / `o is double.NaN` emits `icmp eq ptr, double`, which is invalid IR, because the operand is never type-tested or unboxed | fixed (sample 223) |
| [`startup_object/`](../tools/compat/repros/startup_object/) | 18 docs projects | `<StartupObject>` ignored, giving `CSLLVM0003` (blocker row 2) | fixed (`tests/projects/cli/StartupObject`) |
| [`projectref_struct_definite_assignment/`](../tools/compat/repros/projectref_struct_definite_assignment/) | docs `compiler-messages/WarningWaves` | Folding `ProjectReference` sources into one compilation (ADR 0006) changes C# semantics. Definite-assignment rules are relaxed for structs imported from another assembly (warning `CS8882`), but strict within one compilation (error `CS0177`) | fixed: diagnostics come from per-project compilations, files are parsed with their own project's symbols, and other merged-only errors are explicit (`tests/projects/cli/ProjectRefs`) |

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

A survey item can be an artifact of the consumer rather than a gap: the `System.Xml.Linq` members it lists
(`XName` operators, `XContainer.Element(XName)`, `XElement.Attribute(XName)`, `XObjectChangeEventArgs.ObjectChange`; 6 packages)
are called by `MS.Internal.Xml.Linq.ComponentModel` in `System.ComponentModel.TypeConverter`, which the survey lowers
whole while its trivial consumer references no `System.Xml.Linq`. A program that uses LINQ to XML compiles the
framework's own `System.Xml.Linq` from IL and behaves as .NET (sample 1235, checked at -O0, -O2, `--via-il` and on WebAssembly).

The first run (2026-10-23) lowered 52 packages with 26 030 diagnostics (9 980 sites of 2 150 distinct missing library
members, 12 335 unsupported constructs); 2 packages lowered completely and no package produced IR the verifier rejected. What
blocked packages, by number of packages affected: reflection over `System.Type` (40 packages: `Type.Assembly`,
`IsAssignableFrom`, `IsGenericType`, `MethodBase.GetParameters`, `MakeGenericType`, `Activator.CreateInstance`), BCL value types
(29: `DateTimeOffset`, `Guid`, `StreamingContext`, `ArraySegment<byte>`, `ReadOnlySequence<byte>`), small BCL members (10–23:
`GC.SuppressFinalize`, `Array.SetValue`, `ArrayPool<byte>.Shared`, `Convert.To/FromBase64String`, `Buffer.BlockCopy`), library classes
created or derived from (8–17: `Uri`, `Version`, `Hashtable`, `Stream.ReadAsync` overrides, `Exception.GetObjectData`,
`EventSource`) and code generation at run time (9–10: `System.Linq.Expressions`, `Reflection.Emit`; out of scope, as in
NativeAOT, except that expression trees are interpreted now). All but the last are implemented. The IL side itself is
not what blocks: the unsupported constructs are library types and members, the BCL surface of milestone M2. Reflection is
the largest area; the closed-world answer is compile-time metadata for what is statically known (`typeof(X).Assembly`,
`IsGenericType`, `GetGenericArguments` on known types, [ADR 0018](adr/0018-reflection-metadata.md)) and diagnostics for the rest.

Re-run: `python3 -I tools/il-survey/survey.py --jobs 1 --out il-survey.md` (a fresh `/tmp` work dir by
default; `--only Id1,Id2` for a subset). One package takes minutes now that most methods lower (Newtonsoft.Json:
323 s and 43 diagnostics, against 25 s and 1 942 on 2026-10-24; NJsonSchema 50 minutes), so the workflow runs
the packages as 13 parallel jobs (`--package-shard I/13`) whose `results.json` a report job merges (`--merge`); the
whole survey takes about an hour. The ranking of 2026-11-07 (all 52 packages) and the items taken from it are in
#146; Newtonsoft.Json, Dapper, NodaTime, AutoMapper, HtmlAgilityPack, RestSharp and ImageSharp report no missing
library member of their own any more.

### Roslyn and csharpc

The same survey over M6's bootstrap closure: Roslyn (`Microsoft.CodeAnalysis`,
`Microsoft.CodeAnalysis.CSharp`, the version csharpc itself references) and `csharpc.dll`.
`--il-survey=A,B` lowers every non-generic method of just those assemblies (framework assemblies
they use are compiled from IL on demand, [ADR 0013](adr/0013-framework-il.md)), and
`--il-survey-shard i/n` splits the types into shards, so the manual `IL survey` workflow run with
`target: roslyn` compiles 16 shards in parallel jobs (~3–4 minutes each) and merges them into one
report ranked by the number of methods each item stops (`--rank-by methods`; a method reports its
first failure, so fixing an item can reveal the next one in the same method). Re-run locally for a
slice: `python3 -I tools/il-survey/survey.py --no-packages --local "roslyn+csharpc=<dlls>"
--survey-assemblies Microsoft.CodeAnalysis,Microsoft.CodeAnalysis.CSharp,csharpc --shards 16
--shard-index 0 --rank-by methods`.

| Survey run (CI) | Roslyn | csharpc | framework IL | total methods that fail to lower |
|---|---:|---:|---:|---:|
| 2026-10-24, start of the work | 994 | 102 | 25 | 1 111 |
| 2026-10-24, after the first fixes (layouts, memories, StrongBox ...) | 293 | 74 | 29 | 396 |
| 2026-10-24, corelib compiled to IL | 170 | 77 | 41 (+1 corelib) | 289 |
| 2026-10-31, Roslyn 5.12 | 73 | 17 | 3 (+8 corelib) | 101 |
| 2026-10-31, after the struct-layout, `Parallel` and crypto fixes | 41 | 10 | 2 (+7 corelib) | 60 |
| 2026-10-31, last measurement (the shards' compile time 4 532 s → 1 556 s) | 32 | 2 | 2 (+2 corelib) | 38 |
| 2026-11-07, `main` at `25edc99` (run 42408659401; before the fixes below) | 27 | 3 | 2 | 32 |
| 2026-11-07, `fix/m5-il-leftovers` (run 42423203020; all 16 shards compile to the end) | 4 | 0 | 0 | 4 |

Fixed after the 2026-10-31 survey, by methods stopped: struct
layouts LLVM's struct types can't express — overlapping explicit fields and `Pack` below a field's
alignment, now opaque bodies of the CLR's size with fields at their byte offsets (DiaSymReader's
`VariantStructure` and `ImageDebugDirectory`, CodePages' `CodePageIndex`); `ParallelLoopResult` and
`Parallel.For`/`ForEach` with `ParallelOptions`; tuple names inside a generic container's arguments
(`ImmutableArray<(A a, B b)>.Enumerator`); pointer-element arrays and pointer fields in generic equality
(the `type test against 'byte*'` seen in every shard); `Guid` in P/Invoke; COM-object P/Invokes as stubs
that end the program (the CSLLVM0008 boundary, as for DiaSymReader's Windows-PDB factory); SHA-384,
SHA-512, MD5 and SHA-3 (SHA3-256/384/512, behind `IncrementalHash` and HMAC too); `CollectionsMarshal.GetValueRefOrAddDefault`; `MemoryStream.TryGetBuffer`;
`BinaryReader.Read`; `BigInteger.ToByteArray`/`TryParse` with styles; `Math.BigMul`; span
`ReverseEndianness`; `StringBuilder.AppendFormat` with a provider and a span; `Encoding.RegisterProvider`
and `EncodingProvider`; `TypeInfo.GenericTypeParameters`/`GetDeclaredMethods`/`DeclaredConstructors`;
`SafeBuffer` on `SafeHandle`; and `Assembly.GetManifestResourceStream` (compiled-in resources, M6 step 4).
Also: `Environment.FailFast`, `Assembly.ManifestModule`, `Assembly.GetReferencedAssemblies`,
`Assembly.GetCustomAttributes(Type, bool)`, `RuntimeEnvironment.GetRuntimeDirectory`,
`Process.GetCurrentProcess`, `GC.Collect` overloads, `FileNotFoundException.FusionLog`.

The 2026-11-07 re-measure (#169) found 32 methods, not 38, and after the fixes of that issue 4 were left, all of them
`Mutex` (`new Mutex(bool, string)` in two methods of the analyzer shadow-copy resolver, `Mutex.TryOpenExisting` in one, and
`Assembly.LoadFrom`, an explicit stop now: nothing is loaded at run time, in one); `Mutex` is implemented since (#174). What the run listed and how it was closed:
`System.IO.Compression.DeflateStream` (10 methods: embedded text and PDB compression) was never a missing type: the
survey's compiles inherit an address-space cap and a GC heap limit, and the child `dotnet restore` that fetches the
NativeAOT runtime pack (the zlib-ng shim) failed under them, so System.IO.Compression was not compiled from IL
(the diagnostic now says why, and the restore sets its own heap limit); `Debugger.Break` (a no-op without a debugger),
`BinaryWriter.Write(char[])`, `AppDomain.AssemblyResolve`/`Load` with `ResolveEventArgs`, `Assembly.GlobalAssemblyCache`,
`RuntimeReflectionExtensions.GetRuntimeMethods` and its siblings, `Delegate.CreateDelegate(Type, object, MethodInfo)`,
`File.SetAttributes` (and `FileInfo.IsReadOnly`, which always answered false), `SynchronizationContext.Wait`, the
`ComTypes.FILETIME`/`STATSTG` structs and `AssemblyName.GetAssemblyName` (a missing file only) are corelib members now
(samples 1236, 1237); the survey passes the SDK's source generators that `csharpc.dll` references (they were reported as
missing `-r` assemblies). With the compiles running to the end, the survey verified the whole module for the first time and
found two latent miscompiles (sample 1238): a ref struct's method table asked for field-wise `Equals` over its span fields
(invalid IR), and `IntPtr.op_Addition(IntPtr, int)` added an `i32` to an `i64`.

The survey only counts methods that fail to *lower*. A program that hosts Roslyn is the other
measure: `tests/projects/il/IlRoslynHostApp` references `Microsoft.CodeAnalysis.CSharp` as a package,
parses a snippet, normalizes and rewrites it, binds it against a core library the snippet defines,
prints symbols, constant values and diagnostics (with Roslyn's resource messages), emits it
deterministically in memory and reads the image back with System.Reflection.Metadata. Compiled by
csharpc, it prints exactly what `dotnet run` prints, the MVID and a hash of the PE image included. It
is a `heavy` IL test (`tests/il.sh` runs it with `CSHARPC_HEAVY_IL=1`; the `Bootstrap` workflow does):
on the shared VPS at -O0 with one thread the compile takes 466 s (lowering 106 s — 120 788 IL and
99 878 corelib methods —, LLVM's backend 284 s for 8.1 M instructions) and peaks at 5.6 GB; on a CI
runner with 2 threads, 4 min 22 s (with the `dotnet run` oracle) and 8.4 GB. Getting there fixed a silent
difference C# programs could hit too: generic variance through interface type arguments
(`List<IDerived> is IEnumerable<IBase>` was False; sample 726).

`StringBuilder` is .NET's chunked builder (the corelib port of dotnet/runtime's `StringBuilder.cs`):
`GetChunks`, `Capacity` and `EnsureCapacity` report the chunks .NET would have after any sequence of
appends, inserts, removals and replacements (sample 732). csharpc's walks differ only where they
change nothing observable: `Replace` goes front to back over forward links instead of searching from
the end for every chunk, and the indexer remembers the chunk it found last, so neither is quadratic in
the number of chunks as .NET's are.

csharpc compiling itself (`tools/bootstrap/selfhost.sh`, the `Bootstrap` workflow): `csharpc.dll` (159 693 IL and
144 969 corelib methods) lowers completely in 144 s at 4.5 GB on an 8 GB machine, and the whole self-compile runs on a CI runner
(it needs more memory than that machine has); the times, the memory and the native compiler passing every sample are in
[06-roadmap.md](06-roadmap.md#m6--bootstrapping). Getting there fixed things any program could hit: a precise static
constructor now runs before a P/Invoke call (sample 730), and `Process.Start` works on Linux and macOS (sample 731).

### ASP.NET Core and the TechEmpower app

M7's measure ([06-roadmap.md](06-roadmap.md#m7--techempower-the-csharpc-cli-beats-nativeaot)) is TechEmpower's
own entry: `benchmarks/techempower/app` is the ASP.NET Core [Platform] entry that
TechEmpower publishes with NativeAOT (`frameworks/CSharp/aspnetcore/src/Platform` at commit `57d92fb`,
unchanged but for net11.0): Kestrel with its HTTP parser driven directly (`HttpParser<T>`, no
middleware), System.Text.Json source generation, Npgsql with multiplexing on PostgreSQL, RazorSlices
templates for fortunes, `MemoryCache` for cached queries. The `TechEmpower` workflow
(`.github/workflows/techempower.yml`, `benchmarks/techempower/harness.py`; `run.sh` runs the same phases
on one machine) builds it with NativeAOT and the JIT as the entry's dockerfiles do and with csharpc from
the same IL NativeAOT compiles, verifies every build with TechEmpower's own verification code
(`tfb_verify.py`, run from the pinned FrameworkBenchmarks checkout: headers, bodies, the `queries`
parameter cases, fortune escaping and sorting, and the exact number of queries and rows PostgreSQL's
`pg_stat_statements` counts), requires csharpc's normalized answers to equal NativeAOT's and the JIT's
(`verify.py`), and measures every test type at every level TechEmpower uses with a csharpc-vs-NativeAOT
verdict per metric ([06](06-roadmap.md#m7--techempower-the-csharpc-cli-beats-nativeaot), "The harness"). When
csharpc can't build the entry it ranks what stops it (`blockers.py`).

The plaintext/JSON app the harness measured first (Kestrel middleware on
`WebApplication.CreateSlimBuilder`, not a TechEmpower entry) is `benchmarks/techempower/interim`. It is
measured in the same workflow, outside M7's verdict, and the IL survey's `aspnetcore` target below surveys its closure.

csharpc reads `<FrameworkReference Include="Microsoft.AspNetCore.App">` (implicit in `Microsoft.NET.Sdk.Web`)
and `--framework Microsoft.AspNetCore.App`: Roslyn binds against the ASP.NET Core reference pack, and the
framework-IL choice ([ADR 0013](adr/0013-framework-il.md)) indexes the ASP.NET Core shared framework too,
so for the app it takes 95 assemblies from IL — Hosting, Kestrel, DI, configuration, logging, routing,
System.IO.Pipelines, System.Diagnostics.DiagnosticSource. A type forward makes an assembly ineligible
only when it leads into `System.Private.*`; ASP.NET Core's assemblies forward types to their neighbours
and DiagnosticSource forwards only `IsExternalInit`.

The survey's `aspnetcore` target (`tools/il-survey/aspnetcore.sh`, the `IL survey` workflow with
`target: aspnetcore`, 16 shards) lowers every method of those of the interim app's assemblies that are
`Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Microsoft.Net.Http.Headers`, `System.IO.Pipelines`
and `System.Net.*` (not the Windows-only IIS and HTTP.sys servers, nor the obsolete WebRequest stack) —
58 assemblies — ranked by the methods each item stops.

The first run (2026-10-24) had 2 974 methods fail to lower (2 888 in the 48 assemblies the later runs share); after the work
below, 1 806 (1 691).

Fixed, by methods stopped in the first survey: the whole virtual surface of `Stream`
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
`Span<T>.op_Implicit(ArraySegment<T>)` called as a method (8 DataProtection methods). What the ranked list led with
afterwards — reflection (`MethodInfo`/`ParameterInfo`/`Type.GetConstructors`/`MakeGenericType`/`GetInterfaces`, served from
compile-time metadata, [ADR 0018](adr/0018-reflection-metadata.md)), TLS (`SslStream`, X.509), `DateTimeOffset`,
`MemoryPool<T>`/`MemoryManager<T>`, `Uri`, `SafeHandle` derivations, `SocketAsyncEventArgs`, `AsyncLocal<T>`,
`TaskCompletionSource` derivations — is implemented.

The survey counts methods that fail to *lower*. Compiling the app itself is the other measure: `blockers.py` ranks the
methods the program reaches that csharpc can't compile (a method stops at its first failure, so fixing one can reveal more
behind it); the `TechEmpower` workflow runs it whenever csharpc can't build an entry. Kestrel's socket transport runs on the
epoll engine with `Socket.ReceiveAsync`/`SendAsync` and `SocketAsyncEventArgs`.

The plaintext/JSON app built this way reached NativeAOT's throughput after profile-driven work (task completions waking the
main thread, `Monitor`/`Lock` entries, interface-call scans, thread-pool wake-ups, the `ConcurrentQueue`, byte/char span
searches, allocations per request), and its responses pass TechEmpower's checks identically to NativeAOT's. Still open on this
path:
- Logging configured through the host (`AddConsole` with configuration binding) needs `Array.CreateInstance`
  of a type the program has no array of (`IEnumerable<LoggingConfiguration>` from DI). The app clears
  its providers, as TechEmpower's entries do.
- Async sockets are blocking calls on the thread pool on macOS and Windows (no kqueue/IOCP engine yet).
- `MemberInfo` members called on a `Type` through a `MemberInfo`-typed reference throw
  `NotSupportedException`. Through a `Type`-typed reference they work.

#### TechEmpower's Platform entry

csharpc builds and runs the entry (the build is currently broken by #164). The measured build is NativeAOT's
pipeline: `dotnet build` with the AOT properties, then `csharpc Platform.dll` — the program as IL takes the
shared frameworks `Platform.runtimeconfig.json` lists (Microsoft.AspNetCore.App), as `dotnet Platform.dll`
would load them, so `--framework` is not needed. All seven test types answer as the JIT does (the harness's
normalized transcripts are identical, `benchmarks/techempower/verify.py`). The project input
(`csharpc Platform.csproj`, with the Razor and RazorSlices generators) compiles and runs too: the source front-end applies C# interceptors (`[InterceptsLocation]`) as csc's emit does
(`FunctionCodeGen.Interceptors.cs`, `SemanticModel.GetInterceptorMethod`), so `config.Get<AppSettings>()`
calls the configuration binding generator's code instead of reaching reflection over `AppSettings`
(`tests/projects/cli/ConfigBinding`).

What it took, besides library members (regression samples 840–849):

- **Sockets**: `Bind` sets `SO_REUSEADDR` on TCP sockets as .NET does on Unix (a restarted server could not
  rebind a port in `TIME_WAIT`, a silent difference); `ReuseAddress` reads `SO_REUSEPORT`, as .NET's
  native shim does. `Socket.Blocking`, a non-blocking `Connect` (WouldBlock, `Connected` completing it),
  `Select`, `GetSocketOption`, buffer sizes and TCP keep-alive options (Npgsql's synchronous connect).
  `NetworkStream`'s asynchronous reads and writes are the socket's asynchronous operations: before, they
  ran the synchronous ones on a pool thread, where the socket's `ReceiveTimeout` applied (Npgsql's
  writes failed with "Timeout during writing attempt"); a canceled socket operation throws
  `OperationCanceledException`, as .NET's.
- **Npgsql**: `Dns.GetHostAddressesAsync(host, token)`, `string.Normalize` (ICU, for SCRAM-SHA-256
  passwords), `HMACSHA256.HashData`, `ConcurrentExclusiveSchedulerPair`, EventCounters (never polled: csharpc
  has no listener), `GetString(ReadOnlySequence<byte>)`, `GetStringComparer`, IConvertible on boxed
  primitives, enums and strings, the non-generic `IDictionary` of `Dictionary<,>`, `Enum.Parse(Type, ...)`
  with a Type known only at run time, `StringBuilder.Append(provider, $"...")`, `Span<T>.ToArray()` making a
  `T[]` (it made an untyped array: `GetType()` said `System.Array` and casts to `T[]` failed).
- **MemoryCache**: `ConcurrentDictionary`'s `AlternateLookup<ReadOnlySpan<char>>`.
- **RazorSlices**: `[DynamicDependency]` on the program's and packages' members is honoured (constructors,
  properties, fields and methods, as NativeAOT keeps them: a listed method's
  invoker and delegate function, which compile it, are made only once the program can invoke methods
  through reflection, `ModuleCodeGen.LazyInvokers.cs`; the Platform entry can (the framework code it runs
  calls `MethodBase.Invoke`), so its slice's ~100 `Write` overloads are compiled, +0.8 % at -O0; what they reach that csharpc
  can't compile yet — BigInteger's `ISpanFormattable` — is an unreached method's run-time stub, not a
  compile error), a `Type` known at compile time stored into a
  `[DynamicallyAccessedMembers]` field keeps those members, `TextEncoder`'s TextWriter/span/UTF-8 members
  (also for encoders derived from it), `Utf8.FromUtf16`/`ToUtf16`, `MetadataUpdater.IsSupported` folded to
  false (no hot reload, as under NativeAOT); `NullabilityInfoContext.Create` is an explicit error (the members' `[Nullable]` data it reads is reproduced now, [ADR 0018](adr/0018-reflection-metadata.md); the context itself is follow-up work).
- **Hosting and Kestrel**: hosting startups read `assembly.GetCustomAttributes<T>()`; `UseStartup<T>()`'s
  `Activator.CreateInstance(Type, object[])`; generic method definitions found through reflection are
  described (their signature, invocation and `MakeGenericMethod` are explicit errors);
  `Assembly.DefinedTypes`/`GetTypes()` are explicit errors (the `startupAssembly` setting).
  `QuicListener.IsSupported` is false where MsQuic isn't installed, as .NET answers, folding Kestrel's
  HTTP/3 transport away; where MsQuic is installed csharpc stops with an explicit error. TLS exists on Linux
  since 2026-11-06 and on Windows (over OpenSSL 3 DLLs) since 2026-11-07 ([ADR 0022](adr/0022-tls.md); Kestrel's HTTPS through it is not tested yet, its certificate
  loader's post-quantum keys and `SslStreamCertificateContext` are `NotSupportedException`s); elsewhere
  certificates and SSL streams throw `NotSupportedException`.

## HttpClient

`System.Net.Http.HttpClient` works over HTTP/1.1, plain and over TLS. `System.Net.Http` is compiled from the
shared framework's IL ([ADR 0013](adr/0013-framework-il.md)): the whole of SocketsHttpHandler (connection pool,
header parsing, redirects, cookies, authentication, decompression, proxy support) and `System.Net.Http.Json`
compile, and what the samples exercise is checked against .NET. Samples 1000-1009 run it against a `TcpListener`
server in the same program (no network) and compare with `dotnet run`: GET / POST /
PUT / PATCH / DELETE and custom methods, `StringContent`, `ByteArrayContent`, `FormUrlEncodedContent`,
`MultipartFormDataContent`, `StreamContent` of known and unknown length, `DefaultRequestHeaders`, response and content
headers, `HttpCompletionOption.ResponseHeadersRead`, parallel requests over bounded connections, large bodies,
close-delimited HTTP/1.0 responses, the synchronous `Send`, handlers (`HttpClientHandler`, `SocketsHttpHandler`,
custom `DelegatingHandler`s), cookies, absolute redirects, gzip and deflate decompression, Basic authentication,
`GetFromJsonAsync` /
`PostAsJsonAsync` with a source-generated context, and the exception types and messages of refused connections,
cancellation, `HttpClient.Timeout`, hung-up and garbage responses, bad URIs and a disposed client.

What it took, besides library members (`Socket.Poll`, `DnsEndPoint` connects, `SpinLock`, `SecureString`,
`EventCounter`, `NetworkChange`, `Encoding.CreateTranscodingStream`, digest hashing ...):

- **Classes deriving from `TaskCompletionSource(<T>)`**: the connection waiters and write-queue entries of the
  pool. A derived class is laid out over csrt's task struct ([10-async.md](10-async.md#tasks-and-the-scheduler)).
- **System.Net.Primitives from IL** (a *mixed* assembly, ADR 0013): `CookieContainer`, `CredentialCache`, `NetworkCredential`,
  `DnsEndPoint`, `HttpVersion` are .NET's own code, and since #326 `IPAddress`, `IPEndPoint` and `IPNetwork` too; only
  `SocketException` and the sockaddr shims stay with the socket engine.
- **Awaits on socket operations continue on the thread pool.** The epoll thread used to complete Task-based
  operations inline, so an `await` continuation (and the rest of `Main`) ran on it; one synchronous socket call there
  (`HttpClient.Send` after an `await`) blocked the only thread that delivers readiness, a deadlock (sample 1008).
  `SocketAsyncEventArgs` operations (Kestrel's transport) still complete inline.
- Found on the way, independent of HTTP: `new Span<T>(null, 0, 0)` and `AsSpan(null, 0)` threw a
  NullReferenceException instead of being the empty span (sample 1004), and an array viewed as `IList` with no
  conversion instruction in IL (non-generic `SortedList`, which `CookieContainer` uses) was an InvalidCastException
  (sample 1006).

**The system proxy on macOS and Windows** is .NET's own code, reading the OS settings the way .NET does (the environment
variables first, then `MacProxy` over CFNetwork / `HttpWindowsProxy` over WinINet and the registry):

- *macOS:* `Marshal.GetFunctionPointerForDelegate` for CFNetwork's PAC callback (generated reverse P/Invoke thunks, see
  [16-interop-io.md](16-interop-io.md)), `SafeCreateHandle` and the CoreFoundation imports run as they are.
- *Windows:* `WinHttpGetIEProxyConfigForCurrentUser` / `WinHttpGetProxyForUrl` (auto-detect and PAC included) run as they
  are; `RegNotifyChangeKeyValue` signals a real OS event that `WaitHandle.SafeWaitHandle` creates and a bridge thread
  forwards to the runtime's event, and `ThreadPool.RegisterWaitForSingleObject` (one background waiter per registration)
  reloads the settings when the registry changes. A `"<local>"` bypass lists the machine's addresses
  (`NetworkInterface.GetAllNetworkInterfaces`: `GetAdaptersAddresses` on Windows, `getifaddrs` on Linux and macOS;
  only the unicast addresses are exposed, every other member of those types is CSLLVM0002).
  `System.Security.Principal.Windows` (`WindowsIdentity`, reached by NTLM/Negotiate code and by the native TLS route's
  `SslStream`) compiles; its `AsyncLocal<SafeAccessTokenHandle>` value-changed handler is called when `Value` is
  assigned, not when an execution-context switch changes the value (csharpc's flows do not track that), which only
  matters to impersonation (`WindowsIdentity.RunImpersonated`).

Explicit diagnostics, never a different behavior:

- **TLS** works on Linux, macOS and Windows over OpenSSL 3 (see "SslStream and https" below). HTTP/2 needs ALPN `h2` negotiated
  by a server; HTTP/3 and QUIC are stubs (HTTP/2 over TLS and over cleartext "prior knowledge" is not tested).
- **NTLM / Negotiate** authentication (`NegotiateAuthentication` needs GSSAPI/SSPI): `NotSupportedException` when a
  server challenges with it and the handler has credentials.
- `Dns.GetHostEntry*` (canonical names and aliases are not resolved): `NotSupportedException`.
- `NetworkChange.NetworkAddressChanged` accepts subscriptions and never fires (the pool only uses it to drop its
  Alt-Svc cache).
- **Windows:** gzip, deflate and Brotli decompression run over .NET's own zlib-ng and brotli, linked from the NativeAOT
  win-x64 pack (sample 1005, [16-interop-io.md](16-interop-io.md#binding)), and so do `ZipArchive` and `ZipFile`.
- **Windows:** a rooted request path (`"/absolute/path"`) is a relative `Uri` there, as in .NET, so `BaseAddress` + path
  works (sample 1010); UNC paths and `file:` URIs without a drive letter stay `NotSupportedException`.
- `Uri` is the corelib's: a relative request URI that has a query or escaped characters (`"items?id=7"`, `"with space"`) is the
  Uri shadow's explicit `NotSupportedException` ("System.Uri with this relative URI").
- Stack traces of exceptions thrown by `System.Net.Http` list its frames as .NET does only where csharpc compiles them
  from IL the same way; `ExceptionDispatchInfo.SetCurrentStackTrace` returns its argument unchanged.

Not yet measured: HttpClient's speed against NativeAOT's (the pool, header parsing and `Task` machinery are the same
IL; the sockets under it are csharpc's). A program that does one `GetStringAsync` is 3.2 MB at `-O2` (`tests/size.sh` has
no HttpClient program yet); the budgets of the programs it has are unchanged.

## SslStream and https (Linux, macOS and Windows)

`SslStream` (client and server), `AuthenticatedStream` and `HttpClient` over `https://` work on Linux, macOS and Windows ([ADR 0022](adr/0022-tls.md);
on macOS Homebrew's `openssl@3` is needed, on Windows the OpenSSL 3 DLLs `libssl-3-x64.dll` and `libcrypto-3-x64.dll` must be next to the executable or on `PATH`):
libssl 3 under corelib C#, with .NET's validation logic, policy errors and exception messages. Samples `1101_sslstream_loopback`
(handshake properties and callbacks, synchronous and asynchronous I/O, a 1 MB round trip, TLS 1.2/1.3 selection, mutual
authentication, ALPN, the unauthenticated state, close-notify), `1102_sslstream_validation` (untrusted root, a refusing callback,
host name mismatch, custom root trust through `CertificateChainPolicy`, protocol mismatch, plain-text and garbage peers, a missing
client certificate, invalid server certificates, callbacks given twice) and `1103_httpclient_https` (GET/POST and connection reuse over
one TLS session against an `SslStream` server, `ServerCertificateCustomValidationCallback`, `DangerousAcceptAnyServerCertificateValidator`,
`SslOptions.CertificateChainPolicy`, the `HttpRequestException` of an untrusted or mismatched certificate and of a server that
doesn't speak TLS) run against .NET's recordings, in a program with both ends and no network. Explicit diagnostics, a known
difference (client session resumption) and the platforms are in the ADR. `Socket.ConnectAsync(DnsEndPoint)` for `localhost`
falls back from `::1` to `127.0.0.1` as .NET does, so an IPv4-only listener is reached (samples 1180–1182).

## X.509 certificates (Linux, macOS and Windows)

`X509Certificate`, `X509Certificate2`, `X509Certificate2Collection`, `X509CertificateLoader`, `X509Chain` and the pieces under them
(`X500DistinguishedName`, `AsnEncodedData`, `Oid`, `PublicKey`, `X509ChainPolicy`/`Element`/`Status`) are corelib C# over OpenSSL 3
([ADR 0022](adr/0022-tls.md): why not the framework's IL). Sample `1100_x509_certificates` is recorded from .NET and covers: loading a
PKCS#12 (`X509CertificateLoader.LoadPkcs12*`, the obsolete constructors), DER and PEM (`CreateFromPem` with a key); `Subject`/`Issuer`
(.NET's `CN=a, O=b, C=c` order and quoting), serial number (both byte orders), thumbprint and `GetCertHash(HashAlgorithmName)`,
`NotBefore`/`NotAfter`, version, signature and key algorithms, `GetNameInfo` for names with a CN, `RawData`, `Export(Cert)`,
`ExportCertificatePem`, `GetCertContentType`, equality and hash codes; `GetRSAPrivateKey()` / `GetRSAPublicKey()` (the private key
stays an OpenSSL `EVP_PKEY`, shared with the RSA object); collections (`Add`, `AddRange`, `Contains`, `IndexOf`, `Remove`,
`Find` by thumbprint, serial number, subject and issuer); `X509Chain.Build` with OpenSSL's verification: `UntrustedRoot` with
OpenSSL's text as the status information (what .NET shows on Linux), `CustomRootTrust`, `AllowUnknownCertificateAuthority` (the
status is still reported, `Build` is true), `VerificationTime` and `NotTimeValid`. Errors keep .NET's types and messages: a wrong
PFX password, garbage data, a missing file (OpenSSL's `error:10000080:BIO routines::no such file`), a disposed certificate.

Explicit diagnostics: `X509Certificate2.Extensions` and the typed extension classes (X509KeyUsageExtension, ...), `X509Store`,
PKCS#7, exporting a PFX, `CopyWithPrivateKey`, `ToString(true)` of a `X509Certificate2` (it lists the extensions),
`GetNameInfo` of names without CN/OU/O, revocation checking (`X509Chain` needs `RevocationMode.NoCheck`), ECDSA/DSA keys.
On macOS the same code runs over Homebrew's `openssl@3` (ADR 0022, addendum: trust is OpenSSL's plus `/etc/ssl/cert.pem`, not the keychain; the machine needs `brew install openssl@3`). On Windows it runs over the OpenSSL 3 DLLs,
with the Windows `ROOT` certificate stores (current user, then local machine) as the trust anchors of
`X509ChainTrustMode.System` (sample `1231_x509_system_trust`); roots Windows downloads on demand and the `CA` (intermediate)
stores are not consulted, and `RSA.Create()` (sample 553) uses the same OpenSSL on Windows rather than CNG.

## Expression trees

csharpc runs expression trees the way NativeAOT does: `System.Linq.Expressions` is compiled from the
shared framework's IL ([ADR 0013](adr/0013-framework-il.md)) and `Compile()` runs its interpreter, because
`RuntimeFeature.IsDynamicCodeSupported` is false. Lambdas converted to `Expression<T>` (and, with C# 10's
natural types, to `Expression` / `LambdaExpression`) are lowered by `FunctionCodeGen.ExpressionTrees.cs` to
the factory calls Roslyn's `ExpressionLambdaRewriter` emits for IL, so the trees and their `ToString()` are
.NET's; code compiled by csc reaches the same factories through the IL front-end (`ldtoken` +
`MethodBase.GetMethodFromHandle` / `FieldInfo.GetFieldFromHandle`, real display classes).

- **What it took.** Feature switches that only forward to `RuntimeFeature.IsDynamicCodeSupported`
  (`LambdaExpression.CanCompileToIL`, `CallInstruction.CanCreateArbitraryDelegates`) fold like NativeAOT's
  constant propagation folds them: before, the LambdaCompiler and ~20,000 generic `FuncCallInstruction`
  instantiations were lowered, which is what timed out at 600 s. `CanEmitObjectArrayDelegate` is false (as
  NativeAOT's targets set it) and NativeAOT's `DynamicDelegateAugments.CreateObjectArrayDelegate` is a
  thunk per delegate type (`ModuleCodeGen.ObjectArrayDelegates.cs`). Reflection gained `FieldInfo`
  (standalone records, `GetField` with a constant name, `StrongBox<T>.Value`), delegate types' `Invoke`,
  per-member identity (every record of a method is one `MethodInfo`), static `MethodInfo.CreateDelegate`,
  `Delegate.DynamicInvoke`, value-type arrays through `Array.CreateInstance`/`GetValue`/`SetValue`, and
  CLR unboxing of enums as their underlying type.
- **Captured variables** are read live, through the closure environment presented as Roslyn's display
  class: `value(Program+<>c__DisplayClass1_0).y`, with Roslyn's method and closure ordinals and its
  `CS$<>8__locals{n}` parent links (`RoslynClosureModel` in `ModuleCodeGen.DisplayClasses.cs`).
- **Explicitly not supported yet** (a `CSLLVM0001` error, never a different tree): anonymous types,
  interpolated strings and method-group conversions inside a tree, rectangular array element access, `ref`/`out` arguments,
  delegate combination, trees inside lambdas or local functions that capture variables or `this`,
  generic display classes (captures in generic methods or types), several closure scopes in a release
  build (Roslyn merges them there), variables declared in other constructs than blocks, `for`, `foreach`
  and `using`. At run time, `NotSupportedException`: `Expression.Field/Property/Call` by name on members
  the program doesn't name (as under NativeAOT, where those overloads are `RequiresUnreferencedCode`),
  `MakeArrayType(rank)`, rectangular arrays through reflection.
- **Cost, pay for play.** Programs that don't use `System.Linq.Expressions` don't carry it. Most of
  what the interpreter used to bring was unreachable: overrides of classes nothing constructs (the
  LambdaCompiler's visitors through generic virtual `VisitLambda<T>`), every interface helper of every
  array type, expanded assembly-qualified type names, and method tables for the interfaces only
  `Type.GetInterfaces()` lists (docs/03-lowering.md, "Pruning"; `csrt_LazyInterface`). Compatibility run
  41177667628 (4 programs at a time on the runner) against 41177669294 on `main`: `lambda-expressions`
  2.24 MB / 29.7 s (was 3.88 MB / 51.0 s; NativeAOT 2.34 MB), `ExpressionTreeExample` 2.40 MB / 46.6 s
  (was 4.06 MB / 78.8 s; NativeAOT 3.04 MB). A minimal `Compile()` program: 2.09 MB, ~31 s at `-O2 -j 2`
  on the shared VPS (was 3.66 MB, ~63 s); `benchmarks/programs/exprtree.cs` tracks it in the Benchmarks
  workflow. Most of the remaining time is LLVM's `-O2` pipeline over the interpreter (~2,700 functions). Samples: `tests/samples/672_expression_trees_tostring`, `673_expression_trees_compile`,
  `674_expression_api`, `676_expression_trees_generics` (and `675_unbox_enum_base_object_calls` for the
  runtime fixes); IL front-end: `tests/projects/il/IlExpressionTreesApp`.
