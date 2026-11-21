# System.Runtime.Numerics (BigInteger, Complex)

A framework library served from its own IL: its row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| System.Runtime.Numerics (`BigInteger`, `Complex`; FrameworkIl: the assembly is compiled from the shared framework once csharpc implements none of its types; CoreLib members its IL calls in `src/Csharpc/upstream-types/System.Runtime.Numerics.txt`; #423) | 140 samples that use BigInteger and its neighbours (boxing, ranges, tuples, conversions, string.Join) byte-identical to real .NET at -O0 and -O2 (280 runs), 8 of them via IL and 7 on wasm32 (gate after merging the integration branch at dc414710; 666 and 1443 are pointer-size dependent, below); new 1443. On main 1443 does not compile (`BigInteger.Log` was missing) | `biginteger_throughput` 224 288 to 424 400 B (stripped): the framework's sub-quadratic multiplication and division, Lehmer GCD and faster decimal formatting and parsing: a measured speed gain paid in size | 4.9 to 10.4 s user (the framework's algorithms are more code) | 0.286 to 0.054-0.064 s (4.5x to 5.3x faster); 2 855 M to 341 M instructions (callgrind) | 1 015 (`corelib/BigIntegerOps.cs`, `runtime/bigint.c`, the builtin BigInteger type in codegen: TypeMapper, BclValueTypes, CoreLibRuntime, struct equality, tuples, boxing); 66 added | **Flipped (#423)**. Security: BigInteger is no longer a compiler builtin (a pointer to a `uint[]` with the sign elsewhere) but the framework's struct, with its own overflow checks (`OverflowException` past the maximum length, on conversions out of range), its pooled scratch buffers returned on every path, and its parsing of untrusted text (length limits, `FormatException`, the hex and binary forms) and exception texts. Its interfaces on boxes come from the IL type's own method table instead of a C-runtime one (#433 was the old builtin's gap). | `biginteger_throughput`: 424 248 / 1 460 168 B; compile cpu 13.1 / 20.3 s; run 75 / 67 ms (1.12x slower with 26 % fewer instructions, #448); 341 M / 460 M instructions |

New sample 1443 (arithmetic and generic math over BigInteger and Complex, bit operations, conversions in and out of range, parsing and formatting
with their errors, `Log`, `ModPow`, `GreatestCommonDivisor`).

BigInteger's limbs are `nuint` in .NET 11's IL, so a few results depend on the pointer size exactly as they do on .NET itself: on wasm32 a value
such as 2^40 takes two limbs instead of one (an out-of-range conversion to `short` reports Int32's overflow message rather than the checked
conversion's), and `>>>` of a negative value shifts its two's complement over whole limbs. 666 and 1443 record the 64-bit results and are
listed for WASI in `tests/platform-skips.txt`; the other BigInteger samples run there.
