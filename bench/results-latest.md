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
