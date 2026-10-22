# 01 — Prior art

csharpc borrows heavily, both from the established LLVM front-ends and from twenty years of attempts
to compile .NET ahead of time. This document lists what we take from each and why csharpc ends up
going *source → LLVM* instead of *IL → LLVM*.

## LLVM front-ends

### clang (C, C++, Objective-C)

clang's IR generation is split into **`CodeGenModule`**, which handles module-wide state (globals,
vtables, mangled names, deferred emission), and **`CodeGenFunction`**, which lowers one function
body from the AST.

**What we borrow**

- **The same split.** `ModuleCodeGen` corresponds to `CodeGenModule`
  (`src/Csharpc/CodeGen/ModuleCodeGen.cs`), and `FunctionCodeGen` to `CodeGenFunction`
  (`src/Csharpc/CodeGen/FunctionCodeGen*.cs`).
- **"alloca everything, let mem2reg clean up."** clang never builds phi nodes for locals. Every local
  and parameter gets an `alloca` in the entry block, and LLVM's `mem2reg`/`SROA` promote them to SSA
  registers. csharpc does exactly the same for locals, parameters and Roslyn flow captures, which
  keeps the lowering simple and makes `-O0` output easy to read
  (see [03-lowering.md](03-lowering.md#locals-parameters-and-mem2reg)).
- **Value type vs. memory type.** clang represents C `_Bool` as `i1` in registers and `i8` in
  memory. csharpc does the same for C# `bool` (`TypeMapper.Value` vs `TypeMapper.Storage`).
- **The driver delegates linking.** `clang` is a driver that invokes the system linker with the
  right crt objects and libraries. csharpc uses `clang` itself as its linker driver
  (`src/Csharpc/Driver/Toolchain.cs`).

### rustc (Rust)

rustc lowers through a series of IRs: AST → **HIR** (desugared, name-resolved) → **THIR** (typed) →
**MIR** (a control-flow graph of basic blocks with explicit temporaries). Only MIR reaches
`rustc_codegen_llvm`, which turns each MIR basic block into an LLVM basic block.

**What we borrow**

- **A CFG-shaped mid-level IR between the typed tree and LLVM.** csharpc doesn't have to build one:
  Roslyn's `Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph` already lowers loops, short-circuit
  operators, `?:`, `??`, `?.`, `switch` and patterns into basic blocks, and spills intermediate values
  into "flow captures", which play the role of MIR temporaries. See
  [ADR 0003](adr/0003-roslyn-cfg-as-mir.md).
- **A one-to-one block mapping.** As in `rustc_codegen_llvm`, each reachable CFG block becomes one
  LLVM block. The only extra blocks are runtime-check failure paths.
- **Mangling with length-prefixed identifiers.** Rust's v0 mangling (`_R…`) and the Itanium C++ ABI
  (`_Z…`) both length-prefix names, so arbitrary identifiers need no escaping. csharpc's scheme
  (`_CN…E`, `src/Csharpc/CodeGen/Mangler.cs`) follows them, so compiler-generated names like
  `<Main>$` mangle cleanly.
- **The std model.** Rust's `core`/`alloc`/`std` are ordinary Rust compiled by rustc. csharpc's
  long-term corelib plan copies this ([04-runtime.md](04-runtime.md)).

### gollvm (Go)

gollvm reuses **gofrontend**, the Go front-end shared with gccgo. gofrontend parses and type-checks
Go and talks to a code generator through an abstract **`Backend`** interface. gollvm implements that
interface (the "Bridge") on top of LLVM. Go's precise, moving-capable GC needs stack maps, so gollvm
uses LLVM's GC support (`gc "go"` strategy, statepoint-style stack maps) to tell the runtime where
pointers live.

**What we borrow**

- **Reuse the language's real front-end instead of rewriting it.** gollvm doesn't re-parse Go; it
  plugs into gofrontend. csharpc doesn't re-parse C#; it plugs into Roslyn, consuming
  `IOperation`/`ControlFlowGraph`, Roslyn's stable public "backend interface"
  ([ADR 0001](adr/0001-roslyn-as-frontend.md)).
- **The road to a precise GC.** gollvm is the existence proof that a managed, garbage-collected
  language can do precise collection on LLVM through statepoints and stack maps. It's our long-term
  plan in [ADR 0004](adr/0004-memory-management.md).

## The .NET ahead-of-time lineage

### Mono's LLVM backend

Mono's JIT and AOT compilers can hand methods to LLVM (`--llvm`, and full-AOT for iOS) instead of
Mono's own code generator. The input is Mono's IR, derived from IL. This shipped for years on iOS,
Android and WebAssembly. GC integration is conservative for stack frames.

*What we borrow:* evidence that .NET semantics (null checks, bounds checks, exceptions, virtual
dispatch) map onto LLVM well, and that conservative stack scanning is an acceptable first GC.

### LLILC (2015–2017)

LLILC ("lilac") was Microsoft's open-source IL → LLVM compiler for CoreCLR, both as a JIT and as an
AOT backend. It reached the point of running a good part of the CoreCLR test suite but was abandoned.
Getting the GC (precise stack maps, statepoints were immature then) and EH models to match CoreCLR's
runtime contracts exactly took most of the effort, and RyuJIT kept improving.

*What we learn:* the hardest part of putting .NET on LLVM isn't instruction selection. It's
**matching an existing runtime's GC and exception-handling ABI**. csharpc sidesteps that by owning its
runtime (csrt) and designing the ABI around what LLVM supports well.

### CoreRT → NativeAOT

CoreRT (now NativeAOT, shipped since .NET 7) compiles IL ahead of time with RyuJIT, plus a small
runtime written mostly in C# (`System.Private.CoreLib`) on top of a minimal native layer. It's
closed-world and does whole-program dependency analysis, with trimming and no JIT.

*What we borrow:*

- **Closed-world compilation** ([ADR 0006](adr/0006-closed-world-compilation.md)).
- **A corelib written in C#** over a thin native layer ([ADR 0007](adr/0007-runtime-in-c-first.md)).
- **The method-table/vtable object model.**

### NativeAOT-LLVM (dotnet/runtimelab)

An experimental branch of NativeAOT that swaps RyuJIT's code generator for LLVM to target
WebAssembly (and partly other targets). RyuJIT still does importing and its high-level optimizations,
and emits LLVM IR instead of machine code. WebAssembly can't scan its own stack, so GC references are
spilled to a **shadow stack** the GC can see.

*What we borrow:* the shadow stack as a portable fallback GC-root strategy (wasm32 in particular);
see [ADR 0004](adr/0004-memory-management.md).

### IL2CPP (Unity)

IL2CPP translates IL to C++ and compiles it with the platform's C++ compiler, often clang and thus
LLVM. It uses the Boehm GC (conservative, later with incremental marking) and implements exceptions
with C++ exceptions.

*What we borrow:* proof that a conservative collector and the platform's C++-style unwinding
are good enough for shipping very large C# codebases (games). Our M1 plan for both mirrors it.

### bflat

bflat is a single-binary C# compiler built from Roslyn + NativeAOT ("Go-like" tooling for C#). It
also offers `zerolib`, a minimal standard library for building tiny or freestanding binaries.

*What we borrow:* the user experience (`bflat build hello.cs` → native binary, no project file), and
zerolib as evidence that a small, purpose-built corelib can make C# usable without the full BCL.

## Why source-to-LLVM rather than IL-to-LLVM

Every .NET-on-LLVM project before csharpc consumed **IL**. csharpc consumes **Roslyn's bound
semantic model** instead. That is a deliberate trade-off:

| | IL → LLVM (LLILC, Mono-LLVM, NativeAOT-LLVM) | Source → LLVM (csharpc) |
|---|---|---|
| Library coverage | **Whole BCL and every NuGet package for free**: anything compiled to IL can be compiled | Only source we have; the BCL must be reimplemented or recompiled from source |
| Semantic information | Stack machine, erased local types and names, patterns lowered early | Full typed tree: symbols, nullability, constant values, original locals, syntax locations |
| Debug info | Must reconstruct from PDBs | Direct mapping from syntax nodes to DWARF |
| Desugaring | Done by Roslyn *and* frozen into IL (async/iterator state machines, closures, `foreach`) | Roslyn's CFG lowers control flow; closures, async and iterators are ours to lower (more work, more control) |
| Roundtrip cost | Source → IL → re-import into SSA | Source → CFG → SSA |
| Language coupling | Language-agnostic (F#, VB) | C# only |
| Runtime ABI | Usually must match CoreCLR/Mono's GC and EH contracts (what sank LLILC) | We own the runtime and its ABI |

The IL route optimizes for **compatibility**. The source route optimizes for **being a real LLVM
front-end**: rich types, precise diagnostics on unsupported code (`CSLLVM0001` with a source
location), freedom in object layout and ABI, and a pipeline that looks like clang's and rustc's.
The cost is that the BCL doesn't come for free, which is why the runtime and corelib story
([04-runtime.md](04-runtime.md)) is the critical path of the roadmap.

A hybrid stays possible later: an IL importer could feed the same `ModuleCodeGen` object model to
compile precompiled libraries.
