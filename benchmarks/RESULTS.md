# Benchmark results

csharpc against the two official .NET 11 code generators (RyuJIT and NativeAOT), on identical C#
sources (`benchmarks/programs/*.cs`). Every binary's output must be **byte-identical** to `dotnet run`;
`benchmarks/run.sh` marks a row `MISMATCH` otherwise.

**Machine:** AMD EPYC 9354P (Zen 4, 4 vCPUs), x86_64 Linux 6.8, 15 GiB RAM.
**Toolchain:** .NET SDK 11.0.100-rc.2.26504.105, LLVM 18.1.3 (system `libLLVM-18.so`, clang 18),
csharpc `main` with every feature branch merged (Release build of the compiler). **Date:** 2026-10-22.

**How to read the times.** Each cell is `wall/CPU` in milliseconds, best of 3. CPU is user+sys
time. The machine was shared with several concurrent compiler builds (load average 6–9 on 4 vCPUs),
so **wall times are noisy and CPU time is the number to compare**. JIT times include .NET runtime
startup and tiering, which is what a user experiences.

| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| binarytrees | 210/206 | 198/193 | 362/361 | 251/249 | 2416 | 34 | 1301 |
| churn | 90/86 | 88/84 | 389/360 | 397/366 | 2077 | 30 | 1309 |
| fannkuchredux | 206/202 | 209/202 | 290/286 | 334/218 | 2970 | 34 | 1309 |
| fasta | 160/155 | 156/152 | 218/210 | 180/175 | 3137 | 34 | 1301 |
| fib | 56/52 | 58/53 | 126/122 | 95/90 | 2110 | 26 | 1305 |
| listsort | 292/212 | 308/215 | 252/243 | 276/271 | 6544 | 82 | 1313 |
| mandelbrot | 100/95 | 105/96 | 248/145 | 139/102 | 2106 | 30 | 1301 |
| mandelbrot_parallel | 483/1061 | 340/1055 | 419/1160 | 459/1387 | 2686 | 34 | 1512 |
| matmul | 68/63 | 65/62 | 111/108 | 68/65 | 2324 | 34 | 1309 |
| nbody | 360/356 | 365/360 | 492/449 | 371/368 | 1994 | 34 | 1309 |
| output | 41/36 | 35/30 | 5331/5356 | 5591/5510 | 2030 | 30 | 1309 |
| quicksort | 465/457 | 473/468 | 572/570 | 433/429 | 2162 | 34 | 1301 |
| shapes | 249/245 | 262/257 | 321/324 | 250/253 | 2149 | 34 | 1309 |
| sieve | 72/68 | 77/73 | 139/134 | 113/107 | 2027 | 34 | 1301 |
| spectralnorm | 123/119 | 126/121 | 209/197 | 143/135 | 2309 | 30 | 1309 |
| spectralnorm_parallel | 259/594 | 305/508 | 587/1246 | 270/593 | 2622 | 38 | 1512 |
| strbuild | 112/104 | 98/92 | 195/163 | 111/107 | 2491 | 42 | 1309 |
| wordfreq | 73/66 | 88/70 | 229/205 | 99/72 | 3500 | 74 | 1330 |

**Summary (CPU time, -O2), 18 programs:** csharpc beats the .NET JIT on all 18 and NativeAOT on
16. It ties NativeAOT on `spectralnorm_parallel` (594 vs 593 ms) and loses `quicksort` (457 vs 429 ms,
about 6%: branch-misprediction-bound, and csharpc reloads the array length after each store — see
gaps below). Binaries are 26–82 KiB stripped vs ~1.3 MiB for NativeAOT. Compile times include
Roslyn start-up on the JIT and the machine load.

## What changed since the first measurement

The first table (csharpc @ `ec75bdb`, before the GC) lost binary-trees 2–3× and spectral-norm
by about 13%. These are the optimizations that closed those gaps, each measured on its own
(CPU ms, or instructions from callgrind where noted):

