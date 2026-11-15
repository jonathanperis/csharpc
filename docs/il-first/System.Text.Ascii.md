# System.Text.Ascii, Base64, CompositeFormat, StandardFormat

CoreLib types served from the framework's IL: their row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| `System.Text.Ascii`, `System.Buffers.Text.Base64`, `System.Text.CompositeFormat`, `System.Buffers.StandardFormat` (`src/Csharpc/upstream-types/System.Text.Ascii.txt`, CoreLib: the slicer route; #423) | AGGREGATE_EVIDENCE | `ascii_base64_throughput` 230 560 to 235 344 B (+4.8 KB, stripped) | 5.9 to 6.3 s user | median of 7: 0.462 to 0.090 s (5.1x faster); 3 758 M to 832 M instructions (callgrind). The framework's 128-bit paths (vector narrowing, widening and case changes, Base64's Vector128 decoder) now run on csharpc's Vector128 lowering; the old copy went through the C runtime's 8-at-a-time loops for transcoding only and was scalar elsewhere | 542 (`corelib/AsciiMembers.cs`, `corelib/Base64Members.cs`, `corelib/Text/CompositeFormat.cs`, `corelib/Text/StandardFormat.cs`, `AsciiExtras` in `corelib/StringExtras.cs`) | **Flipped (#423)**. Security: ASCII and Base64 validation, the destination-size checks of every `OperationStatus` member and the in-place coders are the framework's own code over spans (bounds-checked; the pointer loops are CoreLib's, which fuzzing and the BCL's tests cover). Base64 decoding of untrusted input stops at the first invalid byte with `InvalidData` and the exact consumed/written counts .NET reports, whitespace handling included. `CompositeFormat.Parse` keeps .NET's offsets and reasons; the old copy reported a lone `}` as "Unexpected closing brace" where .NET says "Format item ends prematurely" (a silent difference, now gone). `Ascii.Trim*`, `Base64.EncodeToUtf8InPlace` and `Base64.IsValid(…, out int)` were missing on main. | AOT_ROW |

Compiler work this flip needed, each general and covered by a regression sample:

- Vector128: `WidenLower`/`WidenUpper`/`Widen`, `Narrow` (also `Vector.Narrow`), `AddSaturate`/`SubtractSaturate`, the `*OrEqualAll`/`*OrEqualAny`
  comparisons, `IEquatable<Vector128<T>>.Equals` (NaN equal to NaN, from source and IL), `ISimdVector.Alignment`, `GetLower`/`GetUpper` and
  `Create(Vector64, Vector64)`; Vector64: `LoadUnsafe`, `Create`, `GetElement`, `Widen*` (the widening loader Ascii uses without an ISA class).
  Sample 1401.
- A generic virtual method implemented by a struct and called through `constrained.` with the caller's type argument
  (`IBase64Decoder<T>.DecodeWithWhiteSpaceBlockwiseWrapper<TDecoder>`) is the implementation constructed with that argument; it was lowered open
  ("open generic instantiation"). Sample 1402.
- `box T; isinst S; brtrue` with T a known value type and S a struct folds like the interface case already did, and `typeof(A) == typeof(B)`
  guarding a folded branch no longer materializes the two `Type` objects: Base64's decoder structs no longer get box method tables, so their
  AVX2/AVX-512/AdvSimd members are not compiled (they produced CSLLVM0007 warnings and dead code). A narrow part of #420. Sample 1402.
- A CoreLib-internal member hosted from its own IL: `Vector128.StoreLowerUnsafe`.
