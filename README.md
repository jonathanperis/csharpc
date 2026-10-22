# csharpc

**A C# front-end for LLVM.** csharpc compiles C# source code straight to LLVM IR and then to a native
executable. It is to C# what `clang` is to C, `rustc` to Rust and `gollvm` to Go.

```
hello.cs ──Roslyn──▶ IOperation / ControlFlowGraph ──csharpc──▶ LLVM IR ──opt/llc──▶ hello.o ──clang──▶ ./hello
```

csharpc is written in C# (.NET 11, C# 14/preview). It uses **Roslyn** as its parser and type checker
and the **LLVM C API**, called through `[LibraryImport]` P/Invoke, as its backend. Everything in between
is csharpc: lowering, object model, runtime and driver. There is no IL, no JIT and no CoreCLR at run
time. The output is an ordinary ELF executable linked against a ~300-line C runtime (`csrt`).

> **Status: spike (M0).** csharpc compiles a growing but still small subset of C#. Read
> [docs/06-roadmap.md](docs/06-roadmap.md) for what works today and what comes next. Don't use it for
> anything you care about yet.

## Quick start

Requirements (Linux x86-64 tested; AArch64 targets are initialized but untested):

- .NET 11 SDK (RC2 or later)
- LLVM 18 shared library (`libLLVM-18.so`; 19 and 20 are also probed) and `clang` (used as the linker
  driver and to build the runtime)

```bash
# .NET 11 RC2, installed side-by-side in ~/.dotnet11
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 11.0 --quality preview --install-dir ~/.dotnet11
export PATH=$HOME/.dotnet11:$PATH

# LLVM + clang (Ubuntu 24.04)
sudo apt install llvm-18 clang-18      # provides libLLVM-18.so and clang

# build csharpc
dotnet build src/Csharpc

# compile and run a program
cat > hello.cs <<'EOF'
Console.WriteLine("Hello from C# compiled by LLVM!");
for (int i = 0; i <= 10; i++)
    Console.WriteLine($"fib({i}) = {Fib(i)}");

static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
EOF
dotnet src/Csharpc/bin/Debug/net11.0/csharpc.dll hello.cs -o hello
./hello
```

If libLLVM isn't on the default library path, set `CSHARPC_LIBLLVM=/path/to/libLLVM.so`. To use a
different C compiler or linker driver, pass `--cc` or set `CSHARPC_CC`.

## Command line

```
csharpc [options] <file.cs>...

  -o, --output <path>    Output file
  --emit <kind>          exe (default) | obj | asm | llvm-ir | llvm-bc
  -O0 | -O1 | -O2 | -O3  Optimization level (default: -O2)
  --target <triple>      Target triple (default: host)
  -D <symbol>            Define a preprocessor symbol
  --no-implicit-usings   Don't add the SDK's implicit global usings
  --cc <path>            C compiler used as linker driver (default: $CSHARPC_CC or clang)
  --dump-cfg             Print Roslyn's control-flow graphs (csharpc's "MIR")
  --intrinsics           List the library members csharpc implements
  -W                     Show Roslyn warnings
  -v, --verbose          Print pipeline phases and timings
  --version              Print version information
  -h, --help             Show this help
```

`-v` prints the pipeline, which is a good way to see what csharpc does:

```
    1440.8 ms  parse + bind (Roslyn)
     215.5 ms  lower CFG -> LLVM IR
       1.1 ms  verify
       8.4 ms  optimize (default<O2>)
       8.5 ms  codegen (LLVM backend)
  $ clang --target=x86_64-pc-linux-gnu /tmp/csharpc-…-hello.o ~/.cache/csharpc/csrt-….o -lm -o hello
     317.5 ms  link
```

## What the output looks like

`static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);` at `-O2`
(`--emit llvm-ir`):

```llvm
define internal fastcc i32 @"_CN7Program7<Main>$3FibEi"(i32 %n) unnamed_addr #1 {
entry:
  %0 = icmp slt i32 %n, 2
  br i1 %0, label %bb4, label %bb3

bb3:                                              ; preds = %entry, %bb3
  %n.tr5 = phi i32 [ %3, %bb3 ], [ %n, %entry ]
  %accumulator.tr4 = phi i32 [ %4, %bb3 ], [ 0, %entry ]
  %1 = add nsw i32 %n.tr5, -1
  %2 = tail call fastcc i32 @"_CN7Program7<Main>$3FibEi"(i32 %1)
  %3 = add nsw i32 %n.tr5, -2
  %4 = add i32 %2, %accumulator.tr4
  %5 = icmp ult i32 %n.tr5, 4
  br i1 %5, label %bb4, label %bb3

bb4:
  %accumulator.tr.lcssa = phi i32 [ 0, %entry ], [ %4, %bb3 ]
  %n.tr.lcssa = phi i32 [ %n, %entry ], [ %3, %bb3 ]
  %accumulator.ret.tr = add i32 %n.tr.lcssa, %accumulator.tr.lcssa
  ret i32 %accumulator.ret.tr
}
```

