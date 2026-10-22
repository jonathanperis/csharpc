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
2. **A real front-end shape.** The structure should be recognizably like clang/rustc: a
   module-level and a function-level code generator, a mangling scheme, an explicit runtime ABI, and
   the platform toolchain for linking.
3. **Native, VM-free output.** A small runtime (C now, C# later), no IL, no JIT, closed-world
   compilation ([ADR 0006](adr/0006-closed-world-compilation.md)).
4. **Self-hosting eventually.** csharpc should one day compile its own corelib and then itself
   (milestone M4 in [06-roadmap.md](06-roadmap.md)).

## Non-goals

- **Replacing CoreCLR or NativeAOT.** Those are production systems with decades of work behind them:
  precise GC, reflection, the full BCL, tiered compilation, diagnostics. csharpc aims at a different
  point in the design space and will not reach BCL compatibility for a long time, if ever.
- **Running arbitrary existing .NET assemblies.** csharpc compiles C# *source*. It doesn't consume
  IL from NuGet packages ([01-prior-art.md](01-prior-art.md#why-source-to-llvm-rather-than-il-to-llvm)
  discusses this trade-off).
- **Unrestricted reflection, `Reflection.Emit` or dynamic loading of code.** These are incompatible
  with closed-world AOT, as they are under NativeAOT.
- **Reimplementing the C# language front-end.** Roslyn does parsing, binding, overload resolution,
  nullable analysis and lowering of language sugar. Duplicating that would be a multi-year detour
  ([ADR 0001](adr/0001-roslyn-as-frontend.md)).

## What success looks like

- **Near term (M1):** classic benchmarks-game programs (n-body, binary-trees, fannkuch,
  spectral-norm) compile unchanged and perform in the same league as `clang -O2` and NativeAOT.
- **Medium term (M2/M3):** a meaningful subset of idiomatic C# (generics, interfaces, exceptions,
  lambdas, `List<T>`, `Dictionary<K,V>`, LINQ basics) compiles. Programs get DWARF debug info and can
  be cross-compiled, including to wasm32.
- **Long term (M4):** csharpc compiles itself.
