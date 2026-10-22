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
the time spent in each phase.

## Phases

### 1. Front-end: Roslyn (`src/Csharpc/Frontend/RoslynFrontend.cs`)

- Parses each input with `LanguageVersion.Preview` and defines `CSHARPC` (plus any `-D` symbols), so
  sources can `#if CSHARPC`.
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
4. Emits `__csharpc_static_init`, which runs all static field initializers and static constructors.
5. Emits the native `main` shim.

Errors are collected per symbol (`Guard`), so one unsupported construct doesn't hide the rest. All
are reported together with source locations.

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
  for `--target`, with PIC relocation (PIE executables). It emits an object file or assembly, or the
  module is printed (`--emit llvm-ir`) or written as bitcode (`--emit llvm-bc`).

### 5. Link (`src/Csharpc/Driver/Toolchain.cs`)

Like rustc and gollvm, csharpc doesn't drive `ld` directly. It runs
`clang --target=<triple> prog.o <runtime objects> -lm -o out`, and clang supplies the crt objects,
libc and platform flags.

The runtime (`runtime/*.c`, `*.h`) ships as **source** next to the compiler (copied into
`bin/.../runtime/` by the `.csproj`). It is compiled on first use with
`clang -c -O2 -std=c11 -fPIC --target=<triple>` and cached under
`${XDG_CACHE_HOME:-~/.cache}/csharpc/` as `<name>-<hash>.o`. The hash covers the runtime sources,
the triple and the C compiler path, so cross-targets and runtime edits get fresh objects. (The
first version handled only `csrt.c`; as the runtime grows, for example with a GC, every `.c` file
in `runtime/` is compiled.)

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
  match `runtime/csrt.h`. LLVM intrinsics (`llvm.sqrt.f64`, `llvm.fptosi.sat.i32.f64`, …) and libm
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
| exit code 70 | Internal compiler error: invalid LLVM IR was produced |
