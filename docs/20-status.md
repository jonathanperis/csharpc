# 20 — Status and next steps (2026-10-30)

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
  `SetDllImportResolver`; csharpc lowers all of itself, code generation runs in the `Bootstrap`
  workflow ([06](06-roadmap.md#m6--bootstrapping)). Samples 720–729.
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

**TechEmpower** (`main` `9406394`, run 41089947567, 4-vCPU runner; ratios within one run):

| | NativeAOT | csharpc | ratio |
|---|---:|---:|---:|
| plaintext, 256 / 1024 connections | 528,049 / 475,789 req/s | 498,250 / 453,925 | 0.94 / 0.95 |
| JSON, 16 / 64 / 256 connections | 76.3k / 81.3k / 75.5k | 74.3k / 77.6k / 72.9k | 0.97 / 0.95 / 0.97 |
| JSON p99 at 256 connections | 6.41 ms | **4.82 ms** | |
| start-up | 30 ms | **23 ms** | |
| peak RSS idle → after load | 17.3 → 65.1 MiB | **14.2** → 76.5 MiB | |
| executable | 8.9 MiB | 15.6 MiB | |
| build | 24 s | 410 s | |

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
5. M7: TechEmpower with the `csharpc` CLI — throughput first, then memory under load, build time and size
   (the harness keeps measuring meanwhile).
6. M8: the fork; early gauge of its acceptance gate by building dotnet's AOT test suites with csharpc.
7. M9: TechEmpower through the fork.

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
