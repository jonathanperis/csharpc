| benchmark | csharpc -O2 (ms) | csharpc -O3 (ms) | .NET JIT (ms) | NativeAOT (ms) | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| binarytrees | 886 | 887 | 321 | 226 | 1660 | 46 | 1301 |
| fannkuchredux | 205 | 197 | 280 | 219 | 1769 | 46 | 1309 |
| fasta | 161 | 207 | 288 | 180 | 2602 | 50 | 1301 |
| mandelbrot | 109 | 114 | 158 | 132 | 1953 | 46 | 1301 |
| matmul | 104 | 66 | 129 | 102 | 2717 | 54 | 1309 |
| nbody | 530 | 489 | 584 | 463 | 4846 | 50 | 1309 |
| sieve | 193 | 139 | 200 | 98 | 3778 | 46 | 1301 |
| spectralnorm | 233 | 219 | 254 | 326 | 2831 | 54 | 1309 |
