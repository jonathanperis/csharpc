# 00 — Vision

## The dream

C, C++, Rust, Swift, Zig, Go (via gollvm), Fortran (flang), Julia and many others share one optimizer
and one set of code generators: LLVM. Each has a *front-end* that understands the language and lowers
it to LLVM IR. LLVM handles everything after that: SSA optimization, vectorization, instruction
selection for a dozen architectures, object files, debug info and link-time optimization.

C# has never had such a front-end. Its compilers target IL, and IL is executed by a JIT (CoreCLR,
Mono) or turned into native code by .NET's own AOT compilers, which reuse the RyuJIT backend. LLVM
shows up only at the edges of the .NET ecosystem: Mono's LLVM mode, the abandoned LLILC project, and the
experimental NativeAOT-LLVM branch for WebAssembly (see [01-prior-art.md](01-prior-art.md)).

**csharpc is a C# front-end for LLVM**: `csharpc hello.cs -o hello` should feel exactly like
`clang hello.c -o hello` or `rustc hello.rs`. The compiler is written in C#, uses Roslyn for parsing
and semantic analysis, and owns everything from Roslyn's lowered control-flow graph down to the
linked binary.

It has two ways in, sharing one compiler:

- **Standalone**, like rustc: `csharpc` on `.cs` files or a `.csproj`, no .NET SDK workflow required.
- **As .NET's AOT layer**, where NativeAOT sits today: `dotnet publish -p:PublishAot=true` hands the IL to
  csharpc instead of ILCompiler, with NativeAOT's MSBuild properties and outputs — a drop-in replacement
  ([ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)).

The compiler is C#. The runtime is C where C is the right tool (garbage collector, unwinding, threads, OS
boundaries) and C# for the library surface ([ADR 0017](adr/0017-runtime-tiers.md)).

## Why

- **C# as a systems language.** Modern C# already has `Span<T>`, `ref struct`, function pointers,
  `stackalloc`, `unsafe` and source generators. A compiler without a mandatory JIT or VM lets that
  subset reach places .NET does not go today: tiny static binaries, kernels and bootloaders,
  microcontrollers, plugins loaded by C hosts.
- **Every LLVM target for free.** Once the IR is right, AArch64, RISC-V, wasm32, PowerPC and
  embedded ARM need only a runtime port, not a new code generator.
- **Cross-language optimization.** LLVM bitcode from csharpc, clang and rustc can be linked and
  optimized together with LTO. A C# function can be inlined into a C caller and vice versa, without
  P/Invoke marshalling layers.
- **The LLVM tool ecosystem.** Sanitizers, `llvm-mca`, PGO, `perf`/DWARF symbolization, `opt` pass
  experimentation and coverage instrumentation all work on any language with a front-end.
- **Learning and research.** csharpc is a readable, end-to-end compiler for a mainstream language,
  in that language. Roslyn's `ControlFlowGraph` turns out to be a remarkably good mid-level IR (see
  [ADR 0003](adr/0003-roslyn-cfg-as-mir.md)), and this project is an experiment in how far it goes.

## Goals

1. **Faithful C# semantics.** A program's observable behavior must match .NET. The differential test
   suite ([05-testing.md](05-testing.md)) enforces this. When csharpc can't compile something
   correctly, it must refuse with a diagnostic (`CSLLVM0001`), never silently miscompile.
2. **A drop-in toolchain, long term.** Existing .NET projects should build with csharpc unchanged:
   `dotnet build`/`dotnet publish` integration (done), the BCL surface they use (a corelib written in
   C#, growing), and eventually the NuGet packages they depend on (an IL front-end, see the roadmap).
3. **Better than the original where it counts.** Smaller binaries, faster start-up, lower memory,
   competitive or better throughput, and stronger exploit mitigations by default — measured
   reproducibly against NativeAOT and the JIT, including the cases where csharpc loses.
4. **As much C# as possible.** The compiler is C#; the library surface moves from the C bootstrap
   runtime into a C# corelib compiled with each program; only the GC, unwinding, threads and the OS
   boundary stay in C.
5. **A complete, mature toolset for one target.** csharpc targets **.NET 11 (RC2 → GA) and C# 14**:
   a fixed language version, library surface and Roslyn release, so the work can converge on
   completeness and polish instead of tracking moving targets.
6. **Multi-platform evidence.** Linux x86-64 today; Linux ARM64, macOS and Windows next, each with
   its own CI job running the same differential suite against real .NET on that platform.

## Why it matters

csharpc is meant as a **demonstration for the .NET ecosystem**, not a competitor to it: a compiler
built from the official C# front-end plus LLVM, verified against .NET itself, that shows a different
point in the design space is viable — whole-program compilation, a library written in C#, LLVM's
optimizer and tooling. If the ideas are useful to the .NET team — to borrow, to build on, or to adopt
csharpc itself — the project has done its job. That is why rigor (differential testing, honest
benchmarks, documented trade-offs) matters more here than feature count.

## Non-goals

- **Tracking future .NET releases.** The scope is fixed to .NET 11 / C# 14 (see goal 5).
- **Unrestricted reflection, `Reflection.Emit` or dynamic loading of code.** These are incompatible
  with closed-world AOT, as they are under NativeAOT.
- **Reimplementing the C# language front-end.** Roslyn does parsing, binding, overload resolution,
  nullable analysis and lowering of language sugar. Duplicating that would be a multi-year detour
  ([ADR 0001](adr/0001-roslyn-as-frontend.md)).
- **A JIT or interpreter mode.** csharpc is an ahead-of-time compiler only.

## What success looks like

- **Done (M0/M1):** idiomatic C# — generics, interfaces, exceptions, closures, async, records, LINQ,
  collections — compiles and matches .NET; DWARF debug info; real threads; binaries 20–160× smaller
  than NativeAOT's with faster start-up; benchmark throughput ahead of the JIT and mostly ahead of
  NativeAOT ([benchmarks/RESULTS.md](../benchmarks/RESULTS.md)).
- **Next:** the same evidence on Linux ARM64, macOS and Windows; unmodified open-source C# tools
  compiling, with a published compatibility report.
- **Long term:** NuGet packages consumed through an IL front-end — a true drop-in — and csharpc
  compiling itself.

## Further reading

- [Why LLVM is a Game Changer for Compilers](https://www.youtube.com/watch?v=3WojCM9r0Ls) (The Coding
  Gopher, video): the front-end / LLVM IR / back-end split that csharpc builds on.
- [docs/01-prior-art.md](01-prior-art.md): clang, rustc, gollvm and the .NET-specific predecessors.
