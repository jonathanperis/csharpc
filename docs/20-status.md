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

**GC** (`benchmarks/gc`, local 4-vCPU, median of 3, published binaries): on an 800k-node ring,
csharpc pause 30.3 ms and 4.29M ops/s vs NativeAOT 117.4 ms and 3.04M, RSS 205 vs 235 MiB. Mark is
97 % of stop-the-world time (46.6 ms mark, 1.4 ms sweep). Short-lived churn is behind (16.5M ops/s
vs 20.8M NativeAOT). Parallel marking was slower on this machine and is not in the default binary
([ADR 0004](adr/0004-memory-management.md)).

## Next, in order

1. M8 gap: plaintext at 1024 connections (0.97 of NativeAOT on run 40214226895), memory under load,
   binary size (15.7 vs 8.9 MiB), build time (389 s vs 23 s); the TechEmpower workflow has a
   perf-profiling input. GC: the mark is ~97 % of stop-the-world time on `benchmarks/gc` and a
   parallel marker was slower on 4 cores (ADR 0004); the next collector increment is a non-moving
   nursery with a compiler card table.
2. M4: the remaining parity blockers (see 18-compatibility.md) to 100 %.
3. Runtime tier 1 (ADR 0017): move the C runtime's ordinary code to C# where it's at least as fast.
4. Early gauge for M7's acceptance gate: build dotnet's AOT test suites with csharpc on CI.
5. M6: survey generic methods in Roslyn, fix the rest of its blockers, then csharpc compiling itself.
6. M7: the dotnet fork with csharpc behind `PublishAot` and NativeAOT's MSBuild properties; then M8 in
   the TechEmpower environment.

## Cursor progress

Release packaging on `ci/release-packaging`, 2026-10-25. Times are BRT (UTC−3).

**nuget.org Trusted Publishing — configured (BRT 17:18, 2026-10-25).** Owner Jonathan signed in
to nuget.org as account **`Jonathanperis`**, created Active Trusted Publishing policy
**`csharpc-release`** for GitHub Actions: repo `jonathanperis/csharpc`, workflow file
**`release.yml`**, no environment, package glob `*`. Repo variable **`NUGET_USER=Jonathanperis`**
is set. OIDC `NuGet/login` + nuget.org push live in `.github/workflows/release.yml` (claim is
`release.yml`, not a reusable callee); publish job has `id-token: write`. Trigger is
`workflow_run` after green CI on main (and `v*` tags), so the five merge-bar jobs are untouched.
Test/dry-run versions are never pushed to nuget.org (unlist-only). First nuget.org push is the
real `v0.1.0-spike` tag (or `0.1.0-spike.<ci_run_number>` from main after merge).

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

