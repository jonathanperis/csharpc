# System.Collections.Hashtable

A library or CoreLib type served from the framework's IL: its row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements"). Measured for #367 and **not flipped**:
the shadow (`corelib/Hashtable.cs`) stays until #373. `ArrayList` and `BitArray`, the other System.Collections types of CoreLib, do not
compile from IL yet (#372).

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| `System.Collections.Hashtable` (an entry in `src/Csharpc/upstream-types/`, tried in #367) | correct: with it listed, the Hashtable samples (102, 504, 518, 601 and the rest of the 104-sample set) are byte-identical to real .NET at -O0 and -O2 | `hashtable_throughput` 145 416 to 188 744 B (**1.30x**): `Insert`'s duplicate-key message `SR.Format(Argument_AddingDuplicate__, key, key)` formats `object`s through composite formatting, which brings in every primitive's formatter, culture data and decimal/BigInteger formatting | 4.4 to 5.7 s | `hashtable_throughput` (20 rounds: 200 k inserts, 1 M lookups with misses, enumeration, 100 k `Remove`, string keys): instructions 9 391 M to 9 648 M (**1.03x**), median of 5 1 103 to 1 133 ms | (523, `corelib/Hashtable.cs`) | **Not flipped**: slower and 30 % larger, and the shadow is already a port of .NET's bucket layout (same order, same growth). #373: replace the `SR.Format` call (a general slicer replacement that formats `{n}` items without composite formatting would serve every sliced type), then flip. | the shadow, as shipped: `hashtable_throughput` 145 176 / 1 353 576 B; compile cpu 4.2 / 10.9 s; run 1 016 / 2 943 ms (0.35x); 9.39 G / 20.5 G instructions |
