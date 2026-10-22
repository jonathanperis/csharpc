Times are wall/CPU (user+sys) in ms, best of 5.

| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| mandelbrot_parallel | 460/1047 | 540/1057 | 502/1160 | 512/1271 | 2417 | 34 | 1512 |
| spectralnorm_parallel | 310/685 | 273/598 | 657/1164 | 291/639 | 3028 | 38 | 1512 |
