# csharpc documentation

Start with the [top-level README](../README.md) for the quick start.

| # | Document | Contents |
|---|---|---|
| 00 | [Vision](00-vision.md) | The dream, goals, non-goals |
| 01 | [Prior art](01-prior-art.md) | What we borrow from clang, rustc, gollvm and the .NET AOT lineage; source→LLVM vs IL→LLVM |
| 02 | [Architecture](02-architecture.md) | Pipeline, phases, Roslyn CFG as MIR, flow captures, linking |
| 03 | [Lowering](03-lowering.md) | C# construct → LLVM IR, with real compiler output |
| 04 | [Runtime](04-runtime.md) | csrt ABI, formatting, exceptions, the C# corelib plan, GC plan |
| 05 | [Testing](05-testing.md) | Differential testing against `dotnet run` |
| 06 | [Roadmap](06-roadmap.md) | Milestones M0–M4 and the feature-support matrix |
| 07 | [Spike log](07-spike-log.md) | Dated journal and lessons learned |
| 08 | [Drop-in toolchain](08-drop-in.md) | `.csproj` input, `dotnet build/run/publish` integration, packaging, gaps |

## Architecture decision records

| ADR | Decision |
|---|---|
| [0001](adr/0001-roslyn-as-frontend.md) | Roslyn is the C# front-end |
| [0002](adr/0002-llvm-c-api-via-libraryimport.md) | Bind the LLVM C API with `[LibraryImport]` |
| [0003](adr/0003-roslyn-cfg-as-mir.md) | Roslyn's `ControlFlowGraph` is the mid-level IR |
| [0004](adr/0004-memory-management.md) | Memory management: none → conservative GC → precise GC |
| [0005](adr/0005-exceptions.md) | Exceptions: abort now, LLVM EH with an Itanium-style personality next |
| [0006](adr/0006-closed-world-compilation.md) | Closed-world, whole-program compilation |
| [0007](adr/0007-runtime-in-c-first.md) | Bootstrap runtime in C, corelib in C# later |
| [0008](adr/0008-runtime-in-csharp.md) | Runtime library in C# (corelib), C only at the OS boundary |
| [0009](adr/0009-hardening-by-default.md) | Exploit mitigations on by default, CET opt-in |
| [0010](adr/0010-il-frontend.md) | IL front-end: IL as the method-body source for Roslyn's metadata symbols |
| [0011](adr/0011-wasm32.md) | WebAssembly: explicit exception propagation, compiler-spilled GC roots, one thread |
| [0012](adr/0012-stack-traces.md) | Stack traces: a landing pad per call site, names and lines from Roslyn's own PDB |
| [0013](adr/0013-framework-il.md) | Framework assemblies the program uses compiled from the shared framework's IL (`--framework-il`) |
| [0014](adr/0014-compile-time.md) | Compile-time scale: a ReadyToRun compiler, parallel parsing/binding/CFGs (`-j`), machine code per module partition |
| [0015](adr/0015-corelib-il.md) | The corelib compiled to IL once (cached) and lowered through the IL front-end (`--corelib il`, default) |
| [0016](adr/0016-nativeaot-parity-and-end-state.md) | NativeAOT parity as the support target; csharpc as the AOT backend of a dotnet fork as the end state |
| [0017](adr/0017-runtime-tiers.md) | The runtime in three tiers: ordinary C#, compiler-checked "systems C#" (GC, EH, threads), a minimal native floor |

The docs describe the code as of the commit they were written at. When a feature lands, update the
matrix in [06-roadmap.md](06-roadmap.md) and add a dated entry to [07-spike-log.md](07-spike-log.md).
- [09 — Conformance](09-conformance.md): conformance corpus, coverage per area, prioritized failure reasons
- [10 — Async](10-async.md): async/await, iterators and async iterators as LLVM coroutines; the single-threaded scheduler
- [11 — BCL value types](11-bcl-value-types.md): decimal, TimeSpan/DateTime/Stopwatch, BigInteger, Type objects, Array helpers & sorting, culture comparison
- [12 — Corelib](12-corelib.md): the shadow corelib — BCL collections and LINQ written in C#, redirected from the real reference assemblies
- [13 — Debugging](13-debugging.md): `-g`, DWARF debug info, gdb/lldb pretty-printers
- [14 — Threading](14-threading.md): threads, thread-safe GC (stop-the-world handshake), monitors, Interlocked, Parallel
- [15 — Conformance corpus 2](15-conformance2.md): 258 adversarial edge-case programs, wrong-output bugs found and fixed, remaining gaps
- [17 — Security](17-security.md): memory safety, exploit mitigations (`--hardening`), sanitizers and fuzzing, reproducible builds
- [18 — Compatibility](18-compatibility.md): real-world compatibility report — unmodified Microsoft sample/docs console programs vs .NET, pass rate, ranked blockers, bugs found
- [19 — Globalization](19-globalization.md): cultures via ICU (ported System.Globalization and its native shim), culture selection, invariant mode, size
