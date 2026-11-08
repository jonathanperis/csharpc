# csharpc documentation

Start with the [top-level README](../README.md) for what csharpc is and how to install and use it, and with
[AGENTS.md](../AGENTS.md) for the rules of the repository (the one rule, the workflow, the test commands).

| # | Document | Contents |
|---|---|---|
| 00 | [Vision](00-vision.md) | Goals, non-goals, what success looks like |
| 01 | [Prior art](01-prior-art.md) | What we borrow from clang, rustc, gollvm and the .NET AOT lineage; source→LLVM vs IL→LLVM |
| 02 | [Architecture](02-architecture.md) | Pipeline, phases, Roslyn CFG as MIR, flow captures, linking, diagnostics |
| 03 | [Lowering](03-lowering.md) | C# construct → LLVM IR, with real compiler output |
| 04 | [Runtime](04-runtime.md) | csrt ABI and contents, exceptions and stack traces, the C / C# boundary, platforms, GC, finalizers |
| 05 | [Testing](05-testing.md) | Differential testing against `dotnet run`, the suites, CI |
| 06 | [Roadmap](06-roadmap.md) | Milestone status, the feature-support matrix, the standing goal |
| 07 | [Spike log](07-spike-log.md) | Dated journal of the first days and lessons learned |
| 08 | [Drop-in toolchain](08-drop-in.md) | `.csproj` input, `dotnet build/run/publish` integration, packaging, limits |
| 09 | [Conformance](09-conformance.md) | The 195-program conformance corpus |
| 10 | [Async](10-async.md) | async/await, iterators and async iterators as LLVM coroutines; the thread-pool scheduler |
| 11 | [BCL value types](11-bcl-value-types.md) | decimal, TimeSpan/DateTime/Stopwatch, BigInteger, Type objects, Array helpers and sorting |
| 12 | [Corelib](12-corelib.md) | The shadow corelib: BCL types written in C#, redirected from the real reference assemblies |
| 13 | [Debugging](13-debugging.md) | `-g`, DWARF debug info, gdb/lldb pretty-printers |
| 14 | [Threading](14-threading.md) | Threads, the thread-safe GC (stop-the-world handshake), monitors, `Interlocked`, `Parallel` |
| 15 | [Conformance corpus 2](15-conformance2.md) | Adversarial edge-case programs, wrong-output bugs found and fixed |
| 16 | [Interop and I/O](16-interop-io.md) | P/Invoke, `LibraryImport`, source generators, System.IO, sockets |
| 17 | [Security](17-security.md) | Memory safety, exploit mitigations (`--hardening`), sanitizers and fuzzing, reproducible builds |
| 18 | [Compatibility](18-compatibility.md) | Real-world compatibility: Microsoft sample programs vs .NET and NativeAOT, the IL survey, ASP.NET Core |
| 19 | [Globalization](19-globalization.md) | Cultures via ICU, culture selection, invariant mode, size |
| 20 | [Status](20-status.md) | Where the project stands and how to pick the work up |
| | [Contributing](CONTRIBUTING.md) | Reporting bugs, adding a test, CI, where things live |

## Architecture decision records

| ADR | Decision |
|---|---|
| [0001](adr/0001-roslyn-as-frontend.md) | Roslyn is the C# front-end |
| [0002](adr/0002-llvm-c-api-via-libraryimport.md) | Bind the LLVM C API with `[LibraryImport]` |
| [0003](adr/0003-roslyn-cfg-as-mir.md) | Roslyn's `ControlFlowGraph` is the mid-level IR |
| [0004](adr/0004-memory-management.md) | Memory management: none → conservative GC → precise GC; the opt-in nursery |
| [0005](adr/0005-exceptions.md) | Exceptions: LLVM EH with an Itanium-style personality |
| [0006](adr/0006-closed-world-compilation.md) | Closed-world, whole-program compilation |
| [0007](adr/0007-runtime-in-c-first.md) | Bootstrap runtime in C, corelib in C# later |
| [0008](adr/0008-runtime-in-csharp.md) | Runtime library in C# (corelib), C only at the OS boundary |
| [0009](adr/0009-hardening-by-default.md) | Exploit mitigations on by default, CET opt-in |
| [0010](adr/0010-il-frontend.md) | IL front-end: IL as the method-body source for Roslyn's metadata symbols; the program as IL |
| [0011](adr/0011-wasm32.md) | WebAssembly: explicit exception propagation, compiler-spilled GC roots, one thread |
| [0012](adr/0012-stack-traces.md) | Stack traces: a landing pad per call site, names and lines from Roslyn's own PDB |
| [0013](adr/0013-framework-il.md) | Framework assemblies the program uses compiled from the shared framework's IL (`--framework-il`) |
| [0014](adr/0014-compile-time.md) | Compile-time scale: a ReadyToRun compiler, parallel parsing/binding/CFGs (`-j`), machine code per module partition, bounded memory |
| [0015](adr/0015-corelib-il.md) | The corelib compiled to IL once (cached) and lowered through the IL front-end (`--corelib il`, default) |
| [0016](adr/0016-nativeaot-parity-and-end-state.md) | NativeAOT parity as the support target; csharpc as the AOT backend of a dotnet fork as the end state |
| [0017](adr/0017-runtime-tiers.md) | The runtime in three tiers: ordinary C#, compiler-checked "systems C#" (GC, EH, threads), a minimal native floor |
| [0018](adr/0018-reflection-metadata.md) | Member reflection over compile-time metadata, kept as NativeAOT keeps it |
| [0019](adr/0019-dynamic.md) | `dynamic` bound at compile time with Roslyn, per run-time type, in a dispatch function per site |
| [0020](adr/0020-concurrent-marking.md) | Mostly-concurrent marking behind `--gc-concurrent` |
| [0021](adr/0021-finalizers.md) | Finalizers, `GC.SuppressFinalize`, and the `ObjectiveCMarshal` reference tracking |
| [0022](adr/0022-tls.md) | TLS and X.509: the corelib over OpenSSL, not the framework's IL |
| [0023](adr/0023-framework-il-first.md) | Framework IL first: hand-implement only the runtime-coupled core, compile the rest of the framework from its IL, per library by measurement (`CSHARPC_IL_FIRST`) |

The docs describe the code as it is. When a feature lands, update the feature matrix and the open bullets in
[06-roadmap.md](06-roadmap.md), and the document of the area it touches.
