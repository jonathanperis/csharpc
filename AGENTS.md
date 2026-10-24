# AGENTS.md

Guidance for automated coding agents (and humans) working on csharpc — a C# front-end for LLVM that
compiles C# source to native executables (Roslyn front-end → Roslyn ControlFlowGraph → LLVM IR).
Human-oriented docs live in [README.md](README.md) and [docs/](docs/).

## Project goal and the one rule

csharpc aims to be a drop-in replacement for the .NET toolchain that beats it on speed, size and
security, with as much of the implementation as possible written in C#.

The standing goal is to be better than .NET in every area — drop-in compatible, faster and smaller —
and to prove it in CI (docs/06-roadmap.md, "Standing goal"); a size or speed regression is a bug.
The drop-in target is 100 % parity with .NET NativeAOT: what works under `PublishAot` must work with
csharpc; what NativeAOT can't do (run-time code generation) stays an explicit diagnostic. The JIT is the
test oracle and a benchmark baseline.

**The rule:** a program compiled by csharpc must behave exactly like the same program under real
.NET — byte-identical stdout and the same exit code. A silent difference (wrong output) is the worst
possible bug; an explicit "not supported by csharpc yet" diagnostic is always preferable to guessing.

## Setup

- Linux x86-64/ARM64, macOS ARM64 or Windows x64, .NET 11 SDK (version pinned in `global.json`;
  preview allowed), LLVM 18 (`libLLVM-18.so`, Homebrew `llvm@18` on macOS, LLVM-MinGW's
  `libLLVM-18.dll` on Windows, or set `CSHARPC_LIBLLVM`) and `clang` (linker driver, runtime
  compiler; on macOS Homebrew's clang 18, plus the Xcode Command Line Tools for the SDK and linker;
  on Windows LLVM-MinGW's clang, with the test scripts run from Git Bash).
- WebAssembly (`--target wasm32-wasip1`) also needs the WASI SDK 24 (`WASI_SDK_PATH`; its clang must be
  LLVM 18) and wasmtime on `PATH`.
- If the SDK is installed side by side: `export PATH=$HOME/.dotnet11:$PATH`.
- Recommended env: `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`.

## Build and test

```bash
dotnet build src/Csharpc -c Release              # build the compiler
dotnet src/Csharpc/bin/Release/net11.0/csharpc.dll app.cs -o app   # compile a program

tests/run.sh --all-opts        # differential samples at -O0 and -O2 (must stay 100 % green)
tests/conformance.sh -j 4      # C# conformance corpus (must stay 100 %)
tests/programs.sh              # larger real-world programs
tests/projects.sh              # .csproj input and dotnet build/run/publish integration
tests/il.sh                    # libraries consumed only as DLLs / NuGet packages (IL front-end)
tests/debuginfo.sh             # gdb-driven debug-info checks
tests/size.sh                  # binary-size budgets (a size regression is a failure)
benchmarks/run.sh              # csharpc vs .NET JIT vs NativeAOT (Markdown table)
CSHARPC_TEST_TARGET=wasm32-wasip1 tests/run.sh 40_try   # any suite, compiled for WebAssembly, run under wasmtime
```

Run the differential suite (at least the samples related to your change, ideally `--all-opts`)
before every commit that touches `src/`.

## Tests are recorded from real .NET

- Every test is a deterministic C# program plus `.expected` output (and `.exitcode` for non-zero
  exits, `.stdin` for input) recorded by running it under `dotnet run` — real .NET is the oracle.
- Add a test: write `tests/samples/NNN_name.cs`, then `tests/run.sh --regen NNN_name`, then
  `tests/run.sh --all-opts NNN_name`. Same pattern for `tests/conformance.sh --regen` and
  `tests/programs.sh --regen`.
- Never edit `.expected` files by hand. If csharpc's output differs, fix csharpc (or fix a
  non-deterministic test program), never the expectation.
- Test programs must be deterministic under real .NET too: no timing-, thread-scheduling-,
  culture- or hash-seed-dependent output, and no compiler warnings (they would end up in the
  recorded output).
- Every fixed miscompilation gets a minimal regression sample in `tests/samples/`.

## Repository layout

| Path | Contents |
|---|---|
| `src/Csharpc/Frontend` | Roslyn integration (parsing, binding, reference assemblies, project loading) |
| `src/Csharpc/CodeGen` | CFG → LLVM IR lowering; one `FunctionCodeGen.<Feature>.cs` partial per feature area |
| `src/Csharpc/CodeGen/Intrinsics.cs` | BCL members mapped directly to LLVM intrinsics / runtime calls |
| `src/Csharpc/corelib` | BCL types written in C# (`[Shadows("System.X")]` redirect, see docs/12-corelib.md) |
| `src/Csharpc/Llvm` | Hand-written LLVM C API bindings (`[LibraryImport]`) |
| `src/Csharpc/Driver` | CLI, pipeline, toolchain/linking, runtime LTO |
| `src/Csharpc.Sdk` | MSBuild integration (`UseCsharpc` / `PublishCsharpc`) |
| `src/runtime` | C runtime (csrt): GC, exceptions, threads, async scheduler, console, remaining helpers |
| `tests/` | samples, conformance corpus, real programs, project tests, debug-info tests |
| `benchmarks/` | benchmark programs, harness, `RESULTS.md` |
| `docs/` | architecture, lowering, runtime, roadmap, ADRs (`docs/adr/`) |
| `tools/` | debugger pretty-printers, checksec, fuzzing harnesses, helper scripts |

