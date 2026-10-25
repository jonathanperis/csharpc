# 20 — Status and next steps (2026-10-25)

Where the work stands and what to pick up next. The plan is [06-roadmap.md](06-roadmap.md); the
direction is [ADR 0016](adr/0016-nativeaot-parity-and-end-state.md) (NativeAOT parity, the dotnet fork,
TechEmpower as the finish line) and [ADR 0017](adr/0017-runtime-tiers.md) (runtime tiers).

## Finish line

M8: a fork of dotnet with NativeAOT's code removed and csharpc in its place, built, used to compile the
ASP.NET Core TechEmpower Framework Benchmarks entries .NET runs with NativeAOT, beating the
NativeAOT-built entries in the TechEmpower environment. Acceptance gate for the fork (M7): dotnet's own
AOT test suites pass with csharpc. Every step holds to the standing goal (byte-identical behaviour,
explicit diagnostics instead of silent differences; faster, smaller, safer than NativeAOT; size and
speed regressions are bugs; every fix with a regression test recorded from real .NET).

## `main`

Green on all five CI platforms (Linux x86-64/arm64, macOS arm64, Windows x64, wasm32-wasi). Merged in
this stretch: IL front-end and framework assemblies from IL (ADR 0010/0013), compile-time scale (ADR
0014), the corelib compiled once to cached IL (ADR 0015), .NET-identical stack traces (ADR 0012),
globalization via ICU and its cost reduction, binary-size budgets in CI, smaller ARM64/Windows binaries,
System.Type basics and reflection metadata as NativeAOT does it (ADR 0018), DateTimeOffset/MemoryPool/
Convert, Roslyn blocker fixes, the NativeAOT-parity metric, the TechEmpower harness, and csharpc
building and serving the TechEmpower app.

## Open branch (pushed, not merged)

`parity-blockers` is merged (CI run 40198351938 green on all five platforms): NativeAOT parity on
`main` is now 95.9 %.

| branch | head | what | CI |
|---|---|---|---|
| `m8-perf` | `afcbfbc` | TechEmpower throughput (profiling, socket engine, thread pool, interface caches, task/ValueTask allocations, vectorized span searches, lock-free ConcurrentQueue, JSON writer reuse, GC root scanning) | run 40198013749 green on all five platforms; TechEmpower run 40193556960 green |

**To resume:** merge `origin/main` into `m8-perf`. It conflicts in `src/runtime/threads.c`,
`src/runtime/async.c`, `src/Csharpc/corelib/Tasks/ExecutionFlow.cs` and `tests/platform-skips.txt`.
The real work is `threads.c`: both branches rewrote the monitor (`lock`/`Monitor`/`System.Threading.Lock`).
`m8-perf` made Enter/Exit one bucket-locked critical section with no allocation (for Kestrel's request
path). `main` (from `parity-blockers`) added an ordered waiter queue and interruptible waits (Pulse
order and `Thread.Interrupt` as in .NET). The merged design needs both: m8-perf's bucket-locked fast
path plus main's per-waiter queue and interruptible wait. `ExecutionFlow.cs` takes m8-perf's version
(a superset); `platform-skips.txt` keeps both sides' lines. Then run ci.yml and the TechEmpower workflow
on the merge, compare TechEmpower numbers with run 40193556960, and merge when green.

## Measured state

**NativeAOT parity** (compatibility corpus, [18-compatibility.md](18-compatibility.md)): 95.9 % on
`main`, no wrong outputs; csharpc compiles faster than NativeAOT on most
programs (median 4.3 s vs 6.0 s) and is ~10× smaller (median 131 KiB vs 1,330 KiB).

**TechEmpower** (`m8-perf`, CI run 40193556960, 4-vCPU runner, csharpc -O2; both builds pass the
TechEmpower checks with identical responses):

| | NativeAOT | csharpc | ratio |
|---|---:|---:|---:|
| plaintext, 256 connections, pipelined | 546,650 req/s | 540,605 | 0.99 |
| plaintext, 1024 connections, pipelined | 498,653 | 486,203 | 0.98 |
| JSON, 16 / 64 / 256 connections | 70.2k / 85.0k / 87.7k | **74.1k / 86.4k / 91.3k** | 1.05 / 1.02 / 1.04 |
| JSON p99 at 256 connections | 5.36 ms | **4.04 ms** | |
| start-up | 26 ms | **21 ms** | |
| peak RSS idle → after load | 17.2 → 65.0 MiB | **12.9** → 74.9 MiB | |
| binary size | 8.9 MiB | 14.3 MiB | |
| build time | 21 s | 313 s | |

(The first csharpc numbers, before `m8-perf`, were 49 % of NativeAOT on plaintext.)

**Roslyn** (M6): Roslyn's non-generic methods that fail to compile went from 994 to 170; a small
program using Roslyn's parser compiles from IL at -O0.

## Next, in order

1. Merge `m8-perf` (resolve the monitor conflict described above).
2. M8 gap: plaintext ≥ NativeAOT, memory under load, binary size (14.3 vs 8.9 MiB), build time
   (313 s vs 21 s); the TechEmpower workflow has a perf-profiling input.
3. M4: the remaining parity blockers (see 18-compatibility.md) to 100 %.
4. Runtime tier 1 (ADR 0017): move the C runtime's ordinary code to C# where it's at least as fast.
5. Early gauge for M7's acceptance gate: build dotnet's AOT test suites with csharpc on CI.
6. M6: survey generic methods in Roslyn, fix the rest of its blockers, then csharpc compiling itself.
7. M7: the dotnet fork with csharpc behind `PublishAot` and NativeAOT's MSBuild properties; then M8 in
   the TechEmpower environment.

## Working rules

Build with `dotnet build src/Csharpc -c Release -m:1`, compile with `-j2`, run heavy suites on CI
(`gh workflow run ci.yml --ref <branch>`, compat.yml, techempower.yml, il-survey.yml), poll CI slowly.
After corelib edits run `CSHARPC_CHECK_CORELIB=1` (compiles every corelib member). Merge only
CI-verified branches; avoid docs-only pushes to `main` while a merge's CI is pending (a push cancels
the running run). Delete merged branches.
