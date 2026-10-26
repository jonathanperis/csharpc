# Contributing to csharpc

Thanks for helping! (Automated coding agents: see [AGENTS.md](../AGENTS.md).) csharpc is a C# front-end for LLVM; the bar for every change is simple:
**the output of a compiled program must be byte-identical to real .NET.**

## Reporting a bug

Open an issue with a **minimal C# program** and what `dotnet run` prints versus what the csharpc
binary prints (or the compiler error). Mention the optimization level (`-O0`/`-O2`). That program is
almost exactly a regression test (see below).

## Development setup

See the Quick start in the [README](../README.md): .NET 11 SDK, LLVM 18 (`libLLVM-18.so`) and clang.

```bash
dotnet build src/Csharpc -c Release
tests/run.sh --all-opts          # must stay green
tests/conformance.sh -j 4        # must stay at 100 %
```

## Adding a test

1. Put a deterministic program in `tests/samples/NNN_name.cs` (no timing-, culture- or
   thread-scheduling-dependent output; no compiler warnings).
2. Record the oracle output with real .NET: `tests/run.sh --regen NNN_name`
   (writes `.expected`, plus `.exitcode` for non-zero exits; a `.stdin` file is fed as input).
3. Check it passes: `tests/run.sh --all-opts NNN_name`.

Never edit `.expected` files by hand — fix the program or the compiler, not the expectation.

## CI

The merge bar is the five `test (<platform>)` jobs in `.github/workflows/ci.yml` (Linux x86-64,
Linux arm64, macOS arm64, Windows x64, wasm32-wasi). A full run shards the differential suite; the
other suites stay intact, in parallel. Details and the shard counts are in
[05 — Testing](05-testing.md).

A commit that only touches `docs/**`, markdown, or the license files skips the compiler suite when
the parent commit's five checks are already green. Those checks still report success.

A targeted run does not count as that gate. On a `ci/<topic>` branch, either commit `ci/targets`
or end the commit message with a trailer (keys in the trailer override the file):

```
CI-Only: filter=544_blocking_collection platforms=macos-arm64 repeat=20
```

With a token that can dispatch workflows, use Actions → **CI (targeted)**. `gh workflow run` from
the usual agent checkout is HTTP 403. Dispatching the workflow named **CI** (no inputs) is a full
gate, not a targeted run.

## Where things live

| Area | Path |
|---|---|
| Roslyn front-end, project loading | `src/Csharpc/Frontend`, `src/Csharpc/Driver` |
| CFG → LLVM IR lowering | `src/Csharpc/CodeGen` (`FunctionCodeGen.*.cs` per feature) |
| BCL members mapped to runtime calls | `src/Csharpc/CodeGen/Intrinsics.cs` |
| BCL types written in C# (collections, LINQ) | `src/Csharpc/corelib` |
| C runtime (GC, exceptions, threads, formatting, ...) | `src/runtime` |
| LLVM C API bindings | `src/Csharpc/Llvm` |

Debugging tips: `--dump-cfg` shows Roslyn's CFG for every method, `--emit llvm-ir -O0` shows what
csharpc generated, `CSHARPC_TRACE=1` prints which symbol is being lowered, and libLLVM release builds
have no assertions — use `gdb` on crashes. See [docs/02-architecture.md](02-architecture.md).

## Style

Match the surrounding code: C# 14, file-scoped namespaces, comments that explain *why*. Commit
messages: imperative summary line, body explaining the change.
