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

None from this stretch. `ci/gc-nursery` is merging (nursery opt-in only). `parity-blockers`,
`m8-perf`, `ci/precise-gc`, and `ci/release-packaging` are merged.

## Measured state

**NativeAOT parity** (compatibility corpus, [18-compatibility.md](18-compatibility.md)): 95.9 % on
`main`, no wrong outputs; csharpc compiles faster than NativeAOT on most
programs (median 4.3 s vs 6.0 s) and is ~10× smaller (median 131 KiB vs 1,330 KiB).

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
2. M4: the remaining parity blockers (see 18-compatibility.md) to 100 %.
3. Runtime tier 1 (ADR 0017): move the C runtime's ordinary code to C# where it's at least as fast.
4. Early gauge for M7's acceptance gate: build dotnet's AOT test suites with csharpc on CI.
5. M6: survey generic methods in Roslyn, fix the rest of its blockers, then csharpc compiling itself.
6. M7: the dotnet fork with csharpc behind `PublishAot` and NativeAOT's MSBuild properties; then M8 in
   the TechEmpower environment.

## Cursor progress

Parity blockers on `ci/parity-hasflag-attrs`, 2026-10-26. Times are BRT (UTC−3).

**01:43.** Goal: close remaining NativeAOT-parity gaps (was **95.9 %**, 236 of 246 in-scope).
Grouped the ten in-scope failures from `docs/18-compatibility.md` and fixed the highest-impact
reflection cluster first.

**Fixes shipped (samples 637–640, expected from real .NET):**

1. **`Enum.HasFlag` on a boxed enum across basic blocks** — IL merges lose static `Unboxed`
   tracking; intrinsic now calls `csrt_enum_has_flag` (type-match + null messages match .NET).
2. **`TypeInfo.GetCustomAttributes()` via `MemberInfo`** — `AttributeLookup` routes `Type` to
   `Type.GetCustomAttributes`; inherited walk skips bases with no attribute records (was
   `NotSupportedException` on `System.Object`). `CreateAttributes` uses `new Attribute[]` instead
   of `Array.CreateInstance` (avoids pulling every ref type into array MTs).
3. **`Exception` / `AggregateException.GetBaseException()`** — corelib unwrap + dispatch in
   `FunctionCodeGen.Exceptions`.
4. **`Array.CreateInstance(Type, int)` without a compile-time `T[]`** — runtime synthesizes the
   array MT/TypeInfo for reference elements; compile-time also keeps `T[]` (with `IEnumerable<T>`
   maps) for closed array-interface element types and reflection-kept types when CreateInstance is
   used. Full TypeInfo keep blew TracingConfiguration compile time; this scoped keep compiles in
   ~10 s. `Array.GetValue` uses `csrt_array_get_value` for ref arrays (IList cast failed on
   synthesized MTs).
5. **Expression-tree invoker NRE** — `EmitNullableMethod` rejects a null instance with
   `CSLLVM0001` instead of crashing (ExpressionTreeExample stays COMPILE-UNSUPPORTED on
   DynamicMethod / expression trees).

**Measured locally (compat corpus spot, `--no-aot`):**
`tutorial-attributes` **PASS** (was RUNTIME-CRASH), `TracingConfiguration` **PASS** (was
COMPILE-UNSUPPORTED on HasFlag, then CreateInstance). Expected in-scope parity **238 of 246
(~96.7 %)** pending a full Compatibility run; ConfigurationIgnore still blocked on
`System.ComponentModel.TypeConverter` (`Attribute.TypeId` / `IsDefaultAttribute` overrides,
`Array.Sort(Array, IComparer)`, …). Expression trees (2), `dynamic` (1), C# 15 unions (2),
`System.Data` (1), `AssemblyLoadContext` (1) unchanged. Hello size **28520** (unchanged).

**Did not work:** keeping `T[]` for every TypeInfo reference type when CreateInstance appears
(TracingConfiguration compile hung / multi-minute). Scoped keep + runtime synthesis instead.

