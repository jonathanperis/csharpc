# 20 — Status

Where the project stands today. The plan and the milestone definitions are in [06-roadmap.md](06-roadmap.md); the
direction is [00-vision.md](00-vision.md), [ADR 0016](adr/0016-nativeaot-parity-and-end-state.md) (NativeAOT parity,
the dotnet fork, TechEmpower as the finish line) and [ADR 0017](adr/0017-runtime-tiers.md). Last updated 2026-11-07; the CI state below
was read at `6b95c16`, the last commit with a full CI run.

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
| M4 validation | open | NativeAOT parity 242 of 246 (98.4 %) in the latest Compatibility run, 249 of 249 before; the four compile errors are fixed (#163), awaiting a run | #163 |
| M5 packages and program as IL | open | `csharpc app.dll` and `--via-il` pass every sample; `-g` from the PDB; `calli`; RID assets | #168, #146 |
| M6 bootstrapping | open | csharpc compiles itself at -O0 and -O2; the native compiler passes every sample | #169, #93 |
| M7 TechEmpower (CLI) | on hold | harness and performance work on `main`; 288 of 456 metrics ahead on 2026-11-02, none buildable on 2026-11-07 | #164 |
| M8, M9 | not started | | |

## `main`

CI was green on all five platforms at `6b95c16`, and so were the `Bootstrap` (self-compile, native
compiler on the whole suite) and `Program as IL` workflows. Versions are `0.MINOR.PATCH-alpha`: `main` builds publish
`0.1.0-alpha.<run>` to GitHub Releases (`latest-main`), GitHub Packages and GHCR; only `v*` tags on a commit with a
green full-gate CI publish to nuget.org ([06-roadmap.md](06-roadmap.md#credibility-deliverables-cross-cutting)).

Two CI measurements have regressed and are tracked as bugs: the `TechEmpower` workflow is red because csharpc no longer
builds the Platform entry (#164; the three diagnostics it listed are fixed, and 11 more, behind them, remain: the Npgsql
methods that reach `NegotiateAuthentication`, `SHA3_256`, `BigInteger.GetByteCount` ... are reachable through the pooled `IAsyncStateMachine` box), and the parity figure of the `Compatibility` workflow has been 98.4 % since 2026-11-01 (#163).

## Measured state

**NativeAOT parity** (`Compatibility` workflow, scheduled run of 2026-11-01, 320 programs from dotnet/samples and
dotnet/docs): 242 of 246 programs NativeAOT publishes and runs like the JIT also match with csharpc (98.4 %); the four
others are compile errors (fixed in #163, to be confirmed by the next run), none a wrong output. 249 of 249 on 2026-10-30. Against NativeAOT on the 242: executable
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
- **M5 leftovers:** #168. **M6 leftovers:** #169.
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

## How to resume

### State of `main`

Read the sections above first: the milestone table, the measured state and the open bullets. In short, at the time of
writing `main` is green on the five `test (<platform>)` jobs and on `Bootstrap` and `Program as IL`; the `TechEmpower`
workflow is red (#164) and the parity of the `Compatibility` workflow is 98.4 % (#163). Open pull requests: #156 (`MathF`,
for #152) and #158 (reflection and type-test members, for #157).

### The rules

- **Workflow:** [AGENTS.md](../AGENTS.md#workflow-issue-branch-pr-report): an issue first (labels, assignee, milestone), a
  branch, a pull request with `Fixes #N`, merge on the five green jobs, then a closing comment on the issue. Nothing goes to
  `main` directly. A stacked pull request is retargeted to `main` before its base merges (deleting the base branch closes
  it). `tests/platform-skips.txt` is append-only: on a conflict keep both sides.
- **Order of work (the owner's):** the open bullets of M2–M6 ([06-roadmap.md](06-roadmap.md)), the regressions #163 and #164
  first; then the gap lists from the large WinForms/macOS application (#89, #93, #95–#104); then stop. **M7 and later start only
  on the owner's explicit go.**
- **Every fix has a regression sample recorded from real .NET** (`tests/run.sh --regen NNN_name`); never edit a `.expected`
  file; a silent difference is the worst bug, an explicit "not supported by csharpc yet" is always better than guessing.
- **Commits and pull requests carry no AI or tool attribution** and no `Co-Authored-By` trailers.
- **Resources:** build with `dotnet build src/Csharpc -c Release -m:1`, compile with `-j2`, run the heavy suites on CI and
  poll CI slowly. After corelib edits run `CSHARPC_CHECK_CORELIB=1` (compiles every corelib member). Merge only CI-verified
  branches and delete merged ones.

### Running the suites

Locally (the full commands and environment are in [AGENTS.md](../AGENTS.md) and [05-testing.md](05-testing.md)):

| What | Command |
|---|---|
| Differential samples, -O0 and -O2 | `tests/run.sh --all-opts [name]` (`--shard I/N` for one CI shard, `CSHARPC_JOBS=N` for parallel samples) |
| The program as IL | `tests/run.sh --via-il -O0 [name]` |
| The native compiler (csharpc compiled by csharpc) | `tools/bootstrap/selfhost.sh`, then `CSHARPC_NATIVE=path tests/run.sh` |
| Conformance corpora | `tests/conformance.sh -j 4`, `tests/conformance2.sh` (manual) |
| Real-world programs, projects, IL libraries | `tests/programs.sh`, `tests/projects.sh`, `tests/il.sh` |
| Debug info, size budgets | `tests/debuginfo.sh` (Linux, gdb), `tests/size.sh` |
| WebAssembly | `CSHARPC_TEST_TARGET=wasm32-wasip1 tests/run.sh NAME` (WASI SDK 24 and wasmtime) |
| Benchmarks | `benchmarks/run.sh` |
| Compatibility corpus | `tools/compat/run.sh -j 2` (hours; use CI) |

On GitHub Actions (`.github/workflows/`): `CI` (the merge bar, on pull requests and pushes to `main` and `ci/**`),
`Compatibility` (weekly: parity), `Bootstrap` (self-compile and the native compiler on the whole suite), `Program as IL`,
`IL survey` (`target: roslyn`, `aspnetcore` or the 52 packages, in shards), `Benchmarks`, `TechEmpower` and `M7 perf` (the
M7 harness), `GC concurrent`, `GC nursery` and `Release`. Only `CI` gates a merge. A docs-only change skips the compiler
suite when an ancestor's five checks are green; a targeted run (a `ci/**` branch with a `CI-Only:` trailer or `ci/targets`)
is partial and not the bar ([05-testing.md](05-testing.md#ci)). `gh workflow run` is HTTP 403 from the agent checkout, so
workflow runs on a branch come from a push.

### What to pick up

1. #163 and #164: the two regressions the CI measurements found (the regex generator's `#nullable` under old language
   versions, and four methods that stop the TechEmpower Platform entry).
2. The open M2–M6 bullets, with their issues, in [the list above](#what-is-open): the survey-ranked library surface (#146 and
   its in-flight pull requests), #168 for M5, #169 and #93 for M6, the native TLS spikes #144 and #143.
3. Then the gap lists #89, #95–#104.
4. Housekeeping found while auditing: `tests/platform-skips.txt` still has `known gap` entries for features that now work
   (`HttpClient` system proxies on macOS and Windows, Windows decompression and rooted `Uri` paths), so those samples are
   skipped there; check each against CI on that platform and remove the ones that pass. A TechEmpower-verdict regression is
   cheap to catch early: look at the `TechEmpower` workflow's `report` job after any change to the IL front-end or the
   framework-IL policy.

Later, when it matters: a `StringBuilder` that keeps .NET's chunks but not their linked list. `Insert` and `Replace(string)` are
slower than a single buffer because they must keep .NET's layout (40K inserts: 55 → 296 ms with 8,429 chunks; 60 growing/shrinking
replaces: 39 → 117 ms with 67,470 chunks; both still faster than .NET's 400 ms and 101 s). Every insert shifts the stored offset of each later chunk and finding a chunk walks the
predecessor links; holding the chunks in an array with lengths in a prefix-sum tree would make both O(log n) while
`GetChunks`/`Capacity` stay exactly .NET's. The builder object is .NET's last chunk today, so this is a redesign of
`corelib/StringBuilder.cs`.
