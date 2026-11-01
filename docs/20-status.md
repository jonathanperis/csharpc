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

- `ci/m7-startup` — the start-up regression of `ci/m7-perf-3` (628 ms in TechEmpower run 41273842626, 1.3-1.4 s
  since 774b977) was the thread pool's 100 us polling (69b83a3): every idle worker polled, and the start-up's
  10,000-query cache fill hands each reply to the pool, so all the server's CPUs spun for the one that worked.
  One idle worker polls at a time now. TechEmpower run 41297487760 (EPYC 9V74): start-up 277 ms vs NativeAOT's
  548 (CPU 380 vs 960 ms), **301 of 456**, /db at 16-32 connections 1.11-1.12 of NativeAOT (what the polling was
  for, kept). Its first attempt ran on a Xeon 8573C (start-up 663 vs 781 ms; 254 of 456): on that runner and on
  the Xeon 6973P-C NativeAOT's fortunes are ~2x other runners' and csharpc's 0.73-0.88 of them, a fortunes gap
  that only shows where the database is fast (CPU-bound fortunes), not from these changes.
- `ci/il-stack-traces` — stack traces through code compiled from IL and the corelib (ADR 0012). Every
  IL-compiled method (program as IL, libraries, NuGet packages, framework IL) is a frame, named as
  `StackTrace.ToString` names it (state machines through their StateMachine attributes, runtime-async),
  with lines where .NET finds a PDB (embedded or beside the assembly; NuGet packages' only with
  `CopyDebugSymbolFilesFromPackages`). The formatter applies .NET's filters ([StackTraceHidden],
  AggressiveInlining, last frame kept; no "previous location" marker after async frames); awaits of a
  faulted task add the marker and restart from the fault-time frames (awaiting a task twice repeated
  frames: a silent difference on `main`). Library frames for Task.Wait/Result, Socket.ConnectAsync,
  Substring, the ThrowIf helpers, Dictionary.Add, List<T> enumerator/Insert, Single. The Platform entry's
  closed-port trace is now the JIT's; its -O2 executable grows 11 % (it records traces because it prints
  an exception: a decision for M7). Samples 900–903, `tests/il.sh` IlStackTracesApp. Known gaps
  (CSLLVM0006): parse family, string.Format, Task.Run/WaitAll, TcpClient.ConnectAsync, frames of library
  members that call back into the program (ForEach, LINQ, Lazy, Sort).