| change | where | effect |
|---|---|---|
| Bitmap-word small-object allocator: each size class caches a 64-slot free mask, so the fast path is `ctz` + clear lowest bit + multiply; no free-list threading; sweep is `allocated &= marked`; no `madvise` refaulting | `src/runtime/gc.c` | binarytrees 2.82G → 1.67G instr |
| GC trigger: max(32 MiB, 2× live) instead of 4 MiB (`CSRT_GC_TRIGGER_MB` overrides) | `src/runtime/gc.c` | binarytrees 53 → 14 collections; 0.97 s → 0.31 s CPU |
| Marking skips the method-table word, strings, and primitive arrays | `src/runtime/gc.c`, `csrt_PrimitiveArray_MT` | less conservative scanning (strbuild's 20 MB char buffer is no longer scanned) |
| **Whole-program LTO with the runtime**: `src/runtime/*.c` compiled to bitcode (cached), linked into the program module, everything but `main` internalized, then `default<O2>` | `Driver/RuntimeLto.cs`, `Toolchain.cs` | `new T()` inlines as a ~9-instruction fast path with constant-folded size class and zeroing. binarytrees 1.67G → 0.90G instr; 0.27 → 0.18 s CPU |
| Default host CPU = best `x86-64-vN` level (v3 here) with generic tuning, not `-mcpu=znver4` | `Llvm/Llvm.cs` (`--cpu native` restores host tuning) | spectralnorm 135 → 121, matmul 82 → 65. With znver4, AVX-512's EVEX `vcvtsi2sd` merges into a register written inside the unrolled loop (false dependency), and the scheduling model unrolls worse |
| Integer formatting without `snprintf`; `StringBuilder.Append(int/long/bool)` writes digits straight into the buffer; strings and builder buffers allocated without zeroing; ≥128 KiB blocks via `calloc` (pre-zeroed mmap pages); first-char-scan `IndexOf`; Latin-1 flag table for `char.IsLetter`/`IsDigit`/... | `src/runtime/csrt.c`, `src/runtime/strings.c`, `Intrinsics.cs` | strbuild 1.22G → 0.60G instr; 195 → 102 ms CPU |
| Closed-world devirtualization (class hierarchy analysis): a virtual call whose static receiver subtree resolves the slot to a single method becomes a direct (inlinable) call | `CodeGen/ModuleCodeGen.Devirtualization.cs` | semantics checked by `tests/samples/80_devirtualization.cs` |
| `zeroext` on small-int runtime parameters/returns (ABI correctness at -O0), `noalias`+`nonnull` on allocator returns | `CodeGen/Optimizations.cs` | correctness, plus null-check folding without LTO |

## Size, start-up, memory and compile time

Measured on the same machine (heavily loaded: load average 7–12, so treat wall times as upper
bounds; CPU times are the stable numbers). Start-up and peak RSS come from `tools/startup.py`
(median of N runs; peak RSS of one run via the tiny C launcher `tools/maxrss.c`).

**Binary size** (stripped, as csharpc now emits by default; NativeAOT is its default publish output):

| program | csharpc -O2 | csharpc -Oz | NativeAOT |
|---|---:|---:|---:|
| hello world | **17 KiB** | 20 KiB | 1,302 KiB |
| fib | 8 KiB | 8 KiB | 1,305 KiB |
| binarytrees | 23 KiB | 21 KiB | 1,301 KiB |
| nbody | 24 KiB | 22 KiB | 1,309 KiB |
| strbuild | 32 KiB | 29 KiB | 1,309 KiB |
| wordfreq (Dictionary, LINQ) | 60 KiB | 51 KiB | 1,330 KiB |
| listsort (List, Comparer) | 68 KiB | 57 KiB | 1,313 KiB |
| tests/programs/interpreter (records, patterns, collections) | 190 KiB | — | 1,641 KiB |

All 19 benchmark programs are 8–68 KiB at -O2 (they were 26–82 KiB before this round).
`--static` produces a fully static, ASLR-enabled (`static-pie`) executable that needs no shared
libraries at all: 740 KiB for hello world with glibc — still about half of NativeAOT's output, which
additionally needs the system glibc at run time.

**Start-up and memory:**

| program | | csharpc | NativeAOT | .NET JIT |
|---|---|---:|---:|---:|
| hello world | median start-up | **2.8 ms** | 10.5 ms | 145 ms |
| | peak RSS | **1.6 MiB** | 3.6 MiB | 23.2 MiB |
| interpreter | median start-up | **13.3 ms** | 34.6 ms | 279 ms |
| | peak RSS | **3.0 MiB** | 9.4 MiB | 29.4 MiB |

**Compile time** (source to native executable; best of 3):

| program | csharpc, NativeAOT-compiled | csharpc on the JIT | `dotnet publish -p:PublishAot=true` |
|---|---:|---:|---:|
| hello world | **0.55 s CPU** / 2.4 s wall | 2.6 s CPU / 9.7 s wall | 32.7 s wall |
| interpreter | **4.6 s CPU** / 15.4 s wall | 10.9 s CPU / 33.2 s wall | 43.0 s wall |

(`dotnet publish` CPU is not comparable: MSBuild's build-server processes aren't children of the
measured process. For reference, a JIT `dotnet publish` of the same programs took 16–19 s wall.)

What made the difference:

| change | effect |
|---|---|
| lld instead of ld.bfd when installed: no page padding between segments in the file | hello 27.2 → 22.7 KiB |
| `--gc-sections`, `--icf=safe`, packed RELR relocations (`-z pack-relative-relocs`, when the host glibc ≥ 2.36 supports them), strip by default unless `-g` | → 21.8 KiB |
| `Main` doesn't build `string[] args` when it never reads `args` (top-level statements always declare it): hello world then allocates nothing, so the GC, the allocator slow paths and exception formatting are dead and LTO drops them | → **17.3 KiB** |
| csharpc itself published with NativeAOT (`tools/publish-native.sh`); reference assemblies come from the SDK's reference pack instead of the host runtime's TPA list, which doesn't exist in a NativeAOT process | ~5× less CPU per compile |
| `MethodGroupRewriter` no longer calls `GetOperation` on every expression of the corelib (that bound ~5,000 lines of method bodies on every compile) | "lower" phase 1.3–2.1 s → 0.2–0.3 s |
| runtime bitcode merged once into a single cached module; `globaldce` right after it is linked in | runtime link ≈ −30 %, `default<O2>` ≈ −40 % for small programs |

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
RUNS=5 benchmarks/run.sh            # all benchmarks (NO_AOT=1 skips NativeAOT)
benchmarks/run.sh strbuild          # one benchmark
csharpc --cpu native ...       # opt into host-specific tuning
CSHARPC_NO_RUNTIME_LTO=1 ...   # link the runtime as objects instead of bitcode
```
