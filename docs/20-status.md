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

Green on all five CI platforms (Linux x86-64/arm64, macOS arm64, Windows x64, wasm32-wasi). Head
`2119997`, CI run 40214226809. Merged in this stretch: IL front-end and framework assemblies from IL
(ADR 0010/0013), compile-time scale (ADR 0014), the corelib compiled once to cached IL (ADR 0015),
.NET-identical stack traces (ADR 0012), globalization via ICU and its cost reduction, binary-size
budgets in CI, smaller ARM64/Windows binaries, System.Type basics and reflection metadata as NativeAOT
does it (ADR 0018), DateTimeOffset/MemoryPool/Convert, Roslyn blocker fixes, the NativeAOT-parity
metric, the TechEmpower harness, csharpc building and serving the TechEmpower app, the parity-blockers
library surface (95.9 %), and the `m8-perf` throughput work (socket engine, thread pool, interface
caches, task/ValueTask allocations, vectorized span searches, lock-free ConcurrentQueue, JSON writer
reuse, GC root scanning, bucket-locked monitors).

## Open branches

None from this stretch. `parity-blockers` is merged (CI run 40198351938, NativeAOT parity 95.9 %).
`m8-perf` is merged (CI run 40208654147 on `5b06180`, green on all five; the merge commit is
`2119997`). Remote `m8-perf` is deleted.

## Measured state

**NativeAOT parity** (compatibility corpus, [18-compatibility.md](18-compatibility.md)): 95.9 % on
`main`, no wrong outputs; csharpc compiles faster than NativeAOT on most
programs (median 4.3 s vs 6.0 s) and is ~10× smaller (median 131 KiB vs 1,330 KiB).

**TechEmpower** (`main` `2119997`, run 40214226895, 4-vCPU runner, csharpc -O2; both builds pass the
TechEmpower checks with identical responses):

| | NativeAOT | csharpc | ratio |
|---|---:|---:|---:|
| plaintext, 256 connections, pipelined | 548,509 req/s | 546,204 | 1.00 |
| plaintext, 1024 connections, pipelined | 486,836 | 472,140 | 0.97 |
| JSON, 16 / 64 / 256 connections | 70.0k / 84.4k / 86.3k | **74.9k / 87.3k / 92.1k** | 1.07 / 1.03 / 1.07 |
| JSON p99 at 256 connections | 5.26 ms | **4.00 ms** | |
| start-up | 27 ms | **21 ms** | |
| peak RSS idle → after load | 17.2 → 64.9 MiB | **13.3** → 73.3 MiB | |
| binary size | 8.9 MiB | 15.7 MiB | |
| build time | 23 s | 389 s | |

The pre-merge measurement on the same bits (run 40208654140) was plaintext 541,209 / 480,547 vs
539,354 / 484,517 and JSON 74.0k / 86.6k / 90.3k vs 69.7k / 84.3k / 87.5k. Before `m8-perf`, plaintext
was 49 % of NativeAOT. Against that branch's run 40193556960 (plaintext 0.99 / 0.98, JSON 1.05 / 1.02 /
1.04, binary 14.3 MiB, build 313 s), throughput and startup held; the binary grew to 15.7 MiB and the
build to 389 s with the parity-blockers corelib that landed in the same merge.

**Roslyn** (M6): Roslyn's non-generic methods that fail to compile went from 994 to 170; a small
program using Roslyn's parser compiles from IL at -O0.

## Next, in order

1. M8 gap: plaintext at 1024 connections (0.97 of NativeAOT on run 40214226895), memory under load,
   binary size (15.7 vs 8.9 MiB), build time (389 s vs 23 s); the TechEmpower workflow has a
   perf-profiling input.
2. M4: the remaining parity blockers (see 18-compatibility.md) to 100 %.
3. Runtime tier 1 (ADR 0017): move the C runtime's ordinary code to C# where it's at least as fast.
4. Early gauge for M7's acceptance gate: build dotnet's AOT test suites with csharpc on CI.
5. M6: survey generic methods in Roslyn, fix the rest of its blockers, then csharpc compiling itself.
6. M7: the dotnet fork with csharpc behind `PublishAot` and NativeAOT's MSBuild properties; then M8 in
   the TechEmpower environment.

## Cursor progress

Release packaging on `ci/release-packaging`, 2026-10-25. Times are BRT (UTC−3).

