# 20 — Status and next steps (2026-10-31)

Where the work stands and what to pick up next. The plan is [06-roadmap.md](06-roadmap.md); the
direction is [00-vision.md](00-vision.md), [ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)
(NativeAOT parity, the dotnet fork, TechEmpower as the finish line) and [ADR 0017](adr/0017-runtime-tiers.md).

## What csharpc is, and the order of priorities

An LLVM front-end for C#, used standalone (like rustc) and as .NET's AOT layer in NativeAOT's place
behind `PublishAot`. The compiler is C#; the runtime keeps C where it is the right tool. Priorities, in
order: **speed, security, size**; behaviour byte-identical to .NET (an explicit "not supported" over any
silent difference); every fix with a regression sample recorded from real .NET.

## Roadmap order

M2 (library surface) → M4 (100 % NativeAOT parity) → M5/M6 (IL front-end as the primary path; csharpc
compiling Roslyn, then itself) → **M7** (TechEmpower: the standalone `csharpc` CLI's builds of the ASP.NET
Core entries beat NativeAOT's in the TechEmpower environment; started only once M2–M6 are done) → **M8**
(the dotnet fork with csharpc in NativeAOT's place behind `PublishAot`, accepted by dotnet's own AOT test
suites) → **M9**, the finish line (TechEmpower again, built through the fork's `PublishAot`, beating
NativeAOT). Renumbered 2026-10-30.

## `main`

Green on all five CI platforms. Versions restart at **0.1.0-alpha** (`0.MINOR.PATCH-alpha`): `main` builds
publish `0.1.0-alpha.<run>` to GitHub Releases (`latest-main`), GitHub Packages and GHCR; only `v*` tags
on a commit with a green full-gate CI publish to nuget.org. The earlier `0.1.0-spike*` packages are to be
unlisted (a `release.yml` dispatch with `nuget_unlist_except`). History from 2026-10-25 on was rewritten
on 2026-10-30 (authorship and message cleanup only; every tree unchanged).

Recent work: an audit of the 2026-10-25/26 changes fixed silent differences (TypeConverter, `GetInterfaces`,
`Activator.CreateInstance`, `IList<T>` on arrays), gated the unsound opt-in nursery, hardened the release
pipeline (tag gating, license texts, a 12-sample smoke test of the shipped NativeAOT build, installers),
and cut the TechEmpower binary from 17.4 to 15.6 MiB (shared array interface helpers, deduplicated
attribute factories) with a ~25 % faster build.

## Open branches

- `ci/m7-harness` — M7 step 1: the TechEmpower harness measures TechEmpower's real ASP.NET Core
  [Platform] entry (all seven test types on PostgreSQL, every level TechEmpower uses, TechEmpower's own
  verification), every metric side by side with a csharpc-vs-NativeAOT verdict
  ([06](06-roadmap.md#m7--techempower-csharpc-beats-nativeaot), "The harness"). TechEmpower run
  41214295059 green: **M7 finish line 0 of 456**, csharpc doesn't build the entry (44 methods from IL,
  the Razor generators from the project; [18](18-compatibility.md#techempowers-platform-entry)). The old
  plaintext/JSON app is `benchmarks/techempower/interim`, measured outside the verdict (23 of 106).
  Found: csharpc's `Socket.Bind` lacks .NET's `SO_REUSEADDR`.
- `ci/m7-perf` — M7 performance. Measured with `.github/workflows/m7-perf.yml` (the TechEmpower harness,
  unchanged, three load runs plus one profiled run per push to `ci/m7-perf*`; ratios csharpc/NativeAOT,
  medians of three). Baseline (run 41207247881, main `f622dc8`): plaintext 256/1024 0.983/0.967, JSON
  16/64/256 0.969/0.972/0.980, RSS after load 73.4 vs 64.8 MiB, executable 11.0 vs 8.9 MiB, build 188 vs
  21 s. Now (run 41214803825): plaintext 1.140/1.123, JSON 1.003/1.015/1.017, JSON p99 0.27–0.66 of
  NativeAOT's, start-up 21 vs 27 ms, idle RSS 13.2 vs 17.4 MiB, RSS after load 71.6 vs 64.9 MiB,
  executable 10.7 vs 8.9 MiB, build 208 vs 20 s. Changes: guarded devirtualization of interface calls
  (per-slot stubs filled once the constructed classes are known: +5–8 % plaintext), leaf-loop type tests,
  vector ASCII encoding / `Ascii.Equals` / transcoding, `ValueTask<T>` default results and IL ValueTask
  builders without a task, `AsTask()` of a completed ValueTask = `Task.CompletedTask`, one object per
  pending IValueTaskSource ValueTask, ILLink substitutions (UseSystemResourceKeys drops 670 KB of
  FxResources strings), and a collector crash fix (slots of a chunk that changed size class kept old
  fields where marking reads a method table: gclatency segfaulted in 1 of 10 runs). Fixed silent
  differences on the way: null tests on `T` = `int?` (sample 827), `default(ValueTask<Big>)` with
  results over 64 bytes (826). Profiling builds now print task allocation sites and the remaining cached
  interface dispatches. Next levers, by size of the data: `ValueTask<T>` as a two-word struct (64 % of
  the server's task allocations are synchronous ValueTask results: Pipe.ReadAsync, socket sends/receives,
  site profile of run 41214803825); a smaller collector budget (growth 100 %, 16 MiB: RSS 56 vs 65 MiB
  with throughput within noise, experiment runs 41215013272/41216692814, not adopted: it changes every
  program's GC frequency); guards by the call site's static receiver type (the cached dispatches left are
  monomorphic IFeatureCollection/ILogger calls on interfaces with more than three implementers); the
  optimizer's inliner (half of the 150–220 s `default<O2>` phase); type metadata (930 KB of
  `csharpc.typeinfo.entries`).
- `ci/techempower-json-switch` — TechEmpower compiles again (broken since `d42b5cc`: the IL check for
  reflection-based JSON also rejected the `JsonSerializer.IsReflectionEnabledByDefault` getter that
  ASP.NET Core's `JsonOptions()` reads; `tests/il.sh` IlAspNetCoreApp covers it), and the TechEmpower job now
  fails when a build fails (run 41179977525 had passed with only a blockers table). CI 41187053940 green;
  TechEmpower 41187053942: responses identical to NativeAOT; plaintext 503k / 496k vs 548k / 522k req/s
  (0.92 / 0.95), JSON 74.7k / 80.1k / 79.3k vs 76.5k / 80.4k / 79.8k (0.98 / 1.00 / 0.99), JSON p99 at 256
  4.20 vs 6.04 ms, start-up 21 vs 27 ms, RSS 12.8 → 84.4 vs 17.4 → 64.3 MiB, executable 11.1 MiB (was 15.6)
  vs 8.9, build 240 s (was 410) vs 20 s.

- `ci/expression-tree-size` — M7 build time and size: method-table slots only for constructed classes
  (rapid type analysis), generic virtual methods through dispatchers over constructed classes, array/string
  interface helpers per dispatched member, invariant interfaces dispatched per instantiation, FullNames
  composed at run time, lazily described `GetInterfaces()` interfaces. Expression-tree programs are now
  smaller than NativeAOT's and compile in about half the time (docs/18-compatibility.md, "Expression trees";
  compat runs 41177667628 vs 41177669294); `benchmarks/programs/exprtree.cs` tracks them. Samples 780–782.
  TechEmpower doesn't compile on `main` either (run 41179977525: `JsonOptions()` reaches reflection JSON),
  so its size couldn't be compared.

- `ci/m6-bootstrap` — M6: a Roslyn-hosting program compiled from IL with output identical to
  `dotnet run` (`Bootstrap` workflow), Roslyn survey 101 → 38 failing methods, lowering 2.4× faster on
  Roslyn-sized programs, generic variance through interface arguments fixed (a silent difference),
  overlapping/packed struct layouts, compiled-in manifest resources, `ResourceReader`,
  `SetDllImportResolver`, `Process.Start` on Linux/macOS; **csharpc compiles itself** and the native
  compiler compiles samples exactly as the managed one (`Bootstrap` workflow,
  [06](06-roadmap.md#m6--bootstrapping)). `StringBuilder` is .NET's chunked builder (exact `GetChunks`,
  `Capacity`, `EnsureCapacity`; sample 732). Samples 720–732.
- `ci/m2-system-data` — Roslyn 5.12 (the SDK's build), C# 15 unions, field reflection (`GetField(s)`,
  `FieldInfo`), `AssemblyLoadContext`/`AssemblyDependencyResolver` as NativeAOT has them,
  `Assembly.GetType` with constant names kept at compile time, `PropertyDescriptor`/`AttributeCollection`
  ports, BigInteger↔decimal, `TimeSpan`/`Version` parsing, `ReaderWriterLockSlim`, `BitArray`,
  `SerializationInfo`, JSON `Guid`. Locally it clears four parity blockers (both union programs,
  `System.Data`, `AssemblyLoadContext`).
- `ci/expression-trees` — expression trees, the other remaining parity blocker, in progress.
- `ci/il-main` — M5, the program as IL: `csharpc app.dll` (entry point from the CLI header, the IL
  assembly's identity, module initializers, runtimeconfig.json next to it) and `tests/run.sh --via-il`,
  which runs every sample through `dotnet build` → csharpc (informational CI workflow `il-main.yml`,
  `tests/il-main-known-failures.txt`). 459/476 samples pass that way, stack traces included; the first
  runs found 72 failures, 19 of them wrong output, now fixed (ADR 0010, "The program as IL"). Samples 700–702.
- `ci/il-gaps` — M5, what IL programs still lacked: reflection-based JSON metadata from the program's bodies
  (the program grants the binding compilation internals access), `[GeneratedRegex]` members as cached
  interpreters, non-generic enumeration of rectangular arrays (which also stopped them claiming
  `IEnumerable<T>`), and the source path's `[GeneratedRegex]` instance sharing. 471/479 via IL. Samples 740–742.
- `ci/dynamic` — `dynamic` bound at compile time with Roslyn, per run-time type ([ADR 0019](adr/0019-dynamic.md)):
  members, overloads by run-time type, operators, conversions, indexers, events, delegates, ExpandoObject, DynamicObject,
  binder errors with Microsoft.CSharp's messages; samples 680–687; both corpus programs that use `dynamic` pass locally.
- `ci/il-dynamic` — M5, `dynamic` from IL: csc's call sites decoded into the source path's sites and bound
  in a synthesized context that ignores accessibility, with the call site's real context checked on top
  ([ADR 0019](adr/0019-dynamic.md), "From IL"); IL libraries' dynamic code too (IlDynamicApp). Also fixes the
  binder's compound-assignment semantics on both paths (`d += 0.5` on a dynamic int was 2, now 2.5) and a
  simple-name dynamic call in a static member (rejected before). 480/481 via IL. Samples 760–761.

## Measured state

**NativeAOT parity: 249 of 249 (100 %)**, no wrong outputs (run 41152809617, `main` `d572f27`, 2026-10-30) —
M4's target. Against NativeAOT on those 249: compile time median 4.0 s vs 5.2 s (csharpc faster in 183),
executable median 141 KiB vs 1,338 KiB (smaller in 245); slowest compile 128.5 s vs 16.9 s (the
expression-tree programs: the M7 build-time work). 271 of the 272 programs csharpc compiles behave like .NET;
the one difference is a stack-trace frame in a NativeAOT-unsupported program.

**TechEmpower** (`ci/m7-harness`, run 41214295059, 4-vCPU runners): TechEmpower's ASP.NET Core
[Platform] entry, 456 compared metrics. **M7 finish line: 0 of 456 metrics beat NativeAOT**: csharpc
doesn't build the entry yet (blockers in [18](18-compatibility.md#techempowers-platform-entry)). NativeAOT
passes TechEmpower's verification on all seven test types (2.46 M plaintext req/s at 256 connections,
165 k JSON, 551 ms start-up including its 10 000-query cache fill, 16.4 MiB, 32.7 MiB image). On the
interim plaintext/JSON app, ratios within one runner:

| | NativeAOT | csharpc | verdict |
|---|---:|---:|---|
| plaintext pipelined, 256 / 1024 / 4096 / 16384 connections | 558k / 529k / 511k / 471k req/s | 553k / 514k / 475k / 449k | tie / worse ×3 |
| JSON, 16–512 connections | 149k–160k req/s | 141k–149k | worse ×5, tie at 512 |
| start-up / idle RSS | 23 ms / 17.7 MiB | **11 ms / 12.9 MiB** | better |
| RSS, plaintext 16384 / JSON 512 | 260 / 81 MiB | 744 / **57** MiB | worse / better |
| executable / image / build | 8.9 MiB / 25.2 MiB / 16 s | 11.0 MiB / 27.3 MiB / 154 s | worse |

## Next, in order

1. M4: merge the branches above, then rerun the Compatibility workflow for the parity number.
2. M2: library gaps as programs reach them.
3. M5: user code through the IL front-end (what `csc` hands the fork). `csharpc app.dll` and the
   `--via-il` differential mode exist (471/479 with `ci/il-gaps`: reflection-based JSON, `[GeneratedRegex]`
   and rectangular-array enumeration from IL); `dynamic` from IL with `ci/il-dynamic` (480/481); left:
   JSON of a private nested type (1, stays a diagnostic); then `-g` from the PDB. The Roslyn
   path stays for standalone use; gaps between the two are bugs.
4. M6: Roslyn's remaining blockers (non-generic ~170 methods, then generic methods), then csharpc
   compiling itself.
5. M7: TechEmpower with the `csharpc` CLI. First build the real entry (the blockers in
   [18](18-compatibility.md#techempowers-platform-entry): Npgsql, `MemoryCache`, RazorSlices, `UseStartup`;
   Razor generators for the project input; frameworks from runtimeconfig.json for IL input), then every
   metric the harness reports as worse or tied: throughput first, then memory under load, build time and size.
6. M8: the fork; early gauge of its acceptance gate by building dotnet's AOT test suites with csharpc.
7. M9: TechEmpower through the fork.

Later, when it matters: a `StringBuilder` that keeps .NET's chunks but not their linked list. Insert and
Replace(string) are slower than the old single buffer because they must keep .NET's layout (40K inserts:
55 → 296 ms with 8,429 chunks; 60 growing/shrinking replaces: 39 → 117 ms with 67,470 chunks; both still
faster than .NET's 400 ms and 101 s). Every insert shifts the stored offset of each later chunk, and
finding a chunk walks the predecessor links. Holding the chunks in an array with lengths in a prefix-sum
(Fenwick) tree would make finding a chunk and shifting offsets O(log n), and inserting a chunk one pointer
memmove, while `GetChunks`/`Capacity` stay exactly .NET's. The builder object is .NET's last chunk today,
so this is a redesign of `corelib/StringBuilder.cs`, not a patch.

## Working rules

Build with `dotnet build src/Csharpc -c Release -m:1`, compile with `-j2`, run heavy suites on CI,
and poll CI slowly. After corelib edits run `CSHARPC_CHECK_CORELIB=1` (compiles every corelib member).
Merge only CI-verified branches. Delete merged branches.

CI (`.github/workflows/ci.yml`) runs on push to `main` and `ci/**`, on pull requests, and on a manual
full dispatch. The merge bar is still `test (x86-64)`, `test (arm64)`, `test (macos-arm64)`,
`test (windows-x64)` and `test (wasm32-wasi)`. A newer push to the same branch cancels the running
run. A docs-only push skips the suite only when an ancestor's five checks are already green;
cancelling an in-progress full run leaves that ancestor not green, so the replacement runs the
suite. Avoid docs-only pushes to `main` while a merge's CI is in progress anyway.

A measurement branch is named `ci/<topic>` (the `ci/**` push filter on `ci.yml` and `bench.yml`).
That does not change `main`: a push to `main` still runs CI. The Benchmarks workflow still also
requires a path change under `src/`, `benchmarks/` or its own file. TechEmpower has no branch filter,
only those paths, so a `ci/**` branch that touches `benchmarks/techempower/**` or the workflow file
runs it too; do not add a branch filter there.

`gh workflow run` works from this checkout again (Compatibility was dispatched on 2026-10-27). Targeted runs
are a push to `ci/**` with a `CI-Only:` trailer or a `ci/targets` file
([05 — Testing](05-testing.md)). A full gate has neither. Partial runs are labeled `partial (...)`
and do not satisfy the merge bar. Dispatching the workflow named CI, with a token that can, is a
full gate; the targeted form is the workflow CI (targeted).
