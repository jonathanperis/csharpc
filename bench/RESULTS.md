# Benchmark results

csharpc against the two official .NET 11 code generators (RyuJIT and NativeAOT), on identical C#
sources (`bench/programs/*.cs`). Every binary's output must be **byte-identical** to `dotnet run`;
`bench/run.sh` marks a row `MISMATCH` otherwise.

**Machine:** AMD EPYC 9354P (Zen 4, 4 vCPUs), x86_64 Linux 6.8, 15 GiB RAM.
**Toolchain:** .NET SDK 11.0.100-rc.2.26504.105, LLVM 18.1.3 (system `libLLVM-18.so`, clang 18),
csharpc branch `perf` (Release build of the compiler). **Date:** 2026-10-22.

**How to read the times.** Each cell is `wall/CPU` in milliseconds, best of 5. CPU is user+sys
time. The machine was shared with several concurrent compiler builds (load average 6–9 on 4 vCPUs),
so **wall times are noisy and CPU time is the number to compare**. JIT times include .NET runtime
startup and tiering, which is what a user experiences.

| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| binarytrees | 292/**218** | 305/216 | 512/353 | 395/268 | 4502 | 22 | 1301 |
| churn | 131/**101** | 137/107 | 725/368 | 470/327 | 3489 | 18 | 1309 |
| fannkuchredux | 218/**205** | 238/194 | 278/268 | 228/213 | 2469 | 22 | 1309 |
| fasta | 180/**149** | 211/147 | 290/206 | 230/173 | 3325 | 22 | 1301 |
| fib | 59/**51** | 58/45 | 197/122 | 116/84 | 5679 | 18 | 1305 |
| mandelbrot | 244/**93** | 104/91 | 149/132 | 157/106 | 5838 | 22 | 1301 |
| matmul | 82/**64** | 96/60 | 182/110 | 184/65 | 3761 | 22 | 1309 |
| nbody | 518/**362** | 617/363 | 696/455 | 556/394 | 5455 | 22 | 1309 |
| output | 45/**39** | 68/41 | 8100/5614 | 8541/5675 | 2724 | 18 | 1309 |
| quicksort | 715/**467** | 702/479 | 781/535 | 671/475 | 2803 | 22 | 1301 |
| shapes | 358/**269** | 269/235 | 367/327 | 298/271 | 5320 | 26 | 1309 |
| sieve | 78/**72** | 88/80 | 151/142 | 106/97 | 2172 | 22 | 1301 |
| spectralnorm | 130/**121** | 127/118 | 205/193 | 470/201 | 2196 | 22 | 1309 |
| strbuild | 129/**110** | 112/108 | 278/176 | 156/134 | 5615 | 30 | 1309 |

**Summary (CPU time, -O2):** csharpc beats the .NET JIT on all 14 benchmarks. It beats
NativeAOT on 11 and is within noise (≤ 2%) on matmul, quicksort and shapes. On those three,
csharpc -O3 is ahead on matmul (60 vs 65) and shapes (235 vs 271).

## What changed since the first measurement

The first table (csharpc @ `ec75bdb`, before the GC) lost binary-trees 2–3× and spectral-norm
by about 13%. These are the optimizations that closed those gaps, each measured on its own
(CPU ms, or instructions from callgrind where noted):

| change | where | effect |
|---|---|---|
| Bitmap-word small-object allocator: each size class caches a 64-slot free mask, so the fast path is `ctz` + clear lowest bit + multiply; no free-list threading; sweep is `allocated &= marked`; no `madvise` refaulting | `runtime/gc.c` | binarytrees 2.82G → 1.67G instr |
| GC trigger: max(32 MiB, 2× live) instead of 4 MiB (`CSRT_GC_TRIGGER_MB` overrides) | `runtime/gc.c` | binarytrees 53 → 14 collections; 0.97 s → 0.31 s CPU |
| Marking skips the method-table word, strings, and primitive arrays | `runtime/gc.c`, `csrt_PrimitiveArray_MT` | less conservative scanning (strbuild's 20 MB char buffer is no longer scanned) |
| **Whole-program LTO with the runtime**: `runtime/*.c` compiled to bitcode (cached), linked into the program module, everything but `main` internalized, then `default<O2>` | `Driver/RuntimeLto.cs`, `Toolchain.cs` | `new T()` inlines as a ~9-instruction fast path with constant-folded size class and zeroing. binarytrees 1.67G → 0.90G instr; 0.27 → 0.18 s CPU |
| Default host CPU = best `x86-64-vN` level (v3 here) with generic tuning, not `-mcpu=znver4` | `Llvm/Llvm.cs` (`--cpu native` restores host tuning) | spectralnorm 135 → 121, matmul 82 → 65. With znver4, AVX-512's EVEX `vcvtsi2sd` merges into a register written inside the unrolled loop (false dependency), and the scheduling model unrolls worse |
| Integer formatting without `snprintf`; `StringBuilder.Append(int/long/bool)` writes digits straight into the buffer; strings and builder buffers allocated without zeroing; ≥128 KiB blocks via `calloc` (pre-zeroed mmap pages); first-char-scan `IndexOf`; Latin-1 flag table for `char.IsLetter`/`IsDigit`/... | `runtime/csrt.c`, `runtime/strings.c`, `Intrinsics.cs` | strbuild 1.22G → 0.60G instr; 195 → 102 ms CPU |
| Closed-world devirtualization (class hierarchy analysis): a virtual call whose static receiver subtree resolves the slot to a single method becomes a direct (inlinable) call | `CodeGen/ModuleCodeGen.Devirtualization.cs` | semantics checked by `tests/samples/80_devirtualization.cs` |
| `zeroext` on small-int runtime parameters/returns (ABI correctness at -O0), `noalias`+`nonnull` on allocator returns | `CodeGen/Optimizations.cs` | correctness, plus null-check folding without LTO |

## Remaining gaps and caveats

- **Partitioning loops with random data** (quicksort): this code is limited by branch
  mispredictions. Both compilers emit essentially the same loop: bounds check, load, compare.
  csharpc also reloads the array length after each element store, because LLVM can't prove an
  element store doesn't alias the length field. Distinct TBAA tags for array lengths vs elements
  would remove the reload.
- **Loop-invariant null checks** (matmul at -O2): `bj[k]` re-tests `bj != null` and reloads
  `bj.Length` on every iteration, because LICM can't hoist a load guarded by a check. LLVM's -O3
  pipeline fixes this with non-trivial loop unswitching (60 vs 64 ms). Hardware-trap null checks
  (implicit null checks plus a fault handler, as RyuJIT does) are the real fix; they need the
  exception-handling runtime.
- **Strict FP**: neither compiler reassociates floating-point reductions, so dot products and
  spectral-norm stay scalar on both sides. That's correct C# semantics; csharpc doesn't trade
  accuracy for speed.
- **The GC is conservative and non-generational.** Full-heap marking at 2× live growth makes the
  cost proportional to the live heap at each collection. That's fine for these programs, but
  allocation-heavy code with a very large live heap would favor .NET's generational GC. A
  precise or generational collector needs compiler-emitted pointer maps and write barriers.
- **Feature coverage limits what can be benchmarked.** Programs built on `List<T>`,
  `Dictionary<K,V>` or LINQ (k-nucleotide, regex-redux, ...) can't be ported until the self-hosted
  corelib exists.
- **Compile time** (2–6 s here, about 1.6 s on an idle machine) is mostly Roslyn startup and
  reference loading on the JIT, plus about 60 ms to link the runtime bitcode. Shipping the compiler
  ReadyToRun or NativeAOT-compiled would cut most of it.
- **Binary sizes** (18–30 KiB vs 1.3 MiB) aren't a like-for-like comparison: csrt has no
  reflection, type loader or full exception-handling runtime.

## Reproduce

```bash
export PATH=$HOME/.dotnet11:$PATH
RUNS=5 bench/run.sh            # all benchmarks (NO_AOT=1 skips NativeAOT)
bench/run.sh strbuild          # one benchmark
csharpc --cpu native ...       # opt into host-specific tuning
CSHARPC_NO_RUNTIME_LTO=1 ...   # link the runtime as objects instead of bitcode
```