csharpc emitted naive alloca/load/store code, and LLVM did the rest:

- **mem2reg** promoted the allocas to registers.
- **Tail-recursion elimination** turned one of the two recursive calls into a loop with an accumulator.
- Every function except `main` has internal linkage (closed-world compilation), so LLVM switched it to `fastcc`.
- The `nsw` flags were *inferred* by LLVM from the `n >= 2` range check. csharpc itself never emits `nsw`, because C# arithmetic wraps.

## Supported today

An overview; the authoritative matrix is in [docs/06-roadmap.md](docs/06-roadmap.md).

- **Program structure:** top-level statements or `static void/int Main([string[] args])`, static methods, recursion, non-capturing local functions
- **Types:** `bool`, `char`, all integer types, `float` and `double`, enums (as integers), `string`, one-dimensional (including jagged) arrays, classes
- **Statements and control flow:** `if`/`else`, `while`, `do`, `for`, `break`, `continue`, `goto`, `switch` statements and expressions, `?:`, `??`, `?.`, `&&`/`||`
- **Patterns:** constant, relational, `and`/`or`/`not`, type and declaration patterns
- **Integer semantics:** exact .NET behavior, including wraparound, shift-count masking, `DivideByZeroException`, `OverflowException` on `MinValue / -1`, and saturating float→int conversions
- **Strings:** concatenation, interpolation (without alignment or format specifiers), `Length`, the indexer, `==`
- **Classes:** fields, constructors, field initializers, static fields and static constructors, auto and computed properties, inheritance, `virtual`/`abstract`/`override`/`sealed`, `base` calls, `ToString()` overrides, `is`/`as`/casts
- **Arrays:** bounds checks, `Length`, collection expressions
- **Runtime errors:** null, index and division failures raise the .NET exception messages and exit with status 134, like an unhandled .NET exception
- **Library:** `Console.Write`/`WriteLine`, `System.Math`, and a few `string` and `Environment` members (`csharpc --intrinsics` prints the list)

**Not yet supported:** structs, generics, interfaces, `try`/`catch`/`finally`, lambdas and delegates,
closures, `foreach` over arrays (Roslyn lowers it to an enumerator inside try/finally), primary
constructors, tuples, `decimal`, nullable value types, boxing, async, LINQ, and garbage collection
(memory is never freed). csharpc reports each of these as `error CSLLVM0001` with a source location
instead of miscompiling.

## Tests

The test suite is **differential**: each sample in `tests/samples/` is first run on real .NET
(`dotnet run sample.cs`) to record the expected stdout and exit code. Then csharpc compiles and runs
it, and the results must match byte for byte.

```bash
tests/run.sh              # compile + run every sample with csharpc, compare with .NET's results
tests/run.sh 06           # only samples whose name contains "06"
tests/run.sh --regen      # re-record expected results from real .NET
CSHARPC_OPT=-O0 tests/run.sh
```

See [docs/05-testing.md](docs/05-testing.md).

## Documentation

| Doc | |
|---|---|
| [00 — Vision](docs/00-vision.md) | Why C# on LLVM; goals and non-goals |
| [01 — Prior art](docs/01-prior-art.md) | clang, rustc, gollvm, Mono-LLVM, LLILC, NativeAOT(-LLVM), IL2CPP, bflat |
| [02 — Architecture](docs/02-architecture.md) | The pipeline, phase by phase |
| [03 — Lowering](docs/03-lowering.md) | How each C# construct becomes LLVM IR |
| [04 — Runtime](docs/04-runtime.md) | csrt today; the self-hosted corelib plan |
| [05 — Testing](docs/05-testing.md) | Differential testing against .NET |
| [06 — Roadmap](docs/06-roadmap.md) | Milestones and the feature matrix |
| [07 — Spike log](docs/07-spike-log.md) | Journal of what happened and what we learned |
| [ADRs](docs/adr/) | Architecture decision records |

## Repository layout

```
src/Csharpc/
  Program.cs              entry point
  Driver/                 CLI options, pipeline orchestration, linking (Compiler.cs, Toolchain.cs)
  Frontend/               Roslyn compilation setup (RoslynFrontend.cs)
  CodeGen/                module/function lowering, type mapping, mangling, intrinsics
  Llvm/                   LLVM C API bindings (Native.cs) and thin wrappers
runtime/                  csrt.c / csrt.h — the bootstrap C runtime
tests/                    run.sh + samples/*.cs with recorded .NET results
docs/                     design documentation and ADRs
```
