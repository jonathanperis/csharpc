# 02 — Architecture

## Pipeline

```
                         ┌──────────────────────── Roslyn (Microsoft.CodeAnalysis.CSharp 5.9) ───────────────┐
  *.cs ──────────────────▶ parse ──▶ bind / type-check ──▶ IOperation tree ──▶ ControlFlowGraph per method      │
  + implicit usings      │ (CSharpSyntaxTree)  (CSharpCompilation)        (ControlFlowGraph.Create)            │
                         └────────────────────────────────────────────────────────────┬────────────────────────┘
                                                                                      │  "MIR"
                         ┌──────────────────────── csharpc ─────────────────────────────▼──────────────────────┐
                         │ ModuleCodeGen: types, layouts, method tables, statics, entry shim                  │
                         │ FunctionCodeGen: CFG blocks → LLVM blocks, expressions → instructions              │
                         └────────────────────────────────────────────────────────────┬────────────────────────┘
                                                                                      │  LLVM IR (in memory, C API)
                         ┌──────────────────────── LLVM 18 (libLLVM via LibraryImport) ─▼──────────────────────┐
                         │ verify ──▶ new-PM pipeline default<On> ──▶ TargetMachine: object / asm / .ll / .bc │
                         └────────────────────────────────────────────────────────────┬────────────────────────┘
                                                                                      │  prog.o
                         ┌──────────────────────── system toolchain ────────────────────▼──────────────────────┐
                         │ clang (linker driver): prog.o + csrt-<hash>.o + -lm  ──▶  native executable         │
                         └─────────────────────────────────────────────────────────────────────────────────────┘
```

The whole pipeline lives in `src/Csharpc/Driver/Compiler.cs` (`Compiler.Run`). With `-v` it prints
the time spent in each phase (wall, CPU, allocations, peak RSS).

csharpc itself is ReadyToRun-compiled in Release builds, and a compilation uses several threads
(`-j N`, `CSHARPC_THREADS`, default: the processor count) for parsing, binding, Roslyn's CFGs ahead
of lowering and LLVM code generation; the output never depends on the thread count
([ADR 0014](adr/0014-compile-time.md)).

## Phases

### 1. Front-end: Roslyn (`src/Csharpc/Frontend/RoslynFrontend.cs`)

- Parses each input with `LanguageVersion.Preview` and defines `CSHARPC` (plus any `-D` symbols), so
  sources can `#if CSHARPC`. It also defines the symbols the SDK defines for a net11.0 build:
  `TRACE`, `DEBUG`/`RELEASE` per configuration, `NET`, `NET11_0`, `NETCOREAPP`,
  `NET5_0_OR_GREATER` … `NET11_0_OR_GREATER` and `NETCOREAPP1_0_OR_GREATER` …
  `NETCOREAPP3_1_OR_GREATER`. For a project, the evaluation runs the SDK's
  `AddImplicitDefineConstants` target, so `DefineConstants` is exactly what `csc` would receive.
- Adds a synthetic `<implicit-usings>.g.cs` with the same `global using` set the .NET SDK injects
  for console apps, unless `--no-implicit-usings` is given.
