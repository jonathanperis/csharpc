# 20 — Status

Where the project stands today. The plan and the milestone definitions are in [06-roadmap.md](06-roadmap.md); the
direction is [00-vision.md](00-vision.md), [ADR 0016](adr/0016-nativeaot-parity-and-end-state.md) (NativeAOT parity,
the dotnet fork, TechEmpower as the finish line) and [ADR 0017](adr/0017-runtime-tiers.md). Last updated 2026-11-07,
`main` at `6b95c16`.

## What csharpc is, and the order of priorities

An LLVM front-end for C#, used standalone (like rustc) and as .NET's AOT layer in NativeAOT's place
behind `PublishAot`. The compiler is C#; the runtime keeps C where it is the right tool. Priorities, in
order: **speed, security, size**; behaviour byte-identical to .NET (an explicit "not supported" over any
silent difference); every fix with a regression sample recorded from real .NET.

## Milestone status

The roadmap order is M2–M6 first, then M7 (TechEmpower with the standalone `csharpc` CLI), M8 (the dotnet fork with
csharpc in NativeAOT's place, accepted by dotnet's own AOT test suites) and M9 (the finish line: TechEmpower again,
built through the fork's `PublishAot`). A milestone is done only when none of its bullets is open.

| Milestone | Status | Evidence | Open |
|---|---|---|---|
| M0, M1 | done | 663 differential samples, 195 conformance programs, benchmarks | |
| M2 library | open | the surface of [06-roadmap.md](06-roadmap.md#m2--library-in-c) is implemented on Linux; macOS/Windows TLS over OpenSSL | #146 (survey), #143, #144, #89, #95–#104 |
| M3 platforms | done | five `test (<platform>)` CI jobs green on `main` | |
| M4 validation | open | NativeAOT parity 242 of 246 (98.4 %) in the latest Compatibility run, 249 of 249 before | #163 |
| M5 packages and program as IL | open | `csharpc app.dll` and `--via-il` pass every sample; `-g` from the PDB; `calli`; RID assets | #175, #146 |
| M6 bootstrapping | open | csharpc compiles itself at -O0 and -O2; the native compiler passes every sample | #174, #93 |
| M7 TechEmpower (CLI) | on hold | harness and performance work on `main`; 288 of 456 metrics ahead on 2026-11-02, none buildable on 2026-11-07 | #164 |
| M8, M9 | not started | | |

## `main`

CI is green on all five platforms at `6b95c16`, and so are the `Bootstrap` (self-compile, native
compiler on the whole suite) and `Program as IL` workflows. Versions are `0.MINOR.PATCH-alpha`: `main` builds publish
`0.1.0-alpha.<run>` to GitHub Releases (`latest-main`), GitHub Packages and GHCR; only `v*` tags on a commit with a
green full-gate CI publish to nuget.org ([06-roadmap.md](06-roadmap.md#credibility-deliverables-cross-cutting)).

Two CI measurements have regressed and are tracked as bugs: the `TechEmpower` workflow is red because csharpc no longer
builds the Platform entry (#164), and the parity figure of the `Compatibility` workflow has been 98.4 % since 2026-11-01 (#163).

## Measured state

**NativeAOT parity** (`Compatibility` workflow, scheduled run of 2026-11-01, 320 programs from dotnet/samples and
dotnet/docs): 242 of 246 programs NativeAOT publishes and runs like the JIT also match with csharpc (98.4 %); the four
others are compile errors (#163), none a wrong output. 249 of 249 on 2026-10-30. Against NativeAOT on the 242: executable
median 143 KiB vs 1,338 KiB (smaller in 239); compile time median 10.3 s vs 8.6 s (faster in 53), slowest 111.2 s vs 24.2 s.
csharpc passes 22 of the 29 programs NativeAOT can't run. Details: [18-compatibility.md](18-compatibility.md).

**TechEmpower** (`TechEmpower` workflow, 4-vCPU runners, TechEmpower's ASP.NET Core Platform entry, 456 compared
metrics): on `main` on 2026-11-02, 288 beat NativeAOT, 135 tied and 33 were worse, with zero errors at all 37 load
levels. On 2026-11-07 csharpc could not build the entry (0 of 456, #164). Ahead: start-up (521 vs 611 ms), plaintext
latency, JSON, multiple queries, fortunes. Worse: idle RSS (42.4 vs 37.6 MiB), executable (18.3 vs 16.4 MiB), build time
(134 vs 29 s), image size (34.6 vs 32.7 MiB), plaintext at 256 connections (0.93), 12 of 61 cached-query metrics. The
concurrent marker (`--gc-concurrent`) brings pauses from 13–23 ms to p50 0.4 ms and stays off by default because it costs
about 5 % of plaintext throughput ([ADR 0020](adr/0020-concurrent-marking.md)).

**Self-compile** (`Bootstrap` workflow): 192 s at -O0 and 610 s at -O2 on a CI runner, 8.7 GB peak, native compilers of
124 MB and 89 MB, both passing every sample of the differential suite at -O0 and -O2
([06-roadmap.md](06-roadmap.md#m6--bootstrapping)).

**Benchmarks:** [benchmarks/RESULTS.md](../benchmarks/RESULTS.md); NativeAOT is ahead on `asyncfanout` and, on arm64, on
`fileio`, `quicksort`, `shapes` and `spectralnorm`.

## What is open

Everything listed here is a bullet of M2–M6; M7 and later wait for the owner's go.

- **Bugs found by the CI measurements:** #163 (parity 98.4 %), #164 (TechEmpower entry does not build). Also open: #162
  (a static local function called from a generic local function with an `IFormattable` parameter).
- **BCL surface ranked by the NuGet survey (#146):** `MathF` and the floating-point generic-math statics (#152, PR #156),
  reflection lookups (#157, PR #158; #159).
- **The gap lists from a large WinForms/macOS application:** #89, #93 (quadratic lowering), #95–#104.
- **Native TLS routes on macOS and Windows:** #144, #143 (the OpenSSL route works and stays until they land).
- **M5 leftovers:** #175 (static archives). **M6 leftovers:** #174 (`Mutex`), #93.
- **Platform limits that are explicit diagnostics:** async socket operations on macOS/Windows block pool threads;
  `FileSystemWatcher` on macOS/Windows; `PosixSignalRegistration` on Windows; no sockets, TLS, compression or OS ciphers
  on WebAssembly (`known gap` entries in `tests/platform-skips.txt`).

## History

- 2026-10-22..30: first compatibility report (40.6 % identical to .NET), NativeAOT parity as the metric (80.6 % -> 100 %),
  expression trees, `dynamic`, the program as IL, release pipeline, versions restarted at 0.1.0-alpha. History from
  2026-10-25 on was rewritten on 2026-10-30 (authorship and message cleanup only; every tree unchanged).
- 2026-10-31..11-02: M7 harness and performance work (the owner put M7 on hold on 2026-11-05, see the roadmap), stack traces
  through IL-compiled code, concurrent marking, csharpc compiles itself.
- 2026-11-05..07: the issues #18–#32, finalizers and `ObjectiveCMarshal`, TLS, reflection, `calli`, RID assets, `-g` from
  the PDB, self-compile at -O2, the native compiler on all samples, bounded code-generation memory, parallel samples in CI
  (`CSHARPC_JOBS`), the issue-first workflow in [AGENTS.md](../AGENTS.md).

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
