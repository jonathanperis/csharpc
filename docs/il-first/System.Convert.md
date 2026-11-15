# System.Convert

A CoreLib type served from the framework's IL: its row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| `System.Convert` (`src/Csharpc/upstream-types/System.Convert.txt`, CoreLib: the slicer route; #423) | AGGREGATE_EVIDENCE | `convert_throughput` 683 600 to 682 112 B (stripped; the shadow measured on the integration head, which already had globalization from IL) | 15.2 to 14.6 s user | median of 7: 0.172 to 0.126 s (1.37x faster); 921 M to 797 M instructions (callgrind) | 2 821 (`corelib/Convert.cs`, `corelib/ConvertExtras.cs`); 217 added (`corelib/BoxedConvertible.cs`, the contract of the box method tables: IConvertible on boxed primitives, decimal, DateTime and enums) | **Flipped (#423)**. Security: string parsing in other bases (`ParseNumbers`), Base64 and hex text decoding are the framework's own, with its length, digit and overflow checks and its exception texts. The typed `Convert` overloads that a source type's `IConvertible` refuses (`ToBoolean(char)`, `ToInt32(DateTime)`...) call that `IConvertible` member on the box; `BoxedConvertible` throws .NET's `InvalidCastException` there itself, so the call cannot recurse. The old copy's explicit "not supported by csharpc yet" for enum values (`Convert.GetTypeCode`, `ChangeType` of an enum, `ToDateTime`) is gone: enums convert through their underlying value, as `Enum`'s `IConvertible` does. `Convert.IsDBNull` was missing on main. | AOT_ROW |

New sample 1405 (enums through Convert and IConvertible, refused conversions with their texts through the typed overloads and through boxes,
`ToType`/`ChangeType` to non-convertible types, `ToString(object, provider)`, base conversions and their errors, hex and Base64 errors). Convert's
`ToString(object, provider)` asks `IFormattable` on the box, which a boxed BigInteger only answers since #433.
