# Third-party notices

csharpc includes source code derived from the following projects. Their license terms apply to
those parts of csharpc.

## .NET Runtime (dotnet/runtime)

- **What:** the regular-expression engine in `src/Csharpc/corelib/Regex/` is a port of
  `src/libraries/System.Text.RegularExpressions/src` (parser, tree optimizer, find optimizations,
  case-equivalence tables, writer, interpreter and the public `Regex` / `Match` / `Group` /
  `Capture` API), adapted to the csharpc corelib. Each ported file keeps the upstream license
  header.
- **What:** globalization. `src/Csharpc/corelib/Globalization/` ports
  `src/libraries/System.Private.CoreLib/src/System/Globalization` (CultureInfo, CultureData and its ICU
  path, NumberFormatInfo, DateTimeFormatInfo and its scanner, DateTimeFormat, DateTimeParse, the
  calendars, CalendarData, CompareInfo, TextInfo, RegionInfo, GlobalizationMode, IcuLocaleData,
  HebrewNumber, SortKey, SortVersion, SurrogateCasing, InvariantModeCasing) and the Interop.Globalization
  imports from `src/libraries/Common/src/Interop`; the corelib's number layout and parsing code
  (`Number.Formatting.cs`, `Number.Parsing.Culture.cs`) ports `NumberToString`, `NumberToStringFormat`,
  `TryParseNumber` and their helpers from `src/libraries/Common/src/System/Number.{Formatting,Parsing}.Common.cs`
  and `System.Private.CoreLib/src/System/Number.Parsing.cs`; `SR.cs` holds messages of
  `System.Private.CoreLib/src/Resources/Strings.resx`. `src/runtime/globalization/` is the native shim
  `src/native/libs/System.Globalization.Native` (with `pal_compiler.h` from `src/native/libs/Common`).
  Each ported file keeps the upstream license header.
- **What:** `System.Random` in `src/Csharpc/corelib/Random/` is a port of
  `src/libraries/System.Private.CoreLib/src/System/Random*.cs` (the public API, the legacy seeded
  generator .NET keeps for `new Random(seed)`, and xoshiro256**), adapted to the csharpc corelib.
- **Source:** <https://github.com/dotnet/runtime>, release/11.0, commit
  `bc123d0bd173a538addc07e0f8fa38a90f0925c1`.
- **What:** `System.Text.Json` and `System.Text.Encodings.Web` in `src/Csharpc/corelib/Json/`, ported
  from `src/libraries/System.Text.Json/src` and `src/libraries/System.Text.Encodings.Web/src` (see the
  table below), adapted to the csharpc corelib. Each ported file keeps the upstream license header
  and names its upstream sources.
- **What:** `ConcurrentDictionary` and `ConcurrentStack` in `src/Csharpc/corelib/Concurrent/`, ported
  from `src/libraries/System.Private.CoreLib/src/System/Collections/Concurrent/ConcurrentDictionary.cs`
  and `src/libraries/System.Collections.Concurrent/src/System/Collections/Concurrent/ConcurrentStack.cs`,
  with the non-randomized string hashing of `System.Private.CoreLib/src/System/String.Comparison.cs`;
  `ConcurrentBag` follows `ConcurrentBag.cs`. Each ported file keeps the upstream license header.
- **What:** `ReadOnlySequence<T>`, `SequenceReader<T>`, `BuffersExtensions` and the sequence types in
  `src/Csharpc/corelib/Buffers/ReadOnlySequence.cs`, ported from `src/libraries/System.Memory/src/System/Buffers/`
  (`ReadOnlySequence.cs`, `ReadOnlySequence.Helpers.cs`, `SequenceReader.cs`, `SequenceReader.Search.cs`,
  `BuffersExtensions.cs`) and `System.Private.CoreLib`'s `ReadOnlySequenceSegment.cs` and `SequencePosition.cs`;
  the file keeps the upstream license header.
- **What:** `System.Text.Rune` in `src/Csharpc/corelib/Rune.cs`, ported from
  `src/libraries/System.Private.CoreLib/src/System/Text/Rune.cs` (decoding, encoding, validation); the file keeps
  the upstream license header.
- **Source:** <https://github.com/dotnet/runtime>, release/11.0.
- **License:** MIT

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

System.Text.Json files (all under `src/Csharpc/corelib/Json/`):

| File | Upstream sources |
|---|---|
| `JsonCore.cs` | `System.Text.Json`: `JsonException`, `JsonConstants`, `BitStack`, resource strings |
| `UnicodeRanges.g.cs`, `Encoders.cs` | `System.Text.Encodings.Web`: `UnicodeRanges`, `TextEncoderSettings`, `OptimizedInboxTextEncoder`, `DefaultJavaScriptEncoder`; Unicode defined-characters data |
| `JsonWriter.cs`, `ArrayBufferWriter.cs` | `System.Text.Json`: `Utf8JsonWriter*`, `JsonWriterHelper*`, `JsonEncodedText`, `JsonWriterOptions`; `System.Memory`: `ArrayBufferWriter<T>` |
| `JsonReader.cs`, `JsonReaderSupport.cs` | `System.Text.Json`: `Utf8JsonReader*`, `JsonReaderHelper*`, `JsonHelpers.Date`, `ThrowHelper`; `System.Private.CoreLib`: `Number.NumberToDecimal` (via `Utf8Parser`) |
| `JsonDocument.cs` | `System.Text.Json`: `JsonDocument*`, `JsonElement*`, `JsonProperty`, `MetadataDb` |
| `JsonNodes.cs` | `System.Text.Json`: `JsonNode`, `JsonObject`, `JsonArray`, `JsonValue*` |
| `SerializerOptions.cs`, `SerializerMetadata.cs`, `SerializerConverters.cs`, `SerializerObjects.cs`, `Serializer.cs` | `System.Text.Json`: `JsonSerializer*`, `JsonSerializerOptions`, `JsonSerializerContext`, `JsonTypeInfo*`, `JsonPropertyInfo*`, `JsonMetadataServices*`, `PolymorphicTypeResolver`, `JsonNamingPolicy` (`JsonSeparatorNamingPolicy`), the value, enum, object and collection converters, `ThrowHelper.Serialization` |

## ICU (International Components for Unicode)

- **What:** csharpc does not include ICU. Programs load the ICU libraries installed on the machine at run
  time (`libicuuc`/`libicui18n` on Linux, `libicucore` on macOS, `icu.dll` on Windows), as .NET does; the
  declarations the shim compiles against (`src/runtime/globalization/pal_icushim_internal_android.h`) come
  from dotnet/runtime (MIT, above), which extracted them from the ICU headers.
- **Source:** <https://icu.unicode.org>
- **License:** Unicode License v3 (ICU), not redistributed by csharpc.
