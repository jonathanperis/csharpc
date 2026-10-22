<div align="center">

# csharpc

**A C# front-end for LLVM.**
csharpc compiles C# source code straight to LLVM IR and then to small, fast native executables —
what `clang` is to C, `rustc` to Rust and `gollvm` to Go.

[![CI](https://github.com/jonathanperis/csharpc/actions/workflows/ci.yml/badge.svg)](https://github.com/jonathanperis/csharpc/actions/workflows/ci.yml)
![.NET 11](https://img.shields.io/badge/.NET-11-512BD4)
![LLVM 18](https://img.shields.io/badge/LLVM-18-262D3A)
![C# 14](https://img.shields.io/badge/C%23-14-239120)
![license MIT](https://img.shields.io/badge/license-MIT-blue)

</div>

```
hello.cs ──Roslyn──▶ IOperation / ControlFlowGraph ──csharpc──▶ LLVM IR ──LLVM──▶ object ──clang──▶ ./hello
```

csharpc is written in C# (.NET 11). It uses **Roslyn** as its parser and type checker — so every
diagnostic, overload resolution and language rule is exactly the official compiler's — and the
**LLVM C API** as its backend. Everything in between is csharpc: lowering Roslyn's control-flow graph
to LLVM IR, the object model, a garbage-collected runtime, a C# corelib, and the driver. No IL, no JIT
and no CoreCLR at run time: the output is an ordinary native executable.

```console
$ csharpc hello.cs -o hello && ./hello
Hello from C# compiled by LLVM!
$ ls -lh hello
-rwxr-xr-x 1 dev dev 32K hello
```

## Highlights

- **Real C#, verified against real .NET.** Every test program runs under `dotnet run` (the oracle)
  and under csharpc, and the outputs must be byte-identical. **100 %** of the
  [195-program conformance corpus](docs/09-conformance.md) passes, with zero silent miscompilations.
- **Fast.** On [18 benchmark programs](benchmarks/RESULTS.md) csharpc beats the .NET JIT on every
  one and beats or ties NativeAOT on 17 (CPU time), with binaries **50× smaller** (26–82 KiB vs ~1.3 MiB).
- **Drop-in.** Compile a `.csproj` directly, or keep using `dotnet build` / `dotnet publish` with
  `-p:UseCsharpc=true` / `-p:PublishCsharpc=true` ([docs/08-drop-in.md](docs/08-drop-in.md)).
- **Debuggable.** `-g` emits DWARF: breakpoints on C# lines, C# names in backtraces, locals in gdb/lldb.
- **Whole-program optimization.** Closed-world compilation: internal linkage everywhere, class-hierarchy
  devirtualization, generics monomorphized, and the runtime itself LTO-linked into your program.

## Language and library support

| | |
|---|---|
| **Types** | classes, structs, records (class & struct), enums, interfaces (default & static abstract members), generics (monomorphized, constraints, variance, generic math), tuples, `Nullable<T>`, anonymous types, delegates & events, arrays (incl. multi-dimensional), `Span<T>`/`Memory<T>`, pointers & `unsafe`, `decimal`, `BigInteger` |
| **Code** | lambdas & closures, local functions, all statements, `switch` expressions, every pattern kind (type, property, positional, list, relational, logical), `try`/`catch`/`finally`/filters, `checked`, `using`, `lock`, iterators, `async`/`await`, `await foreach`, LINQ (method and query syntax), C# 14 extension members, primary constructors, `required`/`init`/`field` |
| **Runtime** | conservative garbage collector (thread-safe, stop-the-world), real threads (`Thread`, `Monitor`, `Interlocked`, `Parallel`), Itanium-ABI exceptions with .NET's exact messages, async scheduler on LLVM coroutines |
| **Library** | `Console`, `Math`, strings & `StringBuilder`, number formatting/parsing (all standard and custom format strings), `DateTime`/`TimeSpan`/`Stopwatch`, `Type` objects, `System.Collections.Generic` and `System.Linq` (a corelib written in C# and compiled with your program), tasks, cancellation |

Not supported, by design or for now (the same limits NativeAOT has in the first group):
`dynamic`, runtime code loading and member reflection; NuGet packages that ship only IL; file and
network I/O beyond the console. The full matrix is in [docs/06-roadmap.md](docs/06-roadmap.md).

## Quick start

Requirements: Linux x86-64, the .NET 11 SDK, LLVM 18 (`libLLVM-18.so`; 19/20 are also probed) and
`clang` (linker driver and runtime compiler).

```bash
# .NET 11 (side by side in ~/.dotnet11)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --quality preview --install-dir ~/.dotnet11
export PATH=$HOME/.dotnet11:$PATH

# LLVM 18 + clang (Ubuntu 24.04)
sudo apt install llvm-18 clang-18

# Build csharpc
git clone https://github.com/jonathanperis/csharpc && cd csharpc
dotnet build src/Csharpc -c Release
alias csharpc="dotnet $PWD/src/Csharpc/bin/Release/net11.0/csharpc.dll"

# Compile and run
cat > hello.cs <<'EOF'
var primes = Enumerable.Range(2, 50).Where(n => Enumerable.Range(2, (int)Math.Sqrt(n) - 1).All(d => n % d != 0));
Console.WriteLine($"Hello from C# compiled by LLVM! Primes: {string.Join(", ", primes)}");
EOF
csharpc hello.cs -o hello && ./hello
```

If libLLVM isn't on the default library path set `CSHARPC_LIBLLVM=/path/to/libLLVM.so`; to use another
C compiler or linker driver pass `--cc` or set `CSHARPC_CC`.

### Building real projects

```bash
csharpc path/to/App.csproj                 # evaluates the project with MSBuild; ProjectReferences included
dotnet build   -p:UseCsharpc=true          # with the Csharpc.Sdk package: native exe replaces the apphost
dotnet publish -p:PublishCsharpc=true      # one native executable, like PublishAot
dotnet pack src/Csharpc && dotnet tool install -g csharpc --add-source src/Csharpc/bin/Release   # as a dotnet tool
```

## Command line

```
csharpc [options] <file.cs>...
csharpc [options] [<project.csproj> | <directory>]

  -o, --output <path>     Output file, or directory for <AssemblyName>
  -c, --configuration <c> Debug (-O0 -g) | Release (-O2, default)
  -p:<Name>=<Value>       MSBuild property used when evaluating a project
  --emit <kind>           exe (default) | obj | asm | llvm-ir | llvm-bc
  -O0 | -O1 | -O2 | -O3   Optimization level (default -O2)
  -g, --debug             DWARF debug info (C# source-level debugging)
  --target <triple>       Target triple (default: host)
  --cpu <name>            native, x86-64-v3, znver4, ... (default: best x86-64-vN of the host)
  --dump-cfg              Print Roslyn's control-flow graphs (csharpc's "MIR")
  --intrinsics            List the library members implemented by the runtime
  -v, --verbose           Pipeline phases and timings
```

## How it works

1. **Front-end — Roslyn.** Parsing, binding, overload resolution and diagnostics come from the official
   C# compiler, bound against the real .NET reference assemblies.
2. **Middle — Roslyn's `ControlFlowGraph` as MIR.** Loops, `&&`/`||`, `?:`, `??`, `?.`, `switch` and
   patterns arrive already lowered to basic blocks, much like rustc's MIR. csharpc maps blocks 1:1 to
   LLVM blocks and lowers each operation, with entry-block `alloca`s that `mem2reg` promotes (the clang
   strategy). Generics are monomorphized; closures, iterators, `async` (LLVM coroutines), exceptions
   (`invoke`/`landingpad`), records and the rest are lowered by csharpc itself.
3. **Library — csrt + corelib.** A small C runtime (`src/runtime`: GC, exceptions, threads, formatting)
   plus a corelib written in C# (`src/Csharpc/corelib`: collections, LINQ) that library calls are
   redirected to, compiled together with your program.
4. **Back-end — LLVM.** The module, with the runtime's bitcode linked in, goes through LLVM's
   `default<O2>` pipeline and the target backend; `clang` links the executable.

Read [docs/02-architecture.md](docs/02-architecture.md) and [docs/03-lowering.md](docs/03-lowering.md)
for the details, and [docs/01-prior-art.md](docs/01-prior-art.md) for how this compares to Mono's LLVM
backend, LLILC, NativeAOT(-LLVM), IL2CPP and bflat.

## Repository layout

```
src/
  Csharpc/          the compiler: Frontend (Roslyn), CodeGen (CFG → LLVM IR), Llvm (C API bindings), Driver
  Csharpc/corelib/  BCL collections and LINQ written in C#, compiled with every program
  Csharpc.Sdk/      MSBuild integration (UseCsharpc / PublishCsharpc)
  runtime/          csrt — the C runtime: GC, exceptions, threads, async, strings, formatting, decimal, ...
tests/
  samples/          differential tests (each .cs has .expected output recorded from real .NET)
  conformance/      the 195-program C# conformance corpus
  projects/         multi-project solutions for the drop-in / MSBuild tests
  run.sh, conformance.sh, projects.sh, debuginfo.sh
benchmarks/         benchmark programs, harness (run.sh) and RESULTS.md
docs/               architecture, lowering, runtime, roadmap, ADRs, spike log
tools/              gdb and lldb pretty-printers for csharpc binaries
```

## Testing

```bash
tests/run.sh --all-opts     # differential suite at -O0 and -O2 (compares against recorded .NET output)
tests/run.sh --regen 42     # re-record expectations for samples matching "42" with real .NET
tests/conformance.sh -j 4   # conformance corpus, per-area coverage report
tests/projects.sh           # csproj input, dotnet build/run/publish integration
tests/debuginfo.sh          # gdb-driven debug-info checks
benchmarks/run.sh           # csharpc vs .NET JIT vs NativeAOT
```

See [docs/05-testing.md](docs/05-testing.md).

## Documentation

| | |
|---|---|
| [00 Vision](docs/00-vision.md) · [01 Prior art](docs/01-prior-art.md) · [02 Architecture](docs/02-architecture.md) · [03 Lowering](docs/03-lowering.md) | how csharpc is built and why |
| [04 Runtime](docs/04-runtime.md) · [10 Async](docs/10-async.md) · [11 BCL value types](docs/11-bcl-value-types.md) · [12 Corelib](docs/12-corelib.md) · [14 Threading](docs/14-threading.md) | the runtime and library |
| [05 Testing](docs/05-testing.md) · [09 Conformance](docs/09-conformance.md) · [13 Debugging](docs/13-debugging.md) · [08 Drop-in](docs/08-drop-in.md) | using and verifying it |
| [06 Roadmap](docs/06-roadmap.md) · [07 Spike log](docs/07-spike-log.md) · [ADRs](docs/adr/) | where it's going and how it got here |

## Status

csharpc went from an empty directory to the state above in its first day of development
([docs/07-spike-log.md](docs/07-spike-log.md)). It is young: expect gaps outside the tested surface,
and please open an issue with a minimal program when you find one — the differential test format
makes every bug report directly turnable into a regression test. Contributions are welcome; see
[docs/CONTRIBUTING.md](docs/CONTRIBUTING.md).

## License

[MIT](LICENSE)