- Creates a `CSharpCompilation` (`OutputKind.ConsoleApplication`, nullable enabled, unsafe allowed).
- **References:** the `System.*`, `netstandard` and `mscorlib` assemblies from the
  `TRUSTED_PLATFORM_ASSEMBLIES` of the runtime csharpc itself runs on (.NET 11). This metadata only
  drives binding and overload resolution. None of its IL is ever compiled: when user code calls
  `Console.WriteLine(int)`, Roslyn resolves the exact overload, and csharpc supplies the
  implementation (see [intrinsics](03-lowering.md#library-calls-intrinsics)).
- Roslyn errors stop compilation and are printed in Roslyn's normal format. Warnings are shown with
  `-W`.
- Files are parsed in parallel and the program's trees are bound for diagnostics in parallel; the
  corelib's method bodies are only bound when lowering reaches them.
- **Assemblies without sources** (`-r X.dll`, project `<Reference>` items, `PackageReference`s) are
  added as metadata references too; their code comes from the IL front-end (next section).

### 1b. IL front-end (`src/Csharpc/Frontend/Il/`, `CodeGen/FunctionCodeGen.Il*.cs`)

The second source of method bodies ([ADR 0010](adr/0010-il-frontend.md)). Roslyn already provides
symbols for a referenced DLL's types and members; codegen imports them with
`MetadataImportOptions.All` (private fields included) and treats them like source types: one layout,
one method table, one ABI for source and IL code, so a C# class can derive from a class compiled
from IL and call it directly.

```
  IL assembly ──Roslyn (metadata reference)──▶ PE symbols ──▶ ModuleCodeGen: layouts, vtables, statics (as for source)
       │                                                         │ GetFunction(IL method) queues it on demand
       └──System.Reflection.Metadata──▶ IlMethodBody ──▶ FunctionCodeGen.EmitIl: blocks + simulated stack
                                         (tokens → Roslyn symbols)   │  arithmetic, branches, locals, EH: LLVM directly
                                                                     └─ calls, newobj, library members: synthetic IOperations
                                                                        → the same EmitValue as source code
```

- `IlAssemblySet` / `IlAssembly` load the PE images (ReadyToRun framework images too), map
  `ISymbol.MetadataToken` back to method bodies, and resolve the tokens in a body to Roslyn symbols
  (`TypeRef`/`MemberRef` by name and decoded signature, `TypeSpec`/`MethodSpec` through an
  `ISignatureTypeProvider`) in the generic *definition's* context, so `TypeSubstitution`
  monomorphizes IL like source.
- `FunctionCodeGen.EmitIl` splits the body into basic blocks at branch targets and region
  boundaries, keeps a compile-time evaluation stack of `IlEntry` values (int32/int64/native int/F/
  object/managed pointer/value), and spills it into per-block allocas at block boundaries.
  Instructions with IL-specific semantics are lowered directly; calls, object creation, library
  fields and properties, delegates and array elements become synthetic operations
  (`CodeGen/IlOperations.cs`) for the source path's `EmitValue`, which is how IL call sites reach
  the same intrinsics, corelib shadows and dispatch as C# call sites.
- Exception regions use the source path's model: one landing pad per protected range, a dispatch
  chain over its handlers (catch type tests, filters), `leave` through per-finally continuation
  selectors.
- Only reachable methods are lowered. A method that cannot be compiled becomes a stub that ends the
  program with the diagnostic if called; a class-hierarchy analysis from the program's code turns
  the failures it can reach into errors and the rest into `CSLLVM0007` warnings.
- `--dump-il` prints the decoded IL of every method lowered from IL.

### 2. Lowering: ModuleCodeGen (`src/Csharpc/CodeGen/ModuleCodeGen.cs`)

`ModuleCodeGen.Generate()`:

1. Finds the entry point (`Compilation.GetEntryPoint`; `CSLLVM0003` if missing) and collects all
   source types, recursively through namespaces and nested types.
2. For every class, computes a `ClassLayout` (`TypeMapper.cs`): an LLVM named struct
   `class.<Name>`, a field index map, the instance size from the target data layout, and the vtable
   slot assignment. Then it emits the **method table** global. Structs, interfaces, delegates,
   generic types and records are rejected with `CSLLVM0001`.
3. Lowers every method body of every class (ordinary methods, constructors, static constructors,
   accessors, operators, conversions), then drains a queue of **local functions** discovered while
   lowering.
4. Emits `__csharpc_static_init`, which runs the static initializers whose timing is unobservable
   (constant-only initializers, corelib types). Other types get a lazy initialization check with
   .NET's triggers ([03-lowering.md](03-lowering.md#static-initialization)).
5. Emits the native `main` shim.

Errors are collected per symbol (`Guard`), so one unsupported construct doesn't hide the rest. All
are reported together with source locations.

Roslyn's CFG of a body is built once per syntax node (`ModuleCodeGen.Cfgs.cs`), and the CFGs of
methods queued for lowering (corelib, generic instantiations) are built ahead on background threads
while the lowering thread works through the queue ([ADR 0014](adr/0014-compile-time.md)).

`CSHARPC_TRACE=1` logs each symbol as it is lowered, which helps when libLLVM crashes.

### 3. Lowering: FunctionCodeGen (`src/Csharpc/CodeGen/FunctionCodeGen*.cs`)

One instance per LLVM function. It takes Roslyn `ControlFlowGraph`s and produces LLVM basic blocks:

- `FunctionCodeGen.cs` holds the CFG walk: blocks, terminators, returns, captures, regions,
  storage helpers and runtime checks.
- `FunctionCodeGen.Expressions.cs` holds constants, lvalues, assignments, operators and conversions.
- `FunctionCodeGen.Calls.cs` holds calls, virtual dispatch, objects, properties, arrays, strings,
  `throw` and patterns.
- `Intrinsics.cs` maps BCL members to LLVM intrinsics, libm and csrt calls.

The details are in [03-lowering.md](03-lowering.md).

### 4. LLVM

- `LlvmModule.Verify` runs the IR verifier. Invalid IR is always a csharpc bug: the module is written
  to `<output>.invalid.ll` and csharpc exits with code 70 ("internal compiler error").
- `LlvmModule.RunPasses("default<O{n}>")` runs the new pass manager's standard pipeline, the same
  as `opt -O2` or `clang -O2`. `-O0` skips it entirely.
- `TargetMachine` (`src/Csharpc/Llvm/Llvm.cs`) is created for the host triple, CPU and features, or
  for `--target`, with PIC relocation (PIE executables; static on wasm). It emits an object file or
  assembly, or the module is printed (`--emit llvm-ir`) or written as bitcode (`--emit llvm-bc`).
- **Parallel code generation** (`Driver/ParallelCodegen.cs`, [ADR 0014](adr/0014-compile-time.md)):
  an executable whose optimized module has more than ~20,000 instructions is emitted as up to 8
  objects, one partition of contiguous functions each, on several threads, every partition in its own
  LLVM context with its own target machine. The other partitions' functions are declarations there;
  partition 0 keeps the global variables. Optimization still sees the whole program; not with `-g` or
  on wasm32.
- **wasm32 only** (`src/Csharpc/Wasm`, [ADR 0011](adr/0011-wasm32.md)). `ExceptionLowering` runs
  before the optimization pipeline: there is no unwinder, so after every call that may throw, a test
  of `csrt_pending_exception` branches to the landing-pad code or returns to the caller.
  `RootSpilling` runs after it: values that may hold references and are live across a call that
  may collect get a linear-memory stack slot, so the conservative collector sees them. Each pass is
  followed by the IR verifier.

### 5. Link (`src/Csharpc/Driver/Toolchain.cs`)

Like rustc and gollvm, csharpc doesn't drive `ld` directly. It runs
`clang --target=<triple> prog.o <runtime objects> -lm -o out`, and clang supplies the crt objects,
libc and platform flags.

Per platform:

- **Linux** (ELF, glibc): lld when available, `--gc-sections`, `--icf=safe`, packed RELR relocations,
  `-s`, and RELRO/BIND_NOW/noexecstack/separate-code for hardening (on AArch64 separate code at 4 KiB
  rather than lld's 64 KiB padding, see [17-security.md](17-security.md)); `--static` links `-static-pie`.
- **macOS** (Mach-O, arm64 only): the default host triple is `arm64-apple-macosx<deployment target>`
  (`$MACOSX_DEPLOYMENT_TARGET`, else 14.0) rather than LLVM's `arm64-apple-darwin<kernel>`. The SDK
  comes from `$SDKROOT` or `xcrun --show-sdk-path` and is passed as `-isysroot` to every clang call.
  Apple's ld links (`CSHARPC_LD=lld` selects ld64.lld, e.g. to cross-link from Linux with an SDK) with
  `-dead_strip` and `-S -x` instead of `-s`. P/Invoke targets that the program binds directly are weak
  imports; Apple's ld wants those listed (`-U _name`) so a missing one still binds to null. With `-g`
  the DWARF stays in the object files on macOS, so the driver runs `dsymutil` to produce
  `<output>.dSYM`. `--static` and x86-64 macOS are rejected with an explicit error.
- **Windows** (PE/COFF, x86-64 only): csharpc targets the MinGW-w64 environment,
  `x86_64-w64-windows-gnu` (an LLVM built for MSVC reports `*-pc-windows-msvc` as its default triple;
  the host triple is rewritten to the MinGW one, and an explicit `-msvc` target is rejected). The
  reason is exceptions: csharpc lowers `try` to Itanium-style `invoke`/`landingpad` with its own
  personality, and LLVM supports landing pads on Windows only in the MinGW environment, where they
  ride on SEH unwind tables (`.pdata`/`.xdata`); the MSVC environment would need funclet-based EH
  (`catchswitch`/`cleanuppad`) throughout codegen. The toolchain is LLVM-MinGW (clang, lld,
  compiler-rt, libunwind, UCRT); clang links with `-fuse-ld=lld -rtlib=compiler-rt
  -unwindlib=libunwind -static --gc-sections --icf=safe -s`. `-static` only concerns the MinGW
  libraries (libunwind): the executable depends on system DLLs only (kernel32 and the UCRT, part of
  Windows 10+). winpthreads is not linked: the runtime implements the pthread subset it uses over SRW
  locks and condition variables (`src/runtime/csrt_pthread.h`, `win32.c`), and it is compiled with
  `__USE_MINGW_ANSI_STDIO=0` so `printf` is the UCRT's rather than MinGW's C99 copy; together these
  were ~65 KB of a 104 KB "Hello, World!". An output path without an extension gets `.exe`. The compiler itself
  loads LLVM-MinGW's `libLLVM-18.dll` (or an `LLVM-C.dll`; `CSHARPC_LIBLLVM` overrides), and the asm
  parser is initialized because the runtime's bitcode carries inline asm from the MinGW headers.
- **WebAssembly** (`--target wasm32-wasip1`, WASI preview 1): the WASI SDK's clang
  (`$WASI_SDK_PATH`, LLVM 18) compiles the runtime and drives wasm-ld against wasi-libc and
  compiler-rt: `--gc-sections`, `--stack-first` with an 8 MiB stack (an overflow traps at address 0
  instead of corrupting static data), `--strip-all`. The output is a `.wasm` module for wasmtime or
  another WASI runtime. The runtime is always linked into the program as bitcode, at every
  optimization level, because two whole-program IR passes run on wasm (below). `main` is exported as
  `__main_argc_argv`, the name wasi-libc's start code calls. The design is in
  [ADR 0011](adr/0011-wasm32.md).
- The corelib is parsed with `CSHARPC_TARGET_LINUX`, `CSHARPC_TARGET_MACOS`, `CSHARPC_TARGET_WINDOWS`
  or `CSHARPC_TARGET_WASI` (from the target triple) so its libc constants, struct layouts and OS
  calls match the target ([16-interop-io.md](16-interop-io.md)).

The runtime (`src/runtime/*.c`, `*.h`) ships as **source** next to the compiler (copied into
`bin/.../runtime/` by the `.csproj`). It is compiled on first use with
`clang -c -O2 -std=c11 -fPIC --target=<triple>` and cached under
`${XDG_CACHE_HOME:-~/.cache}/csharpc/` as `<name>-<hash>.o`. The hash covers the runtime sources,
the triple and the C compiler path, so cross-targets and runtime edits get fresh objects. (The
first version handled only `csrt.c`; as the runtime grows, for example with a GC, every `.c` file
in `src/runtime/` is compiled.)

## Roslyn's ControlFlowGraph is our MIR

Roslyn exposes, as a stable public API, a lowered control-flow graph of every method body
(`ControlFlowGraph.Create(syntax, semanticModel)`). Each `BasicBlock` has:

- `Operations`: straight-line `IOperation`s (assignments, calls, captures)
- `BranchValue` + `ConditionKind` (`WhenTrue`/`WhenFalse`) + `ConditionalSuccessor`
- `FallThroughSuccessor` with `Semantics` (`Regular`, `Return`, `Throw`, `Rethrow`,
  `StructuredExceptionHandling`, …)
- `ControlFlowRegion`s for locals' lifetimes and try/catch/finally

That is the same shape as rustc's MIR. Roslyn has already lowered `for`/`while`/`do`, `&&`/`||`,
`?:`, `??`, `?.`, `switch` statements and expressions, `goto`, `is` patterns in branch positions,
and object/collection initializers. So csharpc's control-flow lowering is mostly 1:1. You can see
the graph with `--dump-cfg`:

```
=== CFG Collatz(long)  [_CN7Program7<Main>$7CollatzEl]
  B0 (Entry)
    next -> B1 (Regular)
  B1 (Block)
      SimpleAssignment         steps = 0
    next -> B2 (Regular)
  B2 (Block)
    branch-value: Binary n != 1  [WhenFalse]
    if WhenFalse -> B8 (Regular)
    next -> B3 (Regular)
  B3 (Block)
      FlowCapture              n
    branch-value: Binary n % 2 == 0  [WhenFalse]
    if WhenFalse -> B5 (Regular)
    next -> B4 (Regular)
  B4 (Block)
      FlowCapture              n / 2
    next -> B6 (Regular)
  B5 (Block)
      FlowCapture              3 * n + 1
    next -> B6 (Regular)
  B6 (Block)
      ExpressionStatement      n = n % 2 == 0 ? n / 2 : 3 * n + 1;
    next -> B7 (Regular)
  B7 (Block)
      ExpressionStatement      steps++;
    next -> B2 (Regular)
  B8 (Block)
    branch-value: LocalReference steps  [None]
    next -> B9 (Return)
  B9 (Exit)
```

The rationale and limitations are in [ADR 0003](adr/0003-roslyn-cfg-as-mir.md).

### Lowering the CFG

`FunctionCodeGen.LowerCfg`:

1. Enqueues local functions found in the graph (`cfg.LocalFunctions`).
2. Scans regions. Try/catch/finally/filter regions are rejected for now, and locals declared in
   regions are recorded (a local *not* declared in the current graph means a closure capture, which
   is also rejected).
3. Pre-scans for **lvalue captures** (below).
4. Creates one LLVM block `bb<ordinal>` per reachable CFG block and branches to the entry's
   successor.
5. For each block, emits its operations and then the terminator:
   - conditional: `br i1 %cond, %whenTrue, %whenFalse`, swapped for `WhenFalse`
   - `Regular` to the exit block: `ret void`, or a branch to the continuation for prologue graphs
   - `Return`: `ret <BranchValue>`, converted to the method's return type
   - `Throw`: a call to `csrt_throw` followed by `unreachable`

The same machinery chains several graphs into one function. Field initializers (each its own CFG
via `ControlFlowGraph.Create(EqualsValueClauseSyntax)`) are lowered as a **prologue** before the
constructor body. Instead of returning, they branch to a continuation block. The static initializer
works the same way.

### Flow captures and lvalue captures

When a value must survive across blocks, Roslyn spills it into a *flow capture*:
`IFlowCaptureOperation(id, value)` defines it and `IFlowCaptureReferenceOperation(id)` reads it.
csharpc gives each capture an entry-block `alloca`. Because a capture may be assigned in several
predecessor blocks (both arms of `?:`), the alloca-plus-mem2reg strategy builds the phi nodes for us.

**The Collatz bug.** In the graph above, block B3 captures `n` *itself*, not its value, before
evaluating the branches. Then B6 assigns `capture#0 = capture#1`. Roslyn does this to preserve C#
left-to-right evaluation order: the assignment target is evaluated before the right-hand side.
The first implementation stored `n`'s *value* into a temporary and assigned into that temporary,
so `n` never changed and the loop never terminated.

The fix (`FunctionCodeGen.FindLvalueCaptures`): before lowering, walk every operation of the graph
and collect the capture ids that appear as the target of an assignment, compound assignment or
`++`/`--`, or as a `ref`/`out` argument. Those captures store an **address**
(`AddressOf(value)`), not a value. Reads and writes through them go to the original location.
Every other capture still snapshots the value, which is required when the original variable changes
before the capture is read.

## Module-level conventions

- **Closed world, internal linkage.** csharpc compiles the whole program at once, so every
  function, method table and static is `internal` except `main` (`ModuleCodeGen.GetFunction`). This
  lets LLVM change calling conventions (`fastcc`), inline across the program, and delete unused
  code. See [ADR 0006](adr/0006-closed-world-compilation.md).
- **`nounwind` everywhere.** Nothing unwinds yet ([ADR 0005](adr/0005-exceptions.md)), so all
  functions get `nounwind`. csrt's `csrt_throw*` helpers are declared `noreturn cold`.
- **Runtime functions are declared lazily** (`ModuleCodeGen.Runtime`) with the signatures that
  match `src/runtime/csrt.h`. LLVM intrinsics (`llvm.sqrt.f64`, `llvm.fptosi.sat.i32.f64`, …) and libm
  functions are declared the same way.

## LLVM bindings (`src/Csharpc/Llvm/`)

- `Native.cs`: hand-written `[LibraryImport]` declarations for the subset of the LLVM C API we use
  (`llvm-c/Core.h`, `Target.h`, `TargetMachine.h`, `Transforms/PassBuilder.h`, `Analysis.h`,
  `BitWriter.h`). Handles are blittable `readonly record struct`s, so arrays of them marshal by
  pinning. A `DllImportResolver` probes `$CSHARPC_LIBLLVM`, then LLVM 18, 19 and 20 sonames.
  Rationale in [ADR 0002](adr/0002-llvm-c-api-via-libraryimport.md).
- `Llvm.cs`: `LlvmContext`, `LlvmModule`, `TargetMachine`, plus C# 14 `extension` members on the raw
  handles (`type.IsInteger`, `value.Param(i)`, `block.HasTerminator`).
- `IrBuilder.cs`: a thin wrapper over `LLVMBuilderRef` that mirrors `llvm::IRBuilder` naming.

Release builds of libLLVM have no assertions. Misuse of the C API (building into an unpositioned
builder, for example) shows up as a segfault inside libLLVM. Debug it with
`gdb -batch -ex run -ex bt --args dotnet csharpc.dll …` plus `CSHARPC_TRACE=1`
([07-spike-log.md](07-spike-log.md)).

## Diagnostics

| Id | Meaning |
|---|---|
| Roslyn `CSxxxx` | C# errors, exactly as `csc` reports them |
| `CSLLVM0001` | Valid C#, but the construct isn't supported by csharpc yet |
| `CSLLVM0002` | A library member with no csharpc implementation (`'System.Foo.Bar(int)' has no implementation in the csharpc runtime yet`) |
| `CSLLVM0003` | No entry point |
| `CSLLVM0006` (warning) | A known run-time difference from .NET that can't be avoided yet. Today: turning an exception into text or reading `StackTrace`: the trace lists the program's own frames exactly like .NET, but not frames inside .NET's library ([ADR 0012](adr/0012-stack-traces.md)). IL: `tail.` calls compiled as ordinary calls |
| `CSLLVM0007` (warning) | A method of an assembly compiled from IL could not be compiled, but the program cannot reach it (class-hierarchy analysis): it was compiled as a stub that prints the diagnostic and aborts if it is ever called ([ADR 0010](adr/0010-il-frontend.md)) |
| exit code 70 | Internal compiler error: invalid LLVM IR was produced |
