# System.Text.Rune

CoreLib types served from the framework's IL: their row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| `System.Text.Rune`, `StringRuneEnumerator`, `SpanRuneEnumerator` (`src/Csharpc/upstream-types/System.Text.Rune.txt`, CoreLib: the slicer route; #423) | AGGREGATE_EVIDENCE | `rune_throughput` 284 752 to 286 784 B (stripped; the shadow on the integration head) | 6.2 to 6.1 s user | the old copy cannot run the bench: it stops with "Unicode categories of characters outside the Basic Multilingual Plane are not supported by csharpc yet" (and `Rune.GetNumericValue` of a non-ASCII value was unsupported too); from IL 0.059 s, 626 M instructions | 565 (`corelib/Rune.cs`); 39 added (`corelib/StringRunes.cs`: `string.EnumerateRunes` and `MemoryExtensions.EnumerateRunes` building the framework's enumerators through their internal constructors; `string.CreateFromChar` in `StringExtras.cs`) | **Flipped (#423)**. Security: UTF-8 and UTF-16 decoding of untrusted input is CoreLib's own (maximal-subpart replacement, `NeedMoreData` versus `InvalidData` exactly as .NET), and classification reads the framework's Unicode tables (the same data `CharUnicodeInfo` from IL uses), so every plane is classified instead of an explicit "not supported". Encoding into a too-short buffer returns false without writing. Found on the way, fixed in general: `[UnsafeAccessor(UnsafeAccessorKind.Constructor)]` returning a struct (sample 1406). | AOT_ROW |

New samples 1406 (`[UnsafeAccessor]` constructors of structs: overloads, unset fields stay zero, independent copies; fails on main with
"not supported by csharpc yet") and 1407 (decoding of ill-formed UTF-8/UTF-16, first and last sequence, classification and numeric values
across planes, invariant casing, encoding into short buffers, argument checks, the enumerators' U+FFFD replacement; fails on main with the
explicit "not supported" for non-ASCII numeric values).
