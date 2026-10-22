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

The docs describe the code as of the commit they were written at. When a feature lands, update the
matrix in [06-roadmap.md](06-roadmap.md) and add a dated entry to [07-spike-log.md](07-spike-log.md).
- [09 — Conformance](09-conformance.md): conformance corpus, coverage per area, prioritized failure reasons
- [10 — Async](10-async.md): async/await, iterators and async iterators as LLVM coroutines; the single-threaded scheduler
- [11 — BCL value types](11-bcl-value-types.md): decimal, TimeSpan/DateTime/Stopwatch, BigInteger, Type objects, Array helpers & sorting, culture comparison
- [12 — Corelib](12-corelib.md): the shadow corelib — BCL collections and LINQ written in C#, redirected from the real reference assemblies