- `ci/il-traces-2` (on `ci/il-stack-traces`) — the trace tables made cheaper and four silent
  differences fixed. Frame names DEFLATE-compressed (inflated at the first formatted trace), 8-byte
  descriptors for frames without a location, an empty-asm pad anchor: the Platform entry's -O2 executable
  is 18,330,832 bytes, +5.3 % over `main` 774b977 (17,405,512; +11.2 % on `ci/il-stack-traces`); the rest
  is the per-call-site recording (LSDAs +420 KB, pads +197 KB). TechEmpower run 41291420634: 277 of 456 on
  EPYC 7763 (fortunes 1.04-1.18 of NativeAOT, executable 17.48 vs 16.36 MiB; `ci/m7-perf-3` had 276 of 456,
  its start-up regression, 1.4 s vs 0.7 s, is unchanged here); its first attempt ran on a Xeon 6973P-C,
  where NativeAOT's fortunes are 2.5x other runners' (275 of 449, fortunes 0.73-0.86). Fixes: List<T>.Sort/Array.Sort wrap what the comparison throws
  (sample 910); Min/Max of empty arrays and lists as .NET (the period of MemoryExtensions' message; null
  for reference types; sample 911); DirectoryNotFoundException.DirectoryPath and its "Directory path"
  line (sample 912); IL stack slots that merge a list and an array (`list ?? (IEnumerable<T>)[]`, Npgsql's
  builders) typed as both, so the array's interface map includes the interface (sample 913); `nint.ToString()`
  through IL (sample 846: "Program as IL" green again).

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
  21 s. Now (run 41228623899, `28befb0`): plaintext 1.147/1.123, JSON 1.009/1.021/1.019, JSON p99
  0.32–0.67 of NativeAOT's, start-up 21 vs 28 ms, idle RSS 13.1 vs 17.3 MiB, RSS after load 72.3 vs 65.0
  MiB (68.8 in run 41220070541), executable 10.6 vs 8.9 MiB, build 190 vs 22 s; Benchmarks instruction
  counts (run 41228623885) within 0.3 % of main on x86-64, arm64 churn +1.2 % (threaded, noisy). Changes:
  guarded devirtualization of interface calls outside the corelib (per-slot stubs filled once the
  constructed classes are known: +5–8 % plaintext), leaf-loop type tests,
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
- `ci/m7-correctness` — M7 silent differences and gaps. Console.Out writes through on every call and SIGPIPE
  is ignored, as in .NET (a killed server lost its log; `app | head -1` killed csharpc's program); Process.Start
  redirects standard input; `Socket.ConnectAsync` is an engine operation honouring its token (Npgsql's 15 s
  connect timeout: csharpc's /db waited ~2 min for the kernel's); ExternalException/Win32Exception ToString as
  .NET's; `[DynamicDependency]` keeps methods, with lazy reflection invokers (Platform -O0 +0.8 %);
  ISpanFormattable/IUtf8SpanFormattable on decimal, char, dates, TimeSpan, Guid (DateOnly/TimeOnly/Guid emitted
  invalid IR before); inbox text encoders encode UTF-8 by allowed runs and Utf8 transcoding narrows ASCII runs
  (invalid UTF-8 now escaped as .NET does); C# interceptors applied (`csharpc Platform.csproj` runs);
  `Socket.Blocking = false`/`Select` on Windows. Kestrel's application-exception handling already matched the
  JIT (tests/il.sh IlKestrelErrorsApp). Samples 860–866. TechEmpower run 41253596724: **258 of 456** (was 242;
  worse 120 → 81); fortunes 0.77–0.92 (was 0.57–0.76); cached queries 10–100 still 0.59–0.87, which is the
  JSON serialization of `CachedWorld[]` (~3× the JIT's per array locally; MemoryCache lookups are faster
  than the JIT's).
- `ci/m7-perf-2` — M7 performance, second round (on main `365768e`). Adaptive collector budget (growth and
  floor follow the pause overhead; Benchmarks instruction counts as main). `ValueTask<T>` as .NET's
  two-word struct (corelib `Tasks/ValueTaskOfT.cs`, [10](10-async.md)): the interim app's plaintext
  allocations dropped from 4.7 GiB to 0.35 GiB per run, RSS at 256/16384 connections 50.8/739 → 25.5/257
  MiB (NativeAOT 27.7/259; M7 perf run 41241789613). Interface stubs guard up to 8 classes sharing up to
  4 implementations, and the cached dispatch tests the site's cache inline. The collector gives surplus
  empty chunks back to the OS and scans the collecting thread from the collection's entry, without the
  allocation caches (fewer false roots). The optimizer keeps the type data out of the inliner's call
  graph (one reference SCC of 11,465 call SCCs made `removeInternalRefEdge` ~40 % of -O2): interim
  build 195 → 141 s on CI. JSON writer string properties in one pass (a source-generated serialize
  loop 165 → 95 ns, JIT 50). Real-entry verdict (TechEmpower run 41247424405, before the JSON writer
  change): **275 of 456** (was 242), JSON 1.09–1.13×, plaintext 1.14/1.13/0.98/1.07 at
  256/1024/4096/16384. Left in this area: plaintext latency at 4096 connections, JSON RSS flat at
  42 MiB from the first level (budget floor; a floor of 8 MiB under 0.5 % GC overhead gave 35.5 MiB in run
  41254935185 but was reverted: one runner's JSON rounds spread to 0.82 of NativeAOT), GC pauses of
  ~25 ms at 4096+ plaintext connections (stop-the-world marking of ~1.4 M live objects: the latency gap), executable +0.2 MiB, build 150 vs 24 s (single-threaded
  optimizer: per-partition optimization next), the rest of the JSON writer's 1,100 instructions per call.
  The M7 perf workflow also profiles the Platform entry's json and plaintext tests.
- `ci/m7-gc-concurrent` — concurrent marking ([ADR 0020](adr/0020-concurrent-marking.md), on main `4cc0e5b`). Step 1:
  `--gc-concurrent` emits the marker's barrier through the nursery pass's store discovery (an inline volatile flag
  test per heap reference store, the log call out of line; the runtime's CSRT_STORE_REF/CSRT_DIRTY too); no marker
  yet, the default build unchanged. Benchmarks run 41294402756, executed instructions with the barrier: binarytrees
  +4.5 % (arm64 +3.4), sbops +3.0 (+5.4), churn +2.7 (+1.0), exprtree +2.0 (x86-64), strbuild +0.7 (+2.0), the rest
  under 1 % (the nursery's barrier measured the same way: binarytrees +12.6, strbuild +45.4). What costs is
  initializing stores into fresh objects; skipping them is only sound if objects allocated during a cycle are
  rescanned at the remark. Next: the marker thread, buffers, allocate-black, frame rescans, the remark.
- `ci/m7-gc-concurrent` (step 2) — the concurrent marker behind `--gc-concurrent` ([ADR 0020](adr/0020-concurrent-marking.md),
  "Step 2"). Platform plaintext: stop-the-world collections 13-23 ms each, concurrent pauses p50 0.6-0.9 ms, max
  5.2-5.4 ms, of which 4.5 ms is the remark's rescan of coroutine frames (next: concurrent pre-cleaning of frames);
  livemutate pause 32 -> 0.2 ms; wall time on arm64 +9-11 % on binarytrees/churn/exprtree, RSS +33 % in the GC suite.
  GC concurrent workflow green.
- `ci/m7-gc-concurrent` (step 3) — frame pre-cleaning, cheaper barriers, pacing ([ADR 0020](adr/0020-concurrent-marking.md),
  "Step 3"); `--gc-concurrent` stays off by default. What changed:
  - **Coroutine frames.** The remark rescans only the frames still running (a running-frame stack per thread). Their
    part of the remark went from 4.5 ms to 0.00 ms.
  - **Barriers removed:**
    - stores of fresh values, matched after the optimizer through a marker on the inlined allocator;
    - copies tagged reference-free;
    - copies out of constant data.
  - **Value barriers for small aggregates.** A store of a small aggregate (a span) gets a value barrier per reference.
    The range barrier took the slot's address, which kept `out` spans from being promoted to registers, and its call
    inside a lazy initialization stopped LLVM from hoisting loads out of strbuild's IndexOf loop.
  - **Atomic mark bits** only while a cycle runs.
  - **Pacing.** Cycles start early, and the next budget leaves out the cycle's black allocation.

  Measured (M7 perf 41319120179, Benchmarks 41319120195, TechEmpower 41315086945):
  - **Platform plaintext pauses:** p50 0.4 ms, max 0.9–3.5 ms over three runs. The frames' part is now 0; the rest is
    the stop-the-world handshake. Stop-the-world collections were 13–23 ms each.
  - **Plaintext at 4096 from the new latency probe:** p99 79 ms for csharpc against 45 ms for NativeAOT in the
    Platform entry without the flag. With the flag (a different runner): 55 ms against 48 ms. At 16384 csharpc is
    better (239 ms against 309 ms).
  - **Interim plaintext throughput on one CPU type** (EPYC 7763): 1.06–1.16× NativeAOT without the flag, 1.03–1.08×
    with it, about −5 % from the flag.
  - **Benchmarks wall time on arm64:** binarytrees and churn +13 %, exprtree +23 %, even though the arm64 instruction
    counts rose only 4.5 % and 9 %. x86-64 is within noise.
  - **Executed instructions on x86-64 after this round:** binarytrees +0.6 %, churn +1.0 %, exprtree +3.1 %, listsort
    +2.5 %, strbuild +36 → +5.5 %, sbops +7.6 %, wordfreq +5.5 %. wordfreq's cost is spread across its Dictionary
    loop (12 instructions per iteration, register pressure, no barrier on the path); about half of it is the
    allocator marker.
  - **GC suite peak RSS:** 256 → 207 MiB (stop-the-world: 209).
  - **M7 verdict** (TechEmpower 41315086945, default build): 308 of 456.

  Default: still off. Speed comes first, and the flag still costs about 5 % plaintext throughput and 13–23 % wall time
  on arm64's allocation-heavy programs.
- `ci/m7-perf-4` — M7 performance, fourth round (on main `774b977`). `ArrayPool<T>.Shared` pools (it allocated a
  fresh array per Rent): the Platform entry's cached queries allocated 13.3 → 3.2 GB per run, GC 1.56 → 0.77 s;
  cached queries 1.08/1.14/1.05/0.99/0.93× at 1-100 on an Intel runner (M7 perf 41288905514; were
  1.12/1.10/0.97/0.88/0.81 on AMD, 41283379606). JSON writer: integer properties and start tokens in one
  capacity check (CachedWorld[] 740-840 ns locally, JIT 775; a 100-query request 14.3 µs vs the JIT's
  14.7). Found: a stop-the-world collection of the server-shaped heap is ~100 % marking (sort, roots, sweep
  under 0.3 ms of 21 ms), and the parallel marker (CSRT_GC_PARALLEL_MARK) is slower than the serial one (35
  vs 21 ms) — shorter pauses need concurrent marking (a write barrier). Interim size gap: type metadata is
  ~2 MB (csharpc.typeinfo.entries 933 KB at 96 bytes per type, type-name strings 530 KB, typenames 186 KB,
  interface/argument lists 240 KB); 32-bit relative fields would save ~0.4 MB. Intel vs AMD: CPU per
  request drops by the same ~30-40 % for both builds on Intel; the ratio shift is a few %, not a hot spot.
- `ci/m7-perf-3` — M7 performance, third round (on main `9d234c3`). Large executables are optimized and
  code-generated per partition in parallel (ADR 0014 §5: small callees and constants imported as
  available_externally, shared symbols hidden-external, a section per function for --gc-sections):
  interim build 140 → 71-103 s on CI with the same throughput ratios (M7 perf 41269198431), Platform
  optimize+codegen 90 s on 8 partitions. JSON writer: integers into the buffer, arrays by index, inline
  capacity checks, the typed fast path per element (a 20-element CachedWorld[] 1,650 → ~950 ns locally,
  JIT 775). Collector marking: prefetch FIFO, no division per candidate, insertion sort of the
  large-object table (a server-shaped heap's pause 27-29 → 20-21 ms locally). Pool workers poll 100 µs
  (was 30) before sleeping. `LLVMLinkage.Appending` was 6 (WeakODR): fixed. Profiled Platform job (M7 perf
  41273945069, one runner): /db 1.11-1.18× with 355k vs 650k context switches, JSON 1.06-1.12×,
  plaintext 1.15/1.12/0.96/1.05, cached queries still 1.17/0.94/0.87/0.79/0.76 at 1-100 (MemoryCache
  lookups and the serializer's per-property layers). Left: cached queries at 20-100, plaintext p99 at
  4096 (GC pauses, now ~25 % shorter), Intel runners give csharpc lower ratios than AMD ones.
- `ci/m7-deploy` — M7 deployment metrics and two silent differences. Idle RSS of the Platform entry 49.5 →
  27.6 MiB (NativeAOT 37.7; TechEmpower runs 41260599507 and 41266326519: **275 and 264 of 456**, the verdict moving with the runner, 232–275 on this branch): the collector kept the peak its
  start-up's cache fill reached (31 MiB of chunks for 8 MiB live); the pool's timer thread now gives free slot
  pages back to the OS once allocation goes quiet (gc.c, "Idle trimming"; size budget linux-x86_64 async
  +4 KiB). Rebased onto main `97ca487`: the idle trim and `ci/m7-perf-2`'s surplus release share one
  `released` flag (release_chunk, now on Windows and macOS too), and a reused released chunk skips clearing
  where released pages read as zero (Linux, Windows; size budget windows-x86_64 async +6 KiB). On the rebased
  head (TechEmpower run 41273842626): **243 of 456** (35 worse), start-up 628 vs 752 ms and idle RSS 26.5 vs
  37.8 MiB, both better on that runner (start-up CPU 0.96 vs 1.28 s, context switches 39.4k vs 35.9k). Start-up is still behind (747–751 vs 600–610 ms in runs 41266326519/41268870848, with about the
  same CPU time, 1.11 vs 1.00 s: the 10,000-query fill is latency-bound): the timer thread no longer wakes per
  Npgsql command timeout, and the socket engine takes no lock for readiness with nothing pending (futex calls
  to the first response 204k → 116k locally, NativeAOT 60k; start-up 762 → 747 ms). Left: ~38.4k voluntary
  context switches to the first response against NativeAOT's ~27.5k, about one more thread hand-off per query
  (pool wake-ups: workers poll 30 µs, less than a database round trip). A Monitor spin before parking was
  measured and reverted (no change). The same latency makes `/db` at 16–32 connections lose on fast runners
  (0.84–0.87 in run 41263341198). The report now shows start-up CPU, context switches and the idle RSS split. `UseSystemResourceKeys=true` is refused (compile time, and `AppContext.SetSwitch` at run time):
  .NET prints resource keys, csharpc's corelib full messages (sample 880; the interim app dropped it).
  `new SocketException(SocketError)` takes .NET's errno for each value (11 of 47 differed on Linux) and WASI's
  numbering on WebAssembly ("Connection refused" was another text there); samples 881–882. Harness: start-up
  CPU time and idle RSS by mapping in the deploy log. Found, not fixed (needs scheduling): for an exception
  thrown inside Npgsql (the entry against a closed port) the JIT prints 13 Npgsql/System.Net frames and an
  `--- End of stack trace from previous location ---` line before the program's two; csharpc prints only the
  program's two. It is ADR 0012's documented gap (frames of IL-compiled methods aren't recorded; BCL frames
  only for listed members) and warned with CSLLVM0006 at compile time, but the text differs from the oracle.
- `ci/m7-platform` — csharpc builds and runs TechEmpower's Platform entry (`csharpc Platform.dll`, frameworks
  from its runtimeconfig.json); all seven test types answer as the JIT does (identical verify.py
  transcripts locally). Fixes: SO_REUSEADDR on bind, non-blocking connect/Select, async NetworkStream over
  the socket engine, Normalize, HMAC one-shots, scheduler pair, counters, AlternateLookup, IConvertible on
  boxed builtins, IDictionary on Dictionary, run-time Enum.Parse, TextEncoder surface, [DynamicDependency],
  span ToArray typing (samples 840–849; [18](18-compatibility.md#techempowers-platform-entry)). The harness
  fails the run again when csharpc can't build the entry. Includes `m7-project-generators` (project input
  runs the build's source generators; `csharpc Platform.csproj` compiles but stops at run time in
  `config.Get<AppSettings>()`: the configuration binder's interceptors). MySqlConnector 2.6.1 in the app
  (Dependabot #1). Left: `-O2` build time (the optimizer ~570 s single-threaded locally).
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