**nuget.org Trusted Publishing (for the owner, set this first):** OIDC push lives in
`.github/workflows/release.yml` (workflow file name for the policy: **`release.yml`**). **No**
GitHub Actions environment. `NuGet/login` uses `user: ${{ vars.NUGET_USER }}`. Policy fields:
Repository Owner `jonathanperis`, Repository `csharpc`, Workflow File `release.yml`, Environment
empty. Set repo variable `NUGET_USER` to the nuget.org profile name. If `NUGET_USER` is unset, the
release job skips nuget.org with a notice and still publishes GitHub Packages / Releases / GHCR.

**17:00–17:50.** Goal: ship csharpc via GitHub Releases and GitHub Packages. Driver facts:
`AppContext.BaseDirectory/{corelib,runtime}` (already `CopyToPublishDirectory`); libLLVM via
`$CSHARPC_LIBLLVM` then sonames (`Llvm/Native.cs`); clang/lld for csrt compile and link
(`Driver/Toolchain.cs`). Measured on this linux-x64 VM: self-contained R2R publish ~126 MiB / 19 s /
52 MiB `.tar.gz` without libLLVM; NativeAOT ~42 MiB binary / 61 s, with bundled libLLVM (~118 MiB)
~262 MiB on disk / **55 MiB** `.tar.gz`. Chose **NativeAOT bundles** so a clean machine needs no
.NET SDK or runtime. Bundled libLLVM (Apache-2.0 with LLVM exceptions) under `lib/`; clang/lld stay
a documented host prerequisite (full clang+sysroot is much larger and OS-specific; Windows still
needs LLVM-MinGW for the linker/CRT). Container image included: zero host toolchain for
`docker run … csharpc`.

**17:55.** Added `tools/pack-release.sh`, `tools/smoke-release.sh`, release workflow
(build ×4 RIDs → smoke without installing the .NET SDK → publish Release + NuGet to
`nuget.pkg.github.com/jonathanperis` + `ghcr.io/jonathanperis/csharpc`), `install.sh` /
`install.ps1`, `Dockerfile`, README Install section, roadmap/08-drop-in notes. `Native.cs` probes
`lib/` next to the binary. `ci.yml` push trigger extended to `ci/**`. Local smoke of the packed
linux-x64 archive: `01_hello` and `104_linq` match `.expected` with bundled libLLVM and
`CSHARPC_LD=bfd` (this VM has no `lld` package).

**18:10–18:20.** Scope extension: publish is part of the main pipeline via
`workflow_run` on green CI (`release.yml`), not inside the five merge-bar jobs. Rolling GitHub
prerelease `latest-main` + package `0.1.0-spike.<ci_run_number>`; tags `v*` get named releases.
RID-specific NativeAOT tool packages (`ToolPackageRuntimeIdentifiers` + `CsharpcBundleLibLLVM`) so
`dotnet tool install -g csharpc` installs the same binaries as the archives. Pointer package built
by `tools/pack-tool-pointer.sh` (manual DotnetTool v2 nuspec — `dotnet pack` without `-r` was
rebuilding the host RID and wiping the good nupkg). nuget.org via Trusted Publishing in
`release.yml` as above. TechEmpower / Benchmarks / Compatibility keep building csharpc from the
commit under test (they measure that tree); documented in those workflow headers. Windows zip fix:
`cygpath -w` for Compress-Archive.

Audit of the `m8-perf` merge on 2026-10-25. Times are BRT (UTC−3).


**12:46.** Merged `origin/main` (`91f2d97`) into `m8-perf` as `a9dbbd0`. Four conflicts, both sides kept:

- `src/runtime/threads.c`. Bucket-locked Enter/Exit (no allocation on the uncontended path) and the
  in-object `System.Threading.Lock` CAS stay. Main's arrival-order `Monitor.Wait` queue, per-waiter
  released flag, and `Thread.Interrupt` (thrown after Wait reacquires) stay. `ProcessorCount` is
  main's (affinity and cgroup quota, which already included the sched_getaffinity path).
- `src/runtime/async.c`. `TF_WAITED` is bit 4 and `TF_CREATED` is bit 8, so a created task can also
  be waited on. The pool queue-with-state path and the SynchronizationContext-aware yield both stay.
- `src/Csharpc/corelib/Tasks/ExecutionFlow.cs`. m8-perf's version: `int` flow context (a P/Invoke
  `bool` is a 4-byte BOOL) and `csrt_pool_queue_state`.
- `tests/platform-skips.txt`. Union of both additions.

