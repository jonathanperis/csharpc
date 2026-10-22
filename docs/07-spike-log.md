# 07 — Spike log

A dated journal of the work, written so the reasoning survives and not just the code.

## 2026-10-22 — Day one: from nothing to native C#

### Environment

- The machine had the .NET 10 SDK, so the **.NET 11 RC2 SDK** (`11.0.100-rc.2.26504.105`) was
  installed side by side with `dotnet-install.sh --channel 11.0 --quality preview` into
  `~/.dotnet11` and pinned in `global.json`.
- LLVM 18.1.3 (Ubuntu): `libLLVM-18.so`, `clang-18`, `opt`, `llc`, `ld.lld`. A `libLLVM-20.so` is
  also present; the binding resolver probes 18 → 19 → 20.
- Roslyn: `Microsoft.CodeAnalysis.CSharp` **5.9.0** from NuGet, with `LanguageVersion.Preview`.

### Bindings smoke test → exit code 42

Before writing the compiler, the hand-written `[LibraryImport]` bindings were tested end to end. The
test built a module with `define i32 @main() { ret i32 add (40, 2) }`, verified it, emitted an
object with a host `TargetMachine`, linked it with clang, ran it, and got exit status **42**. That
validated library resolution, string marshalling, `LLVMInitializeX86*` and object emission in one
step.

One finding along the way: `LLVMOpcode` values must be copied exactly from `llvm-c/Core.h`. The
first draft forgot `SExt = 32`, which shifted every later cast opcode by one, a silent
miscompilation waiting to happen.

### First crash: a segfault inside libLLVM

The first real compile crashed with a SIGSEGV in `llvm::BasicBlock::getModule()`. **Release libLLVM
has no assertions**, so C API misuse doesn't produce a diagnostic, only a crash. The technique that
found it:

1. `gdb -batch -ex run -ex bt --args dotnet csharpc.dll hello.cs` gave the native frame
   (`BasicBlock::getModule`), meaning an instruction was inserted with no valid insertion block.
2. `--dump-cfg` printed nothing, so the crash happened before any CFG was lowered.
3. A `CSHARPC_TRACE=1` per-symbol log narrowed it to the first method with parameters.

The cause: `FunctionCodeGen`'s constructor spilled parameters into their allocas through the main
builder *before* that builder had been positioned. The fix was to spill through the entry-block
builder. Lesson: keep `CSHARPC_TRACE` and consider a debug libLLVM for development.

### Hello, World and the Fib IR

`hello.cs` (top-level statements, interpolation, recursive local function) produced a 23 KB PIE
executable with correct output. At `-O2`, LLVM had promoted the allocas, inlined `Main` into `main`,
switched internal functions to `fastcc`, and rewritten `Fib` with **tail-recursion accumulator
elimination**. The `nsw` flags in that IR were inferred by LLVM from the `n >= 2` guard. csharpc
emits plain wrapping arithmetic, as C# requires. The clang approach (naive allocas, let the
optimizer work) was validated on day one.

Timing (`-v`) was dominated by Roslyn: about 1.4 s parse+bind on a cold process, about 200 ms
lowering, about 10 ms each for LLVM optimization and codegen, about 300 ms for the clang link.

### The test oracle

Instead of hand-writing expected outputs, every sample is executed by real .NET via
`dotnet run sample.cs` (file-based apps) and its stdout and exit code are recorded
([05-testing.md](05-testing.md)). Two problems with our own samples surfaced immediately: constant
conversions like `(short)40000` and `(int)1e20` are compile errors in C# (CS0221), and local
functions can't be overloaded (CS0128). Both were fixed by routing values through variables and
renaming.

### The Collatz bug: lvalue flow captures

`02_recursion` hung forever. The culprit was:

```csharp
n = n % 2 == 0 ? n / 2 : 3 * n + 1;
```

Roslyn's CFG evaluates the assignment **target first** (C# left-to-right order). Because the
right-hand side branches, the target `n` is spilled into a flow capture *before* the branch, and the
final block assigns `capture#0 = capture#1`. csharpc had captured `n`'s value, so the assignment
updated a temporary and `n` never changed.

