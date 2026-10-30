# 20 — Status and next steps (2026-10-30)

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
this stretch, besides everything before it (IL front-end and framework IL, corelib as cached IL,
stack traces, globalization, size budgets, reflection metadata, the TechEmpower harness and the
`m8-perf` throughput work): the precise collector and an opt-in nursery (`--gc-nursery`, ADR 0004),
release packaging (`release.yml`: NativeAOT-built csharpc bundles for linux-x64/arm64, osx-arm64 and
win-x64, GHCR image, nuget.org via Trusted Publishing; `v0.1.0-spike` and a rolling `latest-main`),
sharded CI with targeted runs and a docs-only skip, and parity fixes (HasFlag, Type attributes,
`Array.CreateInstance`, `GetBaseException`, pool injection for blocked event waits, TypeConverter /
Activator). The TypeDescriptor shadow first converted only strings, so Configuration.Binder silently
left `int`/`bool`/`double` settings at their defaults; `ci/typeconverter-primitives` replaced it with
.NET's primitive converters and an explicit error for the rest (sample 643).

## Open work

- `ci/fix-544-hang` (`f497ed2`): macOS `544_blocking_collection` hangs about once in 20 runs at -O0
  (`ManualResetEventSlim.Wait` with a cancellation token that never wakes). The fix passed 50 macOS
  repeats (run 40426515690); its full CI (40426503049) failed on arm64 at
  `exceptions/003_filter_side_effect_order` with wrong output — not yet known whether that is the fix
  or a flake. `123_monitor_wait_pulse` also hung intermittently on macOS after the `m8-perf` merge and
  was re-run, not explained.
- PR #14 (release action upgrades) and the Renovate PRs for the release actions: Release also starts
  (and skips) after every branch CI run; gate it on CI runs of `main` pushes.
- nuget.org gets a `0.1.0-spike.<run>` package for every green `main` CI; packages can't be deleted.
  Consider publishing to nuget.org from tags only.

## Measured state

**NativeAOT parity** (compatibility corpus, [18-compatibility.md](18-compatibility.md)): 243 of 250
(97.2 %), no wrong outputs (run 40424843703). Left: expression trees (2), `dynamic` (1), C# 15 unions
(2), `System.Data` (1), deriving from `AssemblyLoadContext` (1). csharpc compiles faster than
NativeAOT on most programs (median 6.4 s vs 8.0 s) and is ~10× smaller (median 136 KiB vs 1,338 KiB).

**TechEmpower** (`main` `ee7f8f8`, run 40230540571, 4-vCPU runner, csharpc -O2; both builds pass the
TechEmpower checks with identical responses):

| | NativeAOT | csharpc | ratio |
|---|---:|---:|---:|
| plaintext, 256 connections, pipelined | 799,811 req/s | 757,935 | 0.95 |
| plaintext, 1024 connections, pipelined | 774,706 | 703,443 | 0.91 |
| JSON, 16 / 64 / 256 connections | 121.5k / 145.7k / 146.2k | 117.8k / 131.3k / 130.6k | 0.97 / 0.90 / 0.89 |
| JSON p99 at 256 connections | 3.80 ms | **2.94 ms** | |
| start-up | 22 ms | **18 ms** | |
| peak RSS idle → after load | 17.2 → 67.8 MiB | **13.4** → 73.3 MiB | |
| binary size | 8.9 MiB | 15.7 MiB | |
| build time | 21 s | 373 s | |

The previous table was run 40214226895 on `2119997`: plaintext 548,509 / 486,836 vs 546,204 / 472,140
(1.00 / 0.97) and JSON csharpc ahead at 74.9k / 87.3k / 92.1k. `ee7f8f8` does not change that server
binary's collector: default `gc.o` is still text 13458, data 4, bss 1401, hello is 28520 bytes (28528
on 40214226809) and async is 147280 against the 147456 budget. Absolute req/s rose for every engine
on this runner (NativeAOT plaintext 549k → 800k), and the ratios moved with them. Before `m8-perf`,
plaintext was 49 % of NativeAOT. The pre-merge measurement on the `m8-perf` bits (run 40208654140)
was plaintext 541,209 / 480,547 vs 539,354 / 484,517.

**Roslyn** (M6): Roslyn's non-generic methods that fail to compile went from 994 to 170; a small
program using Roslyn's parser compiles from IL at -O0.

**GC** (`benchmarks/gc`, hosted runners, median of 3, run 40230540325 on `ee7f8f8`): x86-64
`livemutate` csharpc pause 18.6 ms, 4.57M ops/s, heap 205 MiB, RSS 225 MiB against NativeAOT 366 ms,
1.06M ops/s, heap 76 MiB, RSS 106 MiB, and the JIT 394 ms, 0.99M, RSS 129 MiB (1 collection vs 17).
Churn is 22.5M ops/s and a 2.95 ms pause (14 collections) vs NativeAOT 18.5M and 22.6 ms (25).
arm64 `livemutate` pause 17.2 ms vs NativeAOT 220 ms, RSS 224 vs 150 MiB. The local 4-vCPU run that
drove the design was pause 30.3 ms and 4.29M ops/s vs NativeAOT 117.4 ms and 3.04M, RSS 205 vs 235
MiB; an instrumented build (not this binary) spent 46.6 ms marking and 1.4 ms sweeping. Parallel
marking was slower on 4 cores and is not in the default binary
([ADR 0004](adr/0004-memory-management.md)).

## Next, in order

1. M8 gap: memory under load (RSS 13.4 → 73.3 MiB vs NativeAOT 17.2 → 67.8 on run 40230540571;
   the same collector was 13.3 → 73.3 vs 17.2 → 64.9 on 40214226895), binary size (15.7 vs 8.9 MiB),
   build time (373 s vs 21 s). Plaintext on 40230540571 was 0.95 / 0.91 of NativeAOT and JSON
   throughput was 0.97 / 0.90 / 0.89; JSON p99 and startup were still ahead. On 40214226895 the same
   server bits were plaintext 1.00 / 0.97 and JSON was ahead, so the ratio move is the runner.
   GC: hosted x86-64 `livemutate` pause 18.6 ms vs NativeAOT 366 ms, with a larger retained heap
   (205 vs 76 MiB). The nursery + card table landed opt-in only (ADR 0004): RSS falls with
   `--gc-nursery` but churn throughput and livemutate pause get worse, and barriers blow the async
   size budget — not the default. Next collector lever is a denser remembered set (or
   mostly-copying), not flipping the flag.
2. M4: the seven remaining parity blockers (above) to 100 %.
3. Runtime tier 1 (ADR 0017): move the C runtime's ordinary code to C# where it's at least as fast.
4. Early gauge for M7's acceptance gate: build dotnet's AOT test suites with csharpc on CI.
5. M6: survey generic methods in Roslyn, fix the rest of its blockers, then csharpc compiling itself.
6. M7: the dotnet fork with csharpc behind `PublishAot` and NativeAOT's MSBuild properties; then M8 in
   the TechEmpower environment.

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
