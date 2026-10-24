# ADR 0014 — Compile-time scale: a precompiled compiler, parallel front-end work, partitioned code generation

- **Status:** Accepted (2026-10-24), implemented (`src/Csharpc/Csharpc.csproj`, `Driver/Parallelism.cs`,
  `CodeGen/ModuleCodeGen.Cfgs.cs`, `Driver/ParallelCodegen.cs`).

## Context

M6 (csharpc compiles csharpc and Roslyn) means a closed world of ~40 MB of IL, and the standing goal
([06-roadmap.md](../06-roadmap.md#standing-goal-better-than-net-in-every-area-always)) counts compile
time as part of "faster". Measured before this change on the shared 4-vCPU VPS, a program reading
metadata with System.Reflection.Metadata from IL (`tests/projects/il/IlMetadataApp`) took ~9 s at
`-O0` and ~20 s at `-O2`; a program that formats a double ~10 s at `-O2` since the globalization port.
`csharpc -v` attributed the time to:

1. **JIT warm-up.** Roslyn's NuGet assemblies are IL only: every compile JIT-compiled Roslyn's parser
   and binder and csharpc's code generator before doing any work (~1.5 s of CPU for hello world).
2. **Roslyn binding inside lowering.** Corelib and generic code is lowered on demand; building
   Roslyn's control-flow graph of such a body (binding it, then `ControlFlowGraph.Create`) was ~75 % of
   its lowering time, done once per generic *instantiation*, on one thread.
3. **Roslyn's fixed cost.** The corelib (173 files, ~4.5 MB of C#) is parsed for every program; the
   program's trees are bound one after another.
4. **The LLVM backend.** At `-O2` optimization plus code generation were ~60 % of a compile, all on one
   thread (`-O0`: code generation alone, 1.2 s for the metadata program).

## Decision

### 1. The compiler ships precompiled (ReadyToRun) and without dynamic PGO

Release builds of csharpc (`dotnet build -c Release`, which is what the tests, the README and the
Csharpc.Sdk package use) run crossgen2 over `csharpc.dll` and the Roslyn assemblies: the SDK's own
`Microsoft.NETCore.App.Crossgen2.<host RID>` package, fetched with `PackageDownload`, one assembly at a
time with per-item incremental inputs (Roslyn once, ~2 s for `csharpc.dll` after a change). The images
replace the IL ones in the output and the publish directory. ReadyToRun code is version-resilient: on
a newer runtime, code that doesn't match is re-JIT-ed rather than run. Dynamic PGO (`TieredPGO`) is off:
its instrumented tier costs a process that lives a few seconds more than it returns. `-p:CsharpcReadyToRun=false`
opts out; Debug, `PublishAot` (`tools/publish-native.sh`) and explicit `PublishReadyToRun` builds are
unaffected.

### 2. A compilation uses several threads (`-j N`, `CSHARPC_THREADS`, default: the processor count)

Every parallel phase merges its results in a fixed order, so the thread count never changes the output.

- **Parsing:** the program's and the corelib's files are parsed in parallel, keeping tree order.
- **User diagnostics:** each of the program's trees is bound on its own thread; diagnostics are reported
  in tree order.
- **CFGs ahead of lowering** (`CodeGen/ModuleCodeGen.Cfgs.cs`): a CFG is immutable and independent of
  the substitution it is lowered under, so it is built once per syntax node (every instantiation of a
  generic method reuses it). When `GetFunction` queues a corelib or generic method for lowering, its body
  is queued for N−1 background threads, which build CFGs in queue order ahead of the lowering thread
  (one `Lazy` per syntax node: the lowering thread takes a finished graph, waits for one in progress, or
  builds it itself). Roslyn's semantic models are thread-safe; lowering itself (LLVM IR construction)
  stays on one thread. Not used with `--dump-cfg`, `-j 1` or `CSHARPC_SERIAL_CFG=1`. This replaces
  the thread-pool prefetch main gained meanwhile (one graph per instantiation, not limited by `-j`);
  the program's own methods are still queued up front.
- **The C runtime**, on a cache miss: one clang per file and thread.

### 3. Machine code is generated per partition, in parallel (`Driver/ParallelCodegen.cs`)

The program is still optimized as **one** module — whole-program optimization (inlining across the
program, the runtime and the corelib; devirtualization; dead-code elimination) is unchanged. Only the
backend, which works function by function, is split:

1. After optimization the defined functions are cut, in module order, into contiguous partitions of
   ~10,000 instructions (at most 8; a smaller module stays one object, as before — hello world).
2. Internal symbols become hidden externals with a reserved `__csp_` prefix (so they cannot clash with
   a global of another object of the link, the runtime's or a static libc's), except what `llvm.used`
   names, which keeps its name (lld's symbol ordering file places `csrt_code_start`/`csrt_data_start`).
   Hidden symbols bind within the executable only and are stripped from it like before.
3. The module is written once as bitcode and freed. Each partition parses that image into an LLVM
   context of its own, turns the other partitions' functions into declarations in place (linkage
   `available_externally`, then LLVM's `elim-avail-extern`: name, type, calling convention and
   attributes stay, so call sites are compiled exactly as before), and is emitted by a target machine
   of its own. Partition 0 keeps every global variable, module-level asm and the `llvm.*` lists (and
   the functions they name); the other partitions declare them.
4. The objects are linked in partition order. The calling thread is one of the workers, so `-j 1`
   needs no more memory than one object did.

The partitioning depends on the module only, never on the thread count: a program compiles to the same
executable at every `-j`. Not used with `-g` (one DWARF compile unit per program, as the debugger tests
expect), for wasm32 (exception lowering and root spilling are whole-module), or for `--emit obj|asm`.
`CSHARPC_CODEGEN_UNITS=N` forces a partition count (for testing).

### 4. Roslyn's memory is released before LLVM runs

Lowering leaves most of the managed heap in semantic models, operation trees and CFGs nothing reads
afterwards, while the LLVM phases reach the process's peak RSS on top of it. `ModuleCodeGen.Generate`
drops its model and CFG caches; when it built many CFGs (≥ 500) the driver runs one full compacting
collection (~0.15 s) before optimization.

### Rejected or deferred

- **Caching lowered code across compilations** (per-method bitcode in `~/.cache/csharpc`): a corelib
  method's IR depends on the whole program — which generic instantiations exist, class layouts and
  vtable slots (only dispatched slots are kept), devirtualization by class-hierarchy analysis, which
  static initializers run eagerly, whether stack traces are recorded. A key covering all of that is the
  program itself. The C runtime stays the only cross-compilation cache.
- **Persisting Roslyn's CFGs or bound trees:** not serializable, and tied to one compilation's symbols.
- **Splitting the optimizer:** in `default<O2>` the CGSCC inliner and the function simplification it
  drives (~80 % of the pipeline for the metadata program, measured with `opt -passes=...` on its
  bitcode) need the whole call graph; only the final per-function pipeline (vectorizers, unrolling,
  ~15 %) is partitionable, and running module passes per partition (global DCE, constant merging,
  relative lookup tables) would change the code. ThinLTO-style summaries are the route if `-O2`
  optimization becomes the bottleneck for Roslyn-sized programs.
- **NativeAOT-compiled csharpc by default:** faster still to start (`tools/publish-native.sh`), but
  a platform-specific single binary instead of the framework-dependent tool the SDK package and
  `dotnet tool install` use; ReadyToRun gets most of the gain with the same deployment.

## Consequences

Measured 2026-10-24 on the shared 4-vCPU VPS (load average ~5 from other jobs), before (main with its
thread-pool CFG prefetch, csharpc on the JIT) and after interleaved, best of 2, `-j 2`: wall / CPU /
peak RSS.

| program | `-O0` before | `-O0` after | `-O2` before | `-O2` after |
|---|---:|---:|---:|---:|
| hello world | 3.1 s / 3.1 s / 205 MB | **1.4 s / 1.6 s** / 202 MB | 3.5 s / 3.5 s / 217 MB | **1.8 s / 2.0 s** / 219 MB |
| double + DateTime formatting | 5.3 s / 6.7 s / 261 MB | **3.2 s / 4.2 s** / 235 MB | 8.6 s / 9.9 s / 273 MB | **6.4 s / 7.7 s** / 257 MB |
| tests/programs/numbers | 5.0 s / 5.9 s / 245 MB | **2.8 s / 3.7 s** / 238 MB | 7.8 s / 8.8 s / 258 MB | **5.2 s / 6.4 s** / 265 MB |
| tests/programs/interpreter | 5.1 s / 6.4 s / 251 MB | **3.3 s / 4.2 s** / 232 MB | 9.3 s / 9.9 s / 260 MB | **7.7 s / 7.6 s** / 257 MB |
| IlMetadataApp (System.Reflection.Metadata from IL) | 7.5 s / 9.2 s / 310 MB | **5.5 s / 7.5 s** / 293 MB | 18.1 s / 19.5 s / 332 MB | **15.3 s / 17.3 s** / 338 MB |

Against the branch point (before main's globalization trimming and CFG prefetch) at load ~10 the
gains were larger: hello 3.4 → 1.9 s (`-O0`), the metadata program 11.2 → 7.8 s (`-O0`) and 22.2 →
18.5 s (`-O2`). The metadata program's backend alone went from 4.2–4.7 s to 2.4 s (`-j 2`) and
2.1 s (`-j 4`). On CI the differential suite (`tests/run.sh --all-opts`, ~280 samples twice) went from
4101 s to 2554 s on x86-64 and from 2297 s to 1666 s on wasm32 with the ReadyToRun compiler and the CFG
threads alone (against the branch point).

- Release builds of csharpc take ~25 s longer the first time (crossgen2 over Roslyn) and ~2 s longer
  after a change to csharpc.
- Executables differ from single-object ones by a few bytes (double/DateTime program: 373,448 →
  373,456; the metadata program 783,272 → 783,288): the same code, plus per-object alignment.
- Peak memory grows with the thread count during code generation (each busy thread holds a parsed
  partition): metadata program at `-O2`, 366 MB with one object, 368 MB at `-j 1`, 393 MB at `-j 2`,
  445 MB at `-j 4` — before releasing Roslyn's memory ahead of LLVM, which brings `-j 2` back to
  371 MB (`-O0`: 340 → 315 MB; the double/DateTime program at `-O2`: 335 → 300 MB).

## Next hot spots for a Roslyn-sized closed world

1. **The corelib through Roslyn on every compile.** Parsing it and binding the bodies the program
   reaches are still the largest fixed and per-method costs at `-O0`. Compiling the corelib to IL once
   (cached by corelib sources and target defines) and lowering it through the IL front-end like any
   framework assembly would remove both, with IL decoding at about half the per-method cost.
2. **Lowering is single-threaded** after the CFGs: emitting LLVM IR for independent functions needs a
   context per thread and a merge (or lowering straight into partitions), since an LLVM context is
   not thread-safe.
3. **`-O2` optimization** stays one thread over the whole program (see above).
4. **Memory:** each code-generation thread parses the whole bitcode image before pruning it; lazy
   bitcode loading (or per-partition images) would bound it by the partition, and lowering should drop
   Roslyn operation trees and CFGs as soon as a function is emitted.
5. **Tiered compilation's background JIT** rejits hot Roslyn and csharpc methods while LLVM runs
   (~3 s of CPU at `-O2` for the double-formatting program, on another core); `TieredCompilation=false`
   removes it but slows the managed phases on ReadyToRun code.
