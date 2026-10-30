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

- `ci/nursery-merge` — the opt-in nursery made sound (barriers on every store path, a GC nursery CI
  workflow) and an iterator-argument header fix for the default build. (Array covariance checks are on
  `main`; arrays of variant delegates and runtime-synthesized arrays are still unchecked.)

## Measured state

**NativeAOT parity:** 243 of 250 (97.2 %), no wrong outputs (run 40424843703). Left: expression trees (2),
`dynamic` (1), C# 15 unions (2), `System.Data` (1), deriving from `AssemblyLoadContext` (1).

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

1. M4: the seven remaining parity blockers (expression trees, `dynamic`, C# 15 unions, `System.Data`,
   `AssemblyLoadContext`).
2. M2: library gaps as programs reach them (`TimeSpan.Parse`, `Version.Parse`, `Type.GetField(s)`, ...).
3. M5: user code through the IL front-end (what `csc` hands the fork), with the Roslyn path kept for
   standalone use and debug info; gaps between the two are bugs.
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