**02:49.** Branch CI
[40270584270](https://github.com/jonathanperis/csharpc/actions/runs/40270584270) failed on all
five: `CreateAttributes` returned `Attribute[]` for filtered
`GetCustomAttributes(typeof(TagAttribute))` (samples 604/606/617 —
`InvalidCastException` / `NeverUsedAttribute[]` vs `Attribute[]`). Restored
`Array.CreateInstance(attributeType, n)` for concrete Attribute subclasses only;
`Attribute[]` / `object[]` stay direct allocations so CreateInstance does not note
every TypeInfo type. Main CI 40265042196 was green. Re-pushing the fix.

---

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

**18:23.** Release 40231809254 (`d416a11`): zip extract OK on win-x64; smoke failed on
`01_hello` stdout — CRLF vs LF (`.expected` recorded on Linux; `Environment.NewLine` is `\r\n` on
Windows). `smoke-release.sh` now derives want-stdout like `tests/platform.sh` `platform_expected`.
Merged `origin/main` (`ee7f8f8` precise-GC) into the packaging branch before re-verify.

**19:47.** CI 40235338045 / 40235341916 and Release dry-run 40235338055 on `49f45c2`: all five CI
jobs green; all four RID builds + smokes green (win-x64 included). Publish skipped (dry-run).
Removed the temporary `ci/release-packaging` push trigger from `release.yml`. Merged
`origin/main` `b2099e2` (precise-gc status log). Ready to merge to main.

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

**18:32.** `38224e0` is that second push. It dropped the pause histogram and the size-class inverse
out of the default runtime. Branch CI
[40225871920](https://github.com/jonathanperis/csharpc/actions/runs/40225871920) then went green on
all five: x86-64 121267283423, arm64 121267283048, macos-arm64 121267283324, windows-x64
121267283267, wasm32-wasi 121267283189. Size budgets passed (the previous failure was async 151376
vs 147456). Branch Benchmarks
[40225871859](https://github.com/jonathanperis/csharpc/actions/runs/40225871859) green. TechEmpower
did not re-run on `38224e0`: its path filter missed a push that only touched `src/runtime/gc.c` and
docs. The parent `ad5ca76` (histogram still linked) had TechEmpower
[40224424831](https://github.com/jonathanperis/csharpc/actions/runs/40224424831), green, plaintext
499,644 / 464,468 vs NativeAOT 544,380 / 485,715. That binary is not what merged. `origin/main` was
still `799936f`.

Merged `--no-ff` as `ee7f8f8` and deleted `ci/precise-gc`. Nothing else was pushed while main CI
[40230540448](https://github.com/jonathanperis/csharpc/actions/runs/40230540448) ran. It is green:
x86-64 121281095989, arm64 121281095896, macos-arm64 121281096001, windows-x64 121281096478,
wasm32-wasi 121281096015. hello 28520 / async 147280, both under budget. Main Benchmarks
[40230540325](https://github.com/jonathanperis/csharpc/actions/runs/40230540325) green (x86-64
121281095290, arm64 121281095503): `livemutate` pause 18.6 ms vs NativeAOT 366 ms on x86-64, 17.2 vs
220 ms on arm64. Main TechEmpower
[40230540571](https://github.com/jonathanperis/csharpc/actions/runs/40230540571) green; numbers are
the measured-state table. Local differential on the restored `gc.c` at `38224e0`: 20, 21, 125, 310
and 627 at `-O0` and `-O2`.

The default collector is not faster than `2119997`. Both parallel markers lost to one thread, and
the timing code that measured mark vs sweep missed the async budget, so it is gone. The nursery and
the card table are not implemented.

**19:05.** `ci/gc-nursery`: non-moving nursery + compiler card table, off by default. Design after
measurement (ADR 0004 updated):

- Young is a bit on each 256 KiB chunk (and large objects are born old), not a contiguous space.
  Minors mark young only from stacks, statics and dirty old regions, sweep young chunks, then
  promote survivors in place so cards can clear. Majors are today's full mark-sweep (also every
  32nd minor, `GC.Collect`, and stress).
- Cards are per-chunk / per-large dirty bits. A flat table over `[heap_min, heap_max)` OOM'd under
  ASLR (sparse mmap). `--gc-nursery` makes codegen emit `csrt_gc_write` (dirty-before-store) on
  heap reference stores; atomics/volatile and `Array.Copy` of refs use `csrt_gc_cards_dirty`.
  Locals and statics stay plain stores. wasm32-wasi gets the same barrier; shadow stack unchanged.
- Nursery code is `#if CSRT_HAS_NURSERY` / `-DCSRT_GC_NURSERY_DEFAULT` so the default `gc.o` stays
  lean. Sticky survivors (keep young, only clear cards) produced a wrong checksum once cards
  cleared; promotion was required. A 4 MiB nursery trigger made too many minors once the live set
  had promoted; floor is 32 MiB (`CSRT_GC_NURSERY_MB`).

Local 4-vCPU (`benchmarks/gc`, median):

| | churn ops/s | churn pause | livemutate ops/s | livemutate pause | collections (live) | heap / RSS |
|---|---:|---:|---:|---:|---:|---:|
| default | 18.7M | 3.08 ms | 4.00M | 36.0 ms | 2 | 99 / 205 MiB |
| `--gc-nursery` | 11.7M | 3.99 ms | 3.53M | 53.3 ms | 7 | 110 / 135 MiB |
| NativeAOT (earlier baseline on this host) | 20.8M | 2.08 ms | 3.04M | 117.4 ms | 1 | 164 / 235 MiB |

RSS wins (~70 MiB); churn loses ~37 %; livemutate pause gets worse. Size with barriers: async
~167680 vs budget 147456. Default (flag off): hello **28520**, async **147200**. Correctness:
`_gc` samples and 457 at `-O0`/`-O2` default; same six under `--gc-nursery`; `CSRT_GC_STRESS=1`
on 310 with nursery. Not shipping as default — flag + honest ADR.

**20:05.** Push `f80e843` CI
[40238570327](https://github.com/jonathanperis/csharpc/actions/runs/40238570327): arm64
`tests/size` typeinfo 43896 vs budget 43008 (main was 39800; every other aarch64 size binary
matched main). Nursery fields and `csrt_gc_cards_dirty` from `Array.Copy` were still in the
default build; gated them on `CSRT_HAS_NURSERY` (`02f055e`). A mistaken budget raise to 47104
went in as `a65a0e0` and is reverted: the growth was avoidable.

**20:36.** Root cause of the aarch64 typeinfo page: `alloc_small_slow`’s default path was
rewritten with a shared `if (g.enabled && !g.collecting)` wrapper around the trigger check.
That is equivalent, but aarch64 codegen grew the function by 60 bytes (1496 → 1556). The RX
LOAD (`FileSiz` 0x3fd0 → 0x4010) crossed the next 0x1000 file boundary, so the data LOAD moved
from file offset 0xa000 to 0xb000 and the stripped file grew by exactly 4096 (39800 → 43896 on
CI; reproduced cross at 51976 → 56072 with the same RX sizes). Fix: keep the single-generation
`alloc_small_slow` / `alloc_large_locked` bodies byte-identical to main under
`#else` of `CSRT_HAS_NURSERY`. Cross-check after the fix: aarch64 typeinfo file and `size`
match main (RX `FileSiz` 0x3fd0 again); `linux-aarch64 typeinfo` budget restored to 43008.
x86-64 sizes unchanged (hello 28520, async 147200, typeinfo 43776).

**21:38.** Branch CI [40245571259](https://github.com/jonathanperis/csharpc/actions/runs/40245571259)
green on all five (x86-64, arm64, macos-arm64, windows-x64, wasm32-wasi). arm64 typeinfo
**39800** / 43008 again (same as main). Benchmarks
[40245571132](https://github.com/jonathanperis/csharpc/actions/runs/40245571132) green. Waiting
for in-progress main CI 40248320210 (release-packaging merge) before merging this branch.

**22:05.** Main CI 40248320210 green. After merging `origin/main`, branch CI
[40250349833](https://github.com/jonathanperis/csharpc/actions/runs/40250349833) green on all
five; arm64 typeinfo still 39800. Merged `--no-ff` as `1e9b379` and deleted `ci/gc-nursery`.

**23:45.** Main CI [40257845022](https://github.com/jonathanperis/csharpc/actions/runs/40257845022)
green on all five after the merge. arm64 typeinfo **39800** / 43008; x86-64 hello 28520, async
under budget. Budget raise reverted earlier; default collector unchanged.

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
