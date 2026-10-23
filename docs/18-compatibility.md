# 18 — Real-world compatibility report

How much real C# code does csharpc compile and run exactly like .NET today, and which missing
features block the most programs? This is the first measurement for milestone M4 (real-world
validation, [06-roadmap.md](06-roadmap.md)). The differential samples and the conformance corpus
([09-conformance.md](09-conformance.md)) are written *for* csharpc. This corpus is not: it is
unmodified console programs from Microsoft's documentation and sample repositories.

Latest run of 2026-10-22: csharpc `libgaps` at `36fd745`, .NET SDK `11.0.100-rc.2.26504.105`, Linux
x86-64. The first run (csharpc `integ` at `8183dfb`, same day) is the baseline the blocker tables
below were measured on; [Progress](#progress-since-the-first-run) lists what changed since.

## Summary

| | first run (`8183dfb`) | latest run (`36fd745`) |
|---|---:|---:|
| corpus (candidate console programs) | 323 | 323 |
| skipped (non-deterministic or broken under .NET itself) | 40 | 43 |
| **compared** | **283** | **280** |
| **PASS** (byte-identical stdout, same exit code) | **115 (40.6 %)** | **147 (52.5 %)** |
| COMPILE-UNSUPPORTED (explicit `CSLLVM0001/0002` diagnostic) | 135 (47.7 %) | 95 (33.9 %) |
| COMPILE-ERROR (compile fails some other way) | 22 (7.8 %) | 22 (7.9 %) |
| COMPILE-CRASH (internal compiler error, crash) | 3 (1.1 %) | 4 (1.4 %) |
| RUNTIME-DIFF (wrong output or exit code) | 7 (2.5 %) | 11 (3.9 %) |
| RUNTIME-CRASH (native binary crashed, .NET didn't) | 1 (0.4 %) | 1 (0.4 %) |

- **52.5 %** of the compared programs pass (first run: 40.6 %). By corpus: dotnet/samples 38 of 45
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
| 7 | **`System.Globalization`** | 10 | 3 | `CultureInfo.Name` (3), `new CultureInfo(..)`, `GetCultureInfo`, setting `CurrentCulture`, `IdnMapping` | corelib C# (invariant-only `CultureInfo` object model, plus explicit diagnostics for real cultures) |
| 8 | **`System.Threading`** | 10 | 3 | `Task.Run(Action, CancellationToken)`, `Task.Wait(int)`, `WaitAsync`, `ContinueWith`, `Task.Id`, `new CancellationTokenSource`, `CreateLinkedTokenSource`, `SynchronizationContext`, `Timer`, `Thread.Interrupt` | corelib C# (tasks) + runtime (timers) |
| 9 | **`Console` input and encoding** | 9 | 7 | `Console.ReadKey()` (6), `ConsoleKeyInfo`, setting `Console.OutputEncoding` (2) | runtime + corelib: `ReadKey` with redirected stdin must throw `InvalidOperationException` like .NET. `OutputEncoding` = UTF-8 can be a no-op |
| 10 | **`System.Text.RegularExpressions`** | 9 | 3 | `MatchCollection` enumeration and `Count` (4), `Capture.Value`, `new Regex`, `Regex.IsMatch/Match/Replace` | done on `regex` (port of .NET's engine): 5 more programs pass; the others stop at `ThreadLocal<T>`, `IdnMapping`, `Convert.ToUInt16(char)`, formatting `ReadOnlySpan<char>`, `DateTimeOffset`, `JsonConverter<T>` |
| 11 | **non-generic and concurrent collections** | 8 | 2 | `Hashtable`, `ArrayList`, `ReadOnlyCollection<T>`, `BlockingCollection<T>`, `ConcurrentDictionary` members, `OrderedDictionary<,>.TryAdd` | corelib C# |
| 12 | **`StringBuilder.Append($"...")`** | 8 | 6 | `StringBuilder.AppendInterpolatedStringHandler` (7), `DefaultInterpolatedStringHandler` (1) | codegen (lower the handler pattern like plain interpolation) or a corelib shadow struct. High value: 6 programs need nothing else |
| 13 | **`DateTime.Parse`, `DateOnly`, `TimeOnly`** | 7 | 4 | `DateTime.Parse(string)` (5), `DateOnly` (2), `TimeOnly` | corelib C# (`DateTime` is moving to C#, per M2) |
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
| `<FrameworkReference Include="Microsoft.AspNetCore.App">` ignored | 2 | console programs using `Microsoft.Extensions.Configuration`. csharpc should say it doesn't support the framework reference, instead of failing with Roslyn `CS0246` |
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

Still open from step 3: `new Random(seed)` (2 programs) and `Debug.Assert(bool, string)` (3).
`Array.GetEnumerator()` (non-generic `foreach` over an `Array` returned by `Enum.GetValues`)
needs the element type at run time and stays a diagnostic.

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
| [`exception_tostring_stack_trace.cs`](../tools/compat/repros/exception_tostring_stack_trace.cs) | docs `standard/exceptions/how-to-use-the-try-catch-block-to-catch-exceptions`, and since the latest run the `BankAccount` tutorials (docs `fundamentals/tutorials/{introduction-to-classes,object-oriented-programming,xml-documentation}`, samples `classes-quickstart`) | `Exception.ToString()` has no stack-trace lines. Byte-identical stack traces are not achievable, but the difference is silent. The program prints `$"... {e}"` | not fixable byte for byte. The output stays as is, and the compiler now warns with `CSLLVM0006` wherever a program turns an exception into text ([04-runtime.md](04-runtime.md)) |

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
5. Then Regex, Globalization's `CultureInfo` object model (invariant only), the remaining
   threading surface, and the IO extras. All of these are already on the M2 corelib list.

Re-measure after each batch. The blocker tables above are the first-run baseline; the summary has
the latest numbers.

## Re-running

On CI: the `Compatibility` workflow (`.github/workflows/compat.yml`, manual and weekly) runs the whole
corpus on a hosted runner and publishes the summary in the job summary and `results.tsv` /
`summary.md` as an artifact; copy the artifact's `results.tsv` into `tools/compat/` to publish a run.
Locally:

```sh
export PATH=$HOME/.dotnet11:$PATH
tools/compat/run.sh -j 2                    # fetch the pinned corpus, run all programs (~2.5 h on 4 shared cores)
tools/compat/run.sh --no-fetch -j 2 linq    # only program ids containing "linq"
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
