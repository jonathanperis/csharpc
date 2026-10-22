Times are wall/CPU (user+sys) in ms, best of 3.

| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| listsort | 276/211 | 278/226 | 338/255 | 332/260 | 5759 | 78 | 1313 |
