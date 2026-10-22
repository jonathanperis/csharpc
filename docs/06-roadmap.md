# 06 — Roadmap

Status legend for the matrix: ✅ supported · 🟡 partial · 🔜 planned (next milestone) · ⏳ later ·
❌ out of scope.

The matrix reflects the code at the time of writing. Several M1 items were being worked on in
parallel, so check the git log and [07-spike-log.md](07-spike-log.md) for newer state. **Update this
table when a feature lands.**

## Feature-support matrix

### Program structure

| Feature | Status | Notes |
|---|---|---|
| Top-level statements | ✅ | `Program.<Main>$`, including `args` and `return n` |
| `static void/int Main(string[]?)` | ✅ | |
| `async Main` | ⏳ | needs async lowering (M2) |
| Static methods, recursion, overloading | ✅ | |
| `ref`/`out`/`in` parameters | 🟡 | passed by address; `ref` locals and returns not yet |
| Optional and named arguments | ✅ | Roslyn fills in defaults; arguments are placed by ordinal |
| Local functions | 🟡 | non-capturing only |
| Lambdas, delegates, closures | 🔜 | M1: display-class closure conversion + delegate objects |
| Preprocessor (`#if CSHARPC`, `-D`) | ✅ | |

### Types

| Feature | Status | Notes |
|---|---|---|
| `bool char sbyte byte short ushort int uint long ulong nint nuint float double` | ✅ | |
| `decimal` | ⏳ | corelib in C# (M2) |
| Enums | 🟡 | arithmetic, comparisons, switch; no formatting (`ToString`) yet |
| `string` | 🟡 | literals, concat, interpolation, `Length`, indexer, `==`; few methods |
| Single-dimensional and jagged arrays | ✅ | bounds-checked |
| Multi-dimensional arrays | ⏳ | |
| Classes, inheritance, `abstract`/`virtual`/`override`/`sealed` | ✅ | |
| Constructors, field initializers, `base(...)`/`this(...)` | ✅ | |
| Primary constructors (classes) | 🔜 | |
| Static fields and static constructors | 🟡 | eager initialization before `Main` (deviation) |
| Properties (auto, computed, get-only), indexers on user types | ✅ | |
| Structs | 🔜 | M1 |
| Interfaces | ✅ | interface maps + `csrt_interface_slots`, devirtualized when the receiver type is known; DIMs, explicit impls |
| Generics | ✅ | monomorphization (classes, methods, local functions, constraints, generic math on primitives); no statics initializers in generic types yet |
| Records | ⏳ | after structs, interfaces and generics |
| Nullable value types (`int?`) | 🔜 | needs generics and structs |
| Tuples, deconstruction | 🔜 | |
| `object`, boxing/unboxing | ⏳ | |
| Pointers, `unsafe`, function pointers | ⏳ | mapped to `ptr`; codegen not yet |
| `Span<T>`, `ref struct` | ⏳ | |
| User-defined operators and conversions | 🟡 | lowered to direct calls; little tested |

### Statements and expressions

| Feature | Status | Notes |
|---|---|---|
| `if`, `while`, `do`, `for`, `break`, `continue`, `goto`, `return` | ✅ | via Roslyn CFG |
| `switch` statement/expression, `?:`, `??`, `?.`, `&&`, `||` | ✅ | via Roslyn CFG |
| Patterns: constant, relational, `and`/`or`/`not`, type, declaration, `var` | ✅ | |
| Property/positional/list patterns | ⏳ | |
| `foreach` over arrays | 🔜 | Roslyn lowers it to an enumerator inside try/finally; needs `finally` |
| `foreach` over `IEnumerable<T>` | 🟡 | user collections (pattern or interfaces) ✅; arrays as `IEnumerable<T>` and library collections ⏳ |
| `try`/`finally`, `using`, `lock` | 🔜 | M1 |
| `throw` of library exceptions | 🟡 | terminates the process like an unhandled exception |
| `try`/`catch`, user exception types, rethrow | 🔜 | M1, [ADR 0005](adr/0005-exceptions.md) |
| `checked` arithmetic | 🔜 | `llvm.*.with.overflow` |
| String interpolation | 🟡 | no alignment or format specifiers |
| Collection expressions (arrays) | ✅ | no spreads |
| Object and collection initializers | 🟡 | object initializers lowered by Roslyn's CFG; collections need corelib |
| `nameof`, `sizeof` of primitives, `const` | ✅ | constant-folded |
| `typeof`, reflection | ❌/⏳ | closed world; at most minimal `Type` objects |
| Iterators (`yield`) | ⏳ | own state-machine transform |
| `async`/`await` | ⏳ | own state-machine transform (M2) |
| `dynamic` | ❌ | |

### Semantics and runtime

| Feature | Status | Notes |
|---|---|---|
| Integer wraparound, shift masking | ✅ | |
| `DivideByZeroException`, `MinValue / -1` → `OverflowException` | ✅ | |
| Saturating float→int (.NET 9+) | ✅ | `llvm.fpto*i.sat` |
| Null checks → `NullReferenceException` | ✅ | explicit checks |
| Bounds checks → `IndexOutOfRangeException` | ✅ | |
| Invariant-culture number formatting | ✅ | shortest round-trip |
| Garbage collection | 🔜 | currently never frees ([ADR 0004](adr/0004-memory-management.md)) |
| Threads, `lock`, `volatile`, `Interlocked` | ⏳ | |
| Debug info (DWARF) | ⏳ | M3 |