The fix (`FindLvalueCaptures`): pre-scan each graph for captures used as assignment,
compound-assignment, increment or decrement targets, or as `ref`/`out` arguments. Those captures
hold an **address**. All other captures keep snapshotting values, which is required for correctness
when the variable changes before the capture is read. See
[02-architecture.md](02-architecture.md#flow-captures-and-lvalue-captures).

Lesson: Roslyn's CFG is a faithful *evaluation-order* model, and a capture can denote a location as
well as a value.

### "Infinity", not "∞"

csrt initially printed `1.0/0.0` as `∞`, matching .NET Core 3.0+ under ICU cultures. The oracle
said .NET 11 prints **`Infinity`** in our environment, which is the invariant culture's
`PositiveInfinitySymbol`. csharpc programs have no culture data, so the invariant culture is the
right model. Divergences like this are why the oracle has to be real .NET.

### `foreach` over an array goes through an enumerator

`--dump-cfg` showed that Roslyn's CFG does *not* lower `foreach` over an `int[]` to an index loop,
which is what `csc`'s emitter does in its internal lowering. It uses the general enumerator pattern:
`GetEnumerator()` captured, a `MoveNext()` branch, a `Current` read, and a **try/finally region**
whose finally conditionally calls `Dispose()`. So `foreach` depends on try/finally support. The plan
is to lower finally regions in general and to treat the array enumerator's
`GetEnumerator`/`MoveNext`/`Current`/`Dispose` as intrinsics over a stack-allocated `{array, index}`
pair that SROA turns back into an index loop.

### `dotnet run` writes build warnings to stdout

When recording `03_arithmetic`, warnings such as CS1718 ("comparison made to same variable", from
`double.NaN == double.NaN`) ended up at the top of `.expected`, because `dotnet run` prints build
output to stdout. The test runner (or the samples) must keep warnings out of the recorded output.

### Status at end of day

6 of 12 samples passed on the first full run (hello, recursion, strings, math, unhandled exception,
index out of range). The rest exposed exactly the features on the M1 list: try/finally (`foreach`),
primary constructors, tuple swaps, plus the `Infinity` and warnings issues above. Work then fanned
out in parallel: documentation, a conservative GC in csrt, a benchmark suite, and core codegen
features (finally, `foreach`, primary constructors, tuples).

Known issue at time of writing: a class with a static field initializer and no explicit static
constructor crashes the compiler. Roslyn reports an implicit static constructor with no syntax, and
`ModuleCodeGen.EmitMethod`'s fallback dereferences that null syntax.

(That crash was fixed shortly after: implicit static constructors are skipped, since static field
initializers run from `__csharpc_static_init`.)

## 2026-10-22, later the same day — from spike to M1 with parallel agents

The work was split into independent branches, each in its own git worktree, each owned by one agent
with a clear file-ownership boundary, and merged by an integrator (main thread) that kept the
differential suite green after every merge. Order of merges and what each taught us:

| Branch | What it added | Lesson |
|---|---|---|
| `gc` | conservative mark-sweep collector in csrt | 2 GB allocated in 112 MiB RSS; no libgc needed |
| `docs` | this documentation set | found the implicit-cctor crash by building examples |
| `bench` | 8 benchmarks-game programs + harness | csharpc already beat the JIT on compute-bound code |
| `lambdas` | closure conversion, delegates | `noalias` on `csrt_alloc` lets LLVM fold whole closures |
| `structs` | structs, `Nullable<T>`, tuples | narrow integer runtime params need `zeroext` (ABI bug only visible at -O0) |
| `msbuild` | `.csproj` input, `Csharpc.Sdk`, dotnet tool | evaluate projects with MSBuild itself (`-getItem`) |
| `generics` | monomorphization + interfaces | MT header grew 24→40 bytes; every hand-built MT had to follow |
| `conformance` | 195-program corpus | 41 % → the backlog became data-driven |
| `exceptions` | Itanium EH (invoke/landingpad + personality) | runtime-raised errors become catchable .NET exceptions |
| `async` | async/await, iterators, async iterators | LLVM coroutines (CoroSplit must run even at -O0) |
| `perf` | runtime LTO, allocator, devirtualization | default to `x86-64-vN` — `znver4` tuning cost up to 30 % |
| `lowlevel` | pointers, spans, ref locals, events | Roslyn models some unsafe constructs as `OperationKind.None` |

Integration bugs worth remembering:

- **Runtime LTO vs. exceptions.** Linking csrt as bitcode made clang's default `nounwind` on C
  functions visible to LLVM, which then turned `invoke`s of throwing helpers into `call`s and deleted
  the landing pads. Fix: build the runtime with `-fexceptions`. The fix first appeared not to work
  because the runtime cache key didn't include compiler flags — now it does.
- **Boxed struct method tables** were still built with the old 24-byte header after the interface
  map extension; struct `ToString` jumped into the interface-map pointer. Every hand-built method
  table now uses the shared header layout.
- **`with` on record structs** must copy: the struct-capture aliasing rule (a captured struct that is
  later mutated aliases the original) must not apply to the operand of a `with` expression.
- **Extension members (C# 14)**: Roslyn synthesizes static implementation methods that *share the
  member's syntax*; lowering them as ordinary methods binds the body against the wrong parameter.

End of day: 138/138 differential runs (-O0 and -O2), 5/5 project-build routes, conformance 70.3 %,
benchmarks ahead of NativeAOT on CPU time across the board.