**17:18.** Owner (Jonathan Peris / nuget.org account `Jonathanperis`) configured Trusted Publishing
policy `csharpc-release` on nuget.org: Active for GitHub Actions, repo `jonathanperis/csharpc`,
workflow file `release.yml`, no environment, package glob `*`. Repo variable
`NUGET_USER=Jonathanperis` set. Publish stays in `.github/workflows/release.yml` (not a reusable
workflow called from `ci.yml`): NuGet matches OIDC `job_workflow_ref` to the workflow that requests
the token, so a reusable workflow would claim the shared path and fail (NuGet/login#6). Trigger is
`workflow_run` after green CI. No test/dry-run versions to nuget.org (undeletable); first real push
is `0.1.0-spike` (tag) or the main rolling `0.1.0-spike.<ci_run_number>`.

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
commit under test (they measure that tree); documented in those workflow headers. Local linux-x64:
archive 55 MiB, RID nupkg 117 MiB, smoke OK.

**18:25.** Release dry-run 40230115161: all four RID builds green (win-x64 included); all four smoke
jobs failed immediately — `ls a.tar.gz a.zip` under `pipefail` exits 2 when the `.zip` glob is
absent. Fixed with `nullglob` + array. nuget.org push gated off for `*test*`/`*dryrun*` versions.

**18:30.** Release 40230552303: smoke found the archive but corelib bind failed — NativeAOT has no
`TRUSTED_PLATFORM_ASSEMBLIES`, and smoke runners have no .NET 11 ref pack. Bundle
`Microsoft.NETCore.App.Ref/.../ref/net11.0` as `ref/net11.0/` next to the binary; `FindReferencePack`
prefers it. Local retest without any `dotnet` on PATH: 01_hello and 104_linq OK; archive 58 MiB.

**17:40.** Release 40231133703: linux/osx/arm64 smoke green after ref-pack; win-x64 smoke failed —
`Compress-Archive` zip uses backslash separators, Info-ZIP unzip warns and exits 1. Fixed:
`pack-release.sh` builds Windows zips with Python `zipfile` (forward slashes); smoke uses
`Expand-Archive` on Windows. nuget.org login no longer `continue-on-error` — real push when
`publish_nuget_org=true` (policy `csharpc-release` Active as of 17:18).

**17:45.** Release 40231809254 (`d416a11`): zip extract OK on win-x64; smoke failed on
`01_hello` stdout — CRLF vs LF (`.expected` recorded on Linux; `Environment.NewLine` is `\r\n` on
Windows). `smoke-release.sh` now derives want-stdout like `tests/platform.sh` `platform_expected`.
Merged `origin/main` (`ee7f8f8` precise-GC) into the packaging branch before re-verify.

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

**16:05.** GC measurement on `ci/precise-gc`, not a faster collector. `benchmarks/gc` (4 threads,
checksum `1008676208218` from `dotnet run`) median of 3 published runs on this 4-vCPU VM:
`livemutate` csharpc wall 112 ms, 4.29M ops/s, pause 30.3 ms, RSS 205 MiB; JIT 161 ms, 2.98M, pause
118.8 ms, RSS 258 MiB; NativeAOT 158 ms, 3.04M, pause 117.4 ms, RSS 235 MiB. Churn is the other way:
csharpc 16.5M ops/s and 2.73 ms pause (14 collections) vs NativeAOT 20.8M / 2.08 ms (3) and JIT
24.4M / 1.68 ms (3). A quiet `CSRT_GC_STATS=1` run was mark 46.6 ms, sweep 1.4 ms, 18 collections.
csharpc compile of that program 2.3 s, stripped 195 KiB, NativeAOT 1.33 MiB. Hello world file size
stayed 35056 bytes.

What did not work, both checksum-correct:

- Atomic `fetch_or` mark bits. Idle helpers spinning on the mark lock made sample 21 ~30× slower
  (p50 46 ms). After they sleep, forced parallel was still slower (sample 21 mark 158 ms vs ~75 ms
  serial; `livemutate` pause ~55 ms vs ~31 ms). The mark bitmap's cache lines are shared by every
  object in the chunk.
- Chunk ownership with plain stores and batched handoff, large arrays sliced across workers. Mark
  53–74 ms vs ~47 ms serial on this VM. The serial scan is already ~1.5–2 GB/s; the handoff costs
  more than the scan. Linked into every binary it grew hello from 35056 to 43256 bytes (budget
  32768), so it is compiled only with `-DCSRT_GC_PARALLEL_MARK`.

A multiplicative inverse for the size-class division was inside the noise. Together with the pause
histogram it added about 1 KiB of text, and CI run 40224424838 failed the size budget: `async` was
151376 bytes against 147456 on both x86-64 and arm64 (one page). Conformance, programs, projects
and the IL front-end had already passed on those jobs. The histogram and the inverse came back
out; the default `gc.o` text, data and bss match `main` (13458 / 4 / 1401). The ownership marker
stays in the source under `-DCSRT_GC_PARALLEL_MARK` and is not in that object file.

Thread-local allocation was already there. Statepoints and precise maps are the wrong first lever
on a pointer-dense heap; ADR 0004's next step is a non-moving nursery with a compiler card table.
Differential `-O0`/`-O2`: 20, 21, 125, 310, 627. `CSRT_GC_STRESS=1` on 310 passed. Sample 21 under
stress (a collection per allocation) hit the suite timeout; that is the mode, not a wrong output.
`gh workflow run` is still HTTP 403, so CI is the `ci/**` push filter. The size failure cancelled
nothing by itself; the fix is a second push, which supersedes 40224424838.

## Working rules

Build with `dotnet build src/Csharpc -c Release -m:1`, compile with `-j2`, run heavy suites on CI
(`gh workflow run ci.yml --ref <branch>`, compat.yml, techempower.yml, il-survey.yml), poll CI slowly.
After corelib edits run `CSHARPC_CHECK_CORELIB=1` (compiles every corelib member). Merge only
CI-verified branches; avoid docs-only pushes to `main` while a merge's CI is pending (a push cancels
the running run). Delete merged branches.

Pushes to `main` are what start CI. A measurement branch is named `ci/<topic>` (the `ci/**` push
filter on `ci.yml` and `bench.yml`). That does not change `main`: a push to `main` still runs CI,
and the Benchmarks workflow still also requires a path change under `src/`, `benchmarks/` or its
own file. TechEmpower has no branch filter, only those paths, so a `ci/**` branch that touches
`benchmarks/techempower/**` or the workflow file runs it too; do not add a branch filter there.
`gh workflow run` from this checkout returns HTTP 403 (no `actions: write`).