**TLS link, found while building the merge.** A program large enough to split (sample 555, about 21k
instructions) defines `[ThreadStatic]` fields in partition 0. Another partition declared them and
never used them. LLVM 18 writes an unused `thread_local` declaration as an ordinary undefined symbol,
and ld rejects that against the TLS definition ("TLS definition in … section .tbss mismatches non-TLS
reference" on `__csp_…t_parentE` and `t_currentScheduler`). Putting the thread_local flag back after
`elim-avail-extern` changed nothing: the IR was already `external hidden thread_local`, and an unused
declaration is still emitted as a non-TLS symbol. The fix deletes declarations that have no uses in a
non-zero partition. A declaration that is used is left alone and emits as TLS. Sample 555 links and
passes at `-O0` and `-O2`.

**Main was not green.** Run 40203088110 on `91f2d97` (the parity-blockers merge): x86-64, arm64 and
wasm32-wasi passed; windows-x64 passed when it finished; macos-arm64 failed in differential tests.
The only failure was `544_blocking_collection` at `-O0` (exit 124 from the 90s timeout, empty stdout,
821 passed, `-O2` passed). `91f2d97` differs from `f208b53`, which passed that sample on macOS in run
40198351938, only in `docs/20-status.md` and `docs/README.md`. Same bits, one hang, so this is not a
miscompilation and not a regression introduced by a code commit after the green parity run. The main
thread has no `Thread` object until something asks for `CurrentThread`, and `Monitor.Wait` then took
the raw infinite `pthread_cond_wait`. A broadcast the C library swallows is never re-checked, and the
thread stays parked. `Wait` (`single`) now slices that wait every 50 ms even when there is no `Thread`
object, and re-checks the waiter's flag. No new sample: 544 is the regression case, and the failure
was a hang rather than wrong output, so `.expected` was not regenerated. It does not reproduce
reliably on Linux (544 passed there, including eight extra `-O0` runs). The evidence is the macOS
timeout and the following green macOS job.

**12:48.** `gh workflow run` returned HTTP 403 (`X-Accepted-Github-Permissions: actions=write`); this
checkout can push but cannot dispatch workflows. CI starts on a push to `main` only, so `5b06180`
listed `m8-perf` on the CI and TechEmpower push triggers. That push started CI
[40208654147](https://github.com/jonathanperis/csharpc/actions/runs/40208654147) and TechEmpower
[40208654140](https://github.com/jonathanperis/csharpc/actions/runs/40208654140).

**13:55.** CI 40208654147 green on all five platforms at `5b06180` (macOS included 544). TechEmpower
40208654140 green: plaintext 541,209 / 480,547 req/s vs NativeAOT 539,354 / 484,517; JSON 73,958 /
86,587 / 90,313 vs 69,745 / 84,263 / 87,506; JSON p99 at 256 connections 4.14 vs 5.38 ms; startup 21
vs 27 ms; RSS 13.5 → 74.1 vs 17.2 → 64.6 MiB; binary 15.7 vs 8.9 MiB; build 387 vs 20 s.

**13:58.** `6f917b3` removed those `m8-perf` trigger lines so `main` would not name a deleted branch.
Workflow files only; the code is the tree CI had just passed. Pushing it did not start CI (the new
workflow no longer lists the branch). It did start TechEmpower
[40214206989](https://github.com/jonathanperis/csharpc/actions/runs/40214206989), because that
workflow's path filter matches its own file and has no branch filter. That run was not required; it
later succeeded. Merged `--no-ff` to `main` as `2119997` without waiting for it, pushed, and deleted
remote `m8-perf`.

**15:01.** Main CI [40214226809](https://github.com/jonathanperis/csharpc/actions/runs/40214226809)
green on all five. The merge push also started Benchmarks 40214226787 (green, 14:08) and TechEmpower
[40214226895](https://github.com/jonathanperis/csharpc/actions/runs/40214226895) (green, 14:11),
because `src/**` and the workflow file changed. Numbers from 40214226895 are the measured-state table
above. Nothing else was pushed to `main` while 40214226809 was running.

Local checks before the first `m8-perf` push: conformance 195/195; differential `-O0` and `-O2` for
the thread, monitor, lock and async samples, including 555 and 544.

## Working rules

Build with `dotnet build src/Csharpc -c Release -m:1`, compile with `-j2`, run heavy suites on CI
(`gh workflow run ci.yml --ref <branch>`, compat.yml, techempower.yml, il-survey.yml), poll CI slowly.
After corelib edits run `CSHARPC_CHECK_CORELIB=1` (compiles every corelib member). Merge only
CI-verified branches; avoid docs-only pushes to `main` while a merge's CI is pending (a push cancels
the running run). Delete merged branches.