### Library (BCL)

| Area | Status | Notes |
|---|---|---|
| `Console.Write/WriteLine` (primitives, string, object) | ✅ | format-string overloads not yet |
| `System.Math` | 🟡 | common functions; see [03-lowering.md](03-lowering.md#library-calls-intrinsics) |
| `string` methods | 🟡 | `Concat`, `IsNullOrEmpty`, `Equals`; not `Substring`, `Split`, `IndexOf`, … |
| `Environment` | 🟡 | `TickCount64`, `NewLine`, `Exit` |
| `List<T>`, `Dictionary<K,V>`, `StringBuilder`, LINQ | ⏳ | C# corelib (M2) |
| `int.Parse`, `ToString(format)` | ⏳ | C# corelib (M2) |
| Files, networking, threading APIs | ⏳/❌ | through a thin OS layer, case by case |

## Milestones

### M0 — Spike ✅ (2026-10-22)

- End-to-end pipeline: Roslyn → `ControlFlowGraph` → LLVM IR (C API via `[LibraryImport]`) →
  `default<On>` → object → clang link.
- Primitive types, control flow, patterns, strings, arrays, classes with vtables, static members,
  local functions, the main shim.
- Exact .NET integer/float/formatting semantics and runtime checks.
- csrt bootstrap runtime.
- Differential test suite against `dotnet run`.
- `--dump-cfg`, `--emit {exe,obj,asm,llvm-ir,llvm-bc}`, `-O0..3`, `--target`, `-v` timings.

### M1 — "Real programs"

Goal: idiomatic, non-library-heavy C# compiles. The benchmarks-game programs (n-body, binary-trees,
fannkuch-redux, spectral-norm, mandelbrot) compile unchanged.

- **try/finally** in the CFG lowering, by routing every exit of a try region through the finally
  block with a selector variable. This unlocks `foreach` over arrays (with a stack-allocated array
  enumerator that SROA dissolves into an index loop), `using` and `lock`.
- **Structs:** LLVM struct values, by-value copies, `this` as an address, field access through
  addresses, default initialization.
- **Generics by monomorphization:** one LLVM function and layout per instantiation (as rustc and
  C++ templates do), with generic arguments in the mangling. Shared generics over reference types
  are a possible later code-size optimization (NativeAOT's canonical forms).
- **Interfaces:** per-class interface tables and a dispatch scheme (a linear itable scan first, then
  NativeAOT-style dispatch cells or selector-indexed tables).
- **Exceptions** ([ADR 0005](adr/0005-exceptions.md)): `invoke`/`landingpad`, a personality
  routine in csrt, user exception classes, `catch` by type, filters, rethrow.
- **Lambdas, closures, delegates:** closure conversion to display classes, delegate objects as
  `{mt, target, fnptr}`.
- **Primary constructors, tuples (`ValueTuple` as structs), nullable value types, boxing.**
- **GC:** a conservative mark-sweep collector in csrt ([ADR 0004](adr/0004-memory-management.md)).
- **Benchmarks:** a harness comparing csharpc, `clang -O2` ports, CoreCLR (JIT) and NativeAOT.

### M2 — Self-hosted corelib

- A corelib in C#, compiled together with user code ([04-runtime.md](04-runtime.md)): `String`
  methods, number formatting and parsing, `StringBuilder`, `List<T>`, `Dictionary<K,V>`, `HashSet<T>`,
  `Span<T>`, a LINQ-to-objects subset, exceptions hierarchy. The `Intrinsics.cs` table shrinks.
- **Iterators and async.** Roslyn's public `IOperation`/`ControlFlowGraph` API exposes `await` and
  `yield` *un-lowered*. The state-machine rewrite that `csc` does happens in its internal lowering
  phase, not in the public API. csharpc must therefore do its own state-machine transform over the
  CFG (split at suspension points, spill live values into a frame object) or use LLVM coroutines
  (`llvm.coro.*`), which exist for exactly this transformation. Evaluating coroutines is part of M2.
- Lazy, .NET-precise static initialization.
- `checked` contexts everywhere, `decimal`.

### M3 — Toolchain quality

- **DWARF debug info** via the C API's `DIBuilder` (`llvm-c/DebugInfo.h`): line tables from syntax
  locations, local variables from allocas (`llvm.dbg.declare`), types from layouts. Debugging in gdb
  and lldb.
- **Cross-compilation:** `--target aarch64-linux-gnu`, macOS, Windows (COFF, MSVC or MinGW link), and
  a csrt port per OS.
- **wasm32** (WASI first): shadow-stack GC roots, wasm EH or setjmp-based exceptions.
- **LTO with C and Rust:** emit bitcode and link with `clang -flto` / `rustc -Clinker-plugin-lto`,
  plus a C# ⇄ C FFI story (`[DllImport]`/`[LibraryImport]`/function pointers → direct calls).
- **MSBuild/SDK integration:** `dotnet build` with `<UseCsharpc>true</UseCsharpc>` driving csharpc
  for `.csproj` projects (multiple files, references to source projects).
- Incremental and parallel codegen (per-type modules + LTO), compile-time work.

### M4 — Bootstrapping

csharpc compiles csharpc. That requires Roslyn itself to compile, which means the corelib must
cover what Roslyn uses: generics-heavy collections, LINQ, `System.Collections.Immutable`,
reflection-free paths. It's a long road, and the ultimate test of maturity.
