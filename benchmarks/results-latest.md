Times are wall/CPU (user+sys) in ms, best of 3.

| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |
|---|---:|---:|---:|---:|---:|---:|---:|
| binarytrees | 232/221 | 217/204 | 421/400 | skipped | 2509 | 34 | - |