## Where to make a change

- WebAssembly-only lowering: `src/Csharpc/Wasm` (exception propagation and GC root spilling, whole-module
  IR passes; ADR 0011). C runtime differences are behind `__wasi__`/`__wasm__`, corelib ones behind
  `CSHARPC_TARGET_WASI`. Throwing helpers are `CSRT_THROWS` (they return on wasm), and offsets into runtime
  objects must follow the pointer size (wasm32 has 4-byte pointers).

- New library surface: prefer C# in `src/Csharpc/corelib`; use `Intrinsics.cs` only for things that
  must map to an LLVM intrinsic or the C runtime. Keep C in `src/runtime` for GC, unwinding,
  threading and OS boundaries.
- New language feature: a focused `FunctionCodeGen.<Feature>.cs` / `ModuleCodeGen.<Feature>.cs`
  partial with small hooks into the existing dispatch, rather than growing shared files.
- OS-facing library code (files, environment, native libraries): C# in `src/Csharpc/corelib/IO`
  calling libc through `[DllImport]` (see `corelib/IO/Posix.cs`, docs/16-interop-io.md), not new C.
  Intrinsics outrank shadows in `CoreLib.Map`, so remove an intrinsic when a shadow replaces it.
- Generic code: always read types through `S(...)` / `TypeOf(op)` so substitution and corelib
  redirection apply.
- Per-OS behaviour: C runtime differences go behind `#ifdef __APPLE__` / `#ifdef _WIN32`
  (docs/04-runtime.md, "Platforms"); libc constants, struct layouts and OS calls used by C# go behind
  `CSHARPC_TARGET_LINUX` / `CSHARPC_TARGET_MACOS` / `CSHARPC_TARGET_WINDOWS` in the corelib, chosen
  from the target triple, never the host. Windows reimplements the `Posix` class (`corelib/IO/Win32.cs`).
- Newlines: anything .NET writes as `Environment.NewLine` (WriteLine, AppendLine, multi-line exception
  messages) must use it (`ModuleCodeGen.NewLine`, `CSRT_NEWLINE` in C); it is `"\r\n"` on Windows.
- Shared runtime ABI (method-table header, object/string/array layout) is defined once in
  `src/runtime/csrt.h` and mirrored in `TypeMapper`/`ModuleCodeGen`; change all sides together.

## Debugging

- `--dump-cfg` prints Roslyn's control-flow graph per method; `--emit llvm-ir -O0` shows generated IR.
- `CSHARPC_TRACE=1` prints which symbol is being lowered (and stack traces of internal errors).
- The system libLLVM is a release build without assertions: crashes inside LLVM usually mean
  malformed IR — use `gdb` and `--emit llvm-ir`.
- `-g` produces DWARF; `tools/gdb/csharpc.py` pretty-prints strings and arrays.
- `--sanitize=address,undefined` builds the runtime with sanitizers; `tools/fuzz/run.sh` fuzzes the
  C runtime's input handling (libFuzzer), `tools/fuzz/corelib.sh` diff-fuzzes the C# corelib against .NET.
- The runtime is cached in `~/.cache/csharpc`, keyed on sources and flags; so is the corelib, compiled
  to `CsharpcCoreLib.dll` and lowered from IL (ADR 0015). `--corelib source` binds its C# sources in the
  program instead; `CSHARPC_CORELIB_SURVEY=1 --il-survey` lowers every corelib method once (what the IL
  front-end can't lower yet).

## Code style

- C# 14, file-scoped namespaces, nullable enabled; match the surrounding code's naming and density.
- Comments explain *why* (semantics, .NET compatibility, LLVM pitfalls), not *what*.
- Exact .NET semantics include exception types and messages, number formatting (invariant
  culture), and evaluation order.

## Commits and pull requests

- Imperative summary line; a body explaining what changed and why.
- Commit messages and PR descriptions contain no AI/tool attribution: no `Co-Authored-By` trailers
  for tools or models, no "generated by" footers, and no references to AI tooling in the repository.
- Do not commit build output (`bin/`, `obj/`), caches or local tool configuration.
- Keep `tests/run.sh --all-opts` and `tests/conformance.sh` green on every commit to `main`.
