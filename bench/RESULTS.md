# Benchmark results

First measurement of csharpc against the two official .NET 11 code generators, using the same
C# source files (`bench/programs/*.cs`).

**Machine:** AMD EPYC 9354P (4 vCPUs available), x86_64 Linux 6.8, 15 GiB RAM.
**Toolchain:** .NET SDK 11.0.100-rc.2.26504.105, LLVM 18.1.3 (system `libLLVM-18.so`, clang 18 as
linker driver), csharpc @ `main` ec75bdb (Release build of the compiler).
**Date:** 2026-10-22.

| benchmark | csharpc -O2 (ms) | csharpc -O3 (ms) | .NET JIT (ms) | NativeAOT (ms) | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| binarytrees | 806 | 795 | 379 | 242 | 1613 | 18 | 1301 |
| fannkuchredux | 215 | 207 | 270 | 233 | 1740 | 18 | 1309 |
| fasta | 153 | 153 | 205 | 177 | 1885 | 18 | 1301 |
| mandelbrot | 95 | 96 | 145 | 104 | 1612 | 18 | 1301 |
| matmul | 83 | 69 | 107 | 73 | 1899 | 22 | 1309 |
| nbody | 358 | 346 | 449 | 369 | 1819 | 18 | 1309 |
| sieve | 79 | 79 | 131 | 97 | 1596 | 18 | 1301 |
| spectralnorm | 152 | 151 | 164 | 135 | 1809 | 26 | 1309 |

All 24 binaries (8 programs × csharpc/JIT/AOT) printed output **byte-identical** to `dotnet run`,
including shortest round-trip `double` formatting (e.g. nbody's `-0.16908313397889935`).

## Reading the numbers

- **Compute-bound code is already competitive.** On the loop- and float-heavy kernels (mandelbrot,
  sieve, fasta, fannkuch, nbody, matmul) csharpc -O2 is 1.0–1.5× faster than the JIT (whose time
  includes ~50–70 ms of runtime startup and tiering), and roughly on par with or slightly
  faster than NativeAOT. It's the same front-end semantics fed to LLVM's mid-level optimizer
  instead of RyuJIT's. Every array access still has a bounds check and every member access a null
  check; LLVM hoists or folds many of them.
- **Allocation-bound code is slow, as expected.** binarytrees is 2–3× slower than .NET because csrt
  allocates every object with `calloc` and never frees anything (no GC yet: ADR 0004). .NET's
  bump-pointer allocator plus generational GC is the right comparison point. The binary-trees
  depth is capped at 16 so the leaked heap (~0.5 GB) fits in memory.
- **Binaries are tiny.** ~18 KiB stripped vs ~1.3 MiB for NativeAOT, because csharpc links only
  csrt and libc. It has no GC, reflection, type loader, or exception-handling runtime yet. This
  gap will shrink as the runtime grows; it isn't a like-for-like comparison.
- **Compile time (~1.6–1.9 s) is dominated by fixed costs**: starting the compiler on the JIT, loading
  Roslyn, and binding against ~170 framework reference assemblies. Codegen + LLVM -O2 + link of
  these programs takes well under 300 ms. AOT-compiling csharpc itself and trimming the reference
  set are the obvious fixes.
- -O3 vs -O2 makes little difference except matmul (vectorization/unrolling of the inner loop).

## Methodology

- `bench/run.sh` builds each program with csharpc -O2 and -O3, publishes it as a Release
  file-based app for the JIT (`dotnet app.dll`, timing includes runtime startup) and with
  `-p:PublishAot=true` for NativeAOT, then runs each binary 3 times and reports the **best
  wall-clock time** (`date +%s%N` around the process). Every run's stdout is compared to the
  `.expected` file recorded from `dotnet run`. A mismatch is reported instead of a time.
- csharpc compile time = wall time of one `csharpc -O2 file.cs -o exe` invocation (Roslyn + codegen
  + LLVM + clang link; the csrt object is cached after the first build).
- Sizes: csharpc binary after `strip`, NativeAOT binary as published (debug info is already split
  into `.dbg`).
- Inputs are sized so each run takes well under ~1 s. The machine is a shared VM, so expect ±10 %
  noise. Treat these as indicative only.

## Caveats

- No GC in csrt yet: allocation-heavy results are meaningless until it lands.
- csharpc doesn't yet support generics, structs, interfaces, lambdas, exceptions or `foreach`
  over arrays, so the programs are written in a restricted style (see `GAPS.md`). Idiomatic
  versions (structs for vectors, `foreach`, `Span<T>`) would change the .NET numbers too.
- No multithreading anywhere (the canonical benchmarks-game entries are parallel).
- `Console.WriteLine` in csrt is block-buffered and flushed at exit; .NET's console stream is
  autoflushing. These programs print only a few lines, so it doesn't matter here.

Reproduce: `bench/run.sh` (≈2 min; `NO_AOT=1` skips NativeAOT, `RUNS=5` for more samples).
