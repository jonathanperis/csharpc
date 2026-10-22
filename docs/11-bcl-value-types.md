# 11 — BCL value types, Type objects and library helpers

This page covers the library types csharpc implements natively instead of compiling the BCL:
`System.Decimal`, `TimeSpan`, `DateTime`, `Stopwatch`, `BigInteger`, `System.Type`
("reflection-lite"), plus `BitOperations`, `System.Array` helpers, `Convert`, culture-sensitive
string comparison and a number of smaller members. Everything here is verified with differential
samples against real .NET 11 (`tests/samples/80_decimal.cs` … `86_biginteger.cs`).

Code map:

| Piece | Where |
|---|---|
| Member table (methods, getters, ctors, operators, static fields) | `src/Csharpc/CodeGen/BclValueTypes.cs` |
| Per-function helpers (decimal ops, Type objects, loops, delegate calls) | `src/Csharpc/CodeGen/FunctionCodeGen.Bcl.cs` |
| Array / enum / type-only method tables, IR comparers for `Array.Sort` | `src/Csharpc/CodeGen/ModuleCodeGen.Bcl.cs` |
| Decimal arithmetic, conversions, parsing | `src/Csharpc/corelib/DecimalOps.cs` (C#, [ADR 0008](adr/0008-runtime-in-csharp.md)) |
| TimeSpan, DateTime arithmetic, calendar, time zones and formatting | `src/Csharpc/corelib/DateTimeOps.cs` (`DateTimeMembers` shadows the rest of `DateTime`); clocks and Stopwatch in `src/runtime/datetime.c` |
| `System.Type` objects | `src/runtime/type.c` |
| BigInteger | `src/Csharpc/corelib/BigIntegerOps.cs` |
| Array helpers, introsort, Convert, Environment, collation | `src/runtime/bclmisc.c` |
| Number formatting and parsing (decimal included) | `src/Csharpc/corelib/Number.Formatting.cs`, `Number.Parsing.cs` |
| Boxed decimal / BigInteger / TimeSpan / DateTime method tables | `src/runtime/{decimal,bigint,datetime}.c` (their slots call corelib exports) |

## How library members are dispatched

Roslyn binds user code against the real reference assemblies, so every call site names an exact
BCL member. Lowering looks members up by the `Intrinsics.Key` format
(`"System.TimeSpan.FromMinutes(double)"`, `"decimal.Round(decimal, int)"`, accessors as
`get_X`, constructors as `.ctor`, operators as `op_X`):

1. `Intrinsics` (the original table) — consulted first.
2. `BclValueTypes.TryGetRaw` — handlers that need the unevaluated invocation: overloads taking
   `params ReadOnlySpan<T>` that C# 13 prefers for `string.Join(",", a, b, c)`,
   `string.Format`/`Console.WriteLine` with 4+ arguments, and collection-expression arguments.
3. `BclValueTypes.TryGet` — handlers receiving the evaluated receiver and arguments (by-ref
   arguments as addresses).

The same table serves property getters, library constructors (class and struct), library
operator methods (`TimeSpan + TimeSpan`, `Type == Type`, `BigInteger * BigInteger`), compound
assignment and `++`/`--` through library operators, interface calls devirtualized to library
implementations, and static library fields (`string.Empty`, `TimeSpan.Zero`,
`Stopwatch.Frequency`, ...).

## System.Decimal

* **Representation:** the CLR's own layout, `{ i32 flags, i32 hi32, i64 lo64 }` (scale in bits
  16–23 of `flags`, sign in bit 31). Constants are emitted from `decimal.GetBits`.
* **Arithmetic** (`corelib/DecimalOps.cs`) computes the *exact* result with a small bignum,
  then fits it into 96 bits / scale ≤ 28 by dividing by a power of ten with round-half-to-even,
  which is the CLR's contract. Scale rules follow .NET: `+`/`-` keep the larger scale, `*` adds
  scales, `/` produces the smallest scale that represents the quotient exactly (up to 28 digits),
  `%` keeps the larger scale and the dividend's sign, and returns the dividend unchanged when
  its magnitude is smaller than the divisor's (as `DecCalc.VarDecMod` does). Overflow throws `OverflowException`
  ("Value was either too large or too small for a Decimal."); division by zero throws
  `DivideByZeroException`.
* **Conversions:** `int`/`long` → decimal are exact; `double` → decimal is **exact** in .NET 11
  (`(decimal)0.1 == 0.1000000000000000055511151231`) and is implemented from the double's exact
  decimal expansion; decimal → integer truncates and always throws on overflow (C# semantics);
  decimal → double is correctly rounded (what .NET 11 does).
* **Formatting:** `ToString()` keeps trailing zeros from the scale (`1.50m` → `"1.50"`); negative
  zero prints without a sign. Format strings (`F`, `N`, `E`, `P`, `C`, `G`, custom) round the
  exact digits **half away from zero** (unlike `double`, which rounds ties to even).
* `Math.Round/Floor/Ceiling/Truncate/Abs/Max/Min/Sign`, `decimal.Parse/TryParse` (NumberStyles.Number),
  boxing (`csrt_Boxed_Decimal_MT`), `GetHashCode` (normalized, so `1.0m` and `1.00m` hash alike).

## TimeSpan, DateTime, Stopwatch

* `TimeSpan` is lowered to its **tick count** (`i64`, 100 ns units). `DateTime` is .NET's own
  `_dateData` word (`i64`): ticks in bits 0–61 and the **Kind** in bits 62–63 (Unspecified 0,
  Utc 1, Local 2, and 3 for a Local time in the daylight-saving occurrence of an ambiguous hour,
  .NET's `KindLocalAmbiguousDst`). `Ticks` masks the Kind off; arithmetic (`Add*`, `+`/`-`
  `TimeSpan`, `Date`) keeps every kind bit; equality, ordering and hashing look at the ticks only
  (`==`, `Equals`, `CompareTo`, `EqualityComparer`/`Comparer`, `Dictionary` keys, boxed `Equals` and
  `GetHashCode`, records, tuples, and the default `ValueType.Equals`/`GetHashCode` of structs with
  `DateTime` fields, which compare field by field with each field's own `Equals`). `UtcNow` and `UnixEpoch` are Utc; `Now` and `Today` are
  Local; `MinValue`, `MaxValue` and `default` are Unspecified.
* **Time zones** come from libc (`localtime_r` over tzdata and `TZ`, which .NET reads too), resolved
  like `TimeZoneInfo.Local` (checked against .NET in New York, London, Amsterdam, Dublin, Moscow,
  Sao Paulo, St. John's, Monrovia, Kolkata, Sydney, Lord Howe, Chatham, Apia, Kiritimati, Yakutat,
  Sitka, Manila, Guam and UTC):
  * a wall-clock (Unspecified or Local) time in an ambiguous hour takes standard time unless it is a
    Local value flagged as the daylight-saving occurrence; a skipped time takes the zone's base
    offset and is never daylight-saving time; `IsDaylightSavingTime()` is false in both cases;
  * `ToUniversalTime()` treats Unspecified as local and, like .NET, *adds* the offset of a skipped
    time (02:30 on a New York spring-forward day becomes 21:30Z the day before);
  * `ToLocalTime()` treats Unspecified as UTC and sets the ambiguous-hour flag; both conversions
    clamp to `MinValue`/`MaxValue`;
  * local mean times keep only the hours and minutes of their difference to the base offset (New
    York's -4:56:02 is -4:57; Apia's -11:26:04 against +13 wraps to +12:34), offsets are clamped to
    ±14 h (Yakutat's +14:41 is +14:00), and a daylight-saving period still running at the end of year
    9999 ends at local midnight.

  Samples `188`–`190` pin a zone with `Environment.SetEnvironmentVariable("TZ", ...)` before
  anything reads the local zone (both .NET and libc load it lazily), so their recorded output does
  not depend on the machine's zone.
* `SpecifyKind`, `ToBinary`/`FromBinary`, `ToFileTime[Utc]`/`FromFileTime[Utc]`, `Equals(object)`,
  `CompareTo(object)` and the conversions above are C# shadows (`DateTimeMembers`); invalid kinds
  throw `ArgumentException` like .NET. A `DateTime` P/Invoke parameter or return is diagnosed as
  unsupported (.NET marshals it as an OLE Automation date).
* Calendar math is the CLR's own algorithm (proleptic Gregorian, years 1–9999):
  `AddMonths` clamps the day, `AddDays(double)` adds integral and fractional parts separately
  (.NET 7+ behaviour), range errors throw `ArgumentOutOfRangeException` with .NET's messages.
* `TimeSpan.FromX(double)` truncates exact ticks (.NET 7+); the .NET 9 integer overloads
  (`FromSeconds(long)`, `FromHours(int, long minutes = 0, ...)`) are recognised by parameter
  names. `TimeSpan * double` and `/ double` round ticks to even like .NET.
* Formatting follows the invariant culture: `TimeSpan` `c`/`g`/`G` and custom formats
  (`hh\:mm\:ss`); `DateTime` standard formats (`d D f F g G M Y t T s u U o R`) and custom formats
  (`yyyy-MM-dd HH:mm:ss.fff`, `dddd`, `MMMM`, `tt`, `F` trimming that drops a dangling `.`).
  `K` writes `Z` for Utc, the local offset for Local and nothing for Unspecified; `z`/`zz`/`zzz` write
  `+00:00` for Utc and the local offset otherwise (a bare time of day below one day gets today's
  offset, even for Utc, as in .NET); `o`/`O` end with `K`; `U` converts to universal time first, while
  `r` and `u` format the value as it is.
* `Stopwatch` is a runtime object on `CLOCK_MONOTONIC`; `Stopwatch.Frequency` is 1 000 000 000
  (so its ticks are nanoseconds, as on Linux .NET). `ToString()` returns `Elapsed.ToString()`.

## System.Type (reflection-lite)

* One **canonical Type object per method table**, cached in a `.bss` table that the
  conservative GC scans. So `typeof(X) == obj.GetType()` is a pointer comparison.
* `obj.GetType()` is static for value-type receivers and reads the object's method table
  otherwise. `Name`, `FullName`, `Namespace` and `ToString()` derive from the MT's CLR name
  (`"Program+Nested"`, `"MyNs.Point"`, `"System.Int32[]"`).
* Every type gets an identifying method table: classes/structs reuse theirs, primitives use
  their boxed MTs, enums get a **boxed-enum MT** (also used for boxing enums, whose `ToString`
  formats by name), library types get a name-only MT, and arrays get an **element-typed array
  MT** stamped right after allocation, so `new int[3].GetType().Name == "Int32[]"` and
  `array.ToString() == "System.Int32[]"`. Arrays of reference-free elements carry
  `CSRT_MT_NO_REFERENCES`, which the GC uses to skip scanning them (replacing the earlier
  identity check against `csrt_PrimitiveArray_MT`).
* Not supported: members beyond names/identity (no `GetMethods`, attributes, `Activator`).

## BigInteger

A `BigInteger` value is a reference to an immutable `uint[]` (element 0 the sign, then the
magnitude; `null` is zero, so `default(BigInteger)` and zero-initialized arrays are correct). Schoolbook multiplication,
Knuth algorithm D for division (remainder takes the dividend's sign), exponentiation by squaring,
`ModPow`, `GreatestCommonDivisor`, `Parse/TryParse`, `ToString("D"/"N"/"X")`, all comparison
operators including the `(BigInteger, long)` overloads, implicit/explicit conversions, boxing.

## Array helpers and sorting

* `Array.Reverse/Copy/Clear/Fill/IndexOf/Resize/BinarySearch/Empty`, and `Exists/Find/FindIndex/
  TrueForAll/ForEach` with predicate delegates (IR loops calling the delegate).
* `Array.Sort` is a **port of .NET's `ArraySortHelper<T>.IntrospectiveSort`**
  (`src/runtime/bclmisc.c`), so the order of elements that compare equal matches the CLR exactly. It
  is not stable, and .NET isn't either. Comparers are generated in IR per element type:
  `Comparison<T>` delegates and `IComparable<T>` user types (`Comparer<T>.Default` semantics,
  nulls first; struct `CompareTo` receives the element's address). Primitive arrays use the
  type's natural order (`double`: NaN first).

## Culture-sensitive comparison

.NET uses ICU. With the invariant culture that is the CLDR root collation, so for example
`Array.Sort` gives `"apple" < "Banana"`, and punctuation sorts before digits, which sort before
letters. `csrt_string_compare_culture` approximates root collation for Latin text with three
levels: base letter (case- and accent-insensitive) < accent < case (lowercase first). Control
characters are ignorable. It backs `string.Compare`, `CompareTo`, `Array.Sort` and
`StringComparison.*Culture*`. `StringComparison.Ordinal(IgnoreCase)` and `==` stay ordinal.
Full UCA tables (non-Latin scripts, contractions) are future work.

## Other members

`BitOperations` (`ctpop`/`ctlz`/`cttz`/`fshl`/`fshr` intrinsics), primitive `CompareTo`/`Equals`
(`double.CompareTo` orders NaN first), `double.IsNaN/IsInfinity/IsFinite`, generic-math statics
(`double.Round`, `int.Max/Min/Clamp/Abs/Sign`, ...), `Math.Round(double, [digits,] MidpointRounding)`
(on the double's exact decimal value, matching .NET 11), `Convert.ToInt32/ToInt64/ToString`
with bases 2/8/10/16 and `Convert.ToInt32(double)` (banker's rounding), `bool.Parse`,
`char.GetNumericValue`, `Environment.ProcessorCount/TickCount/GetCommandLineArgs/Is64BitProcess`,
`StringBuilder.Insert/Remove/Replace/indexer/AppendJoin`, `string.Split(string|char[])`,
`string.Concat(string[])`, `string.IsNullOrWhiteSpace`, `object.ReferenceEquals`, `x as T`
returning null, and custom numeric formats with `positive;negative;zero` sections.
`CultureInfo.InvariantCulture`/`CurrentCulture` are accepted as placeholders: formatting and
parsing are always invariant.

## Known gaps

* `TimeZoneInfo`, `DateTimeOffset`, `DateOnly`/`TimeOnly`, `DateTime.Parse` (diagnosed as unsupported).
  Historical daylight-saving gaps take the zone's *current* base offset, which can differ from .NET
  where the standard offset has changed since.
* Decimal `G` with precision follows double-style rules only approximately; `decimal.Parse`
  does not accept exponents (NumberStyles.Number, as .NET's default).
* `Type` members other than names/identity; generic type names print as `List`1` without arguments.
* Full ICU collation (see above). `BigInteger` uses O(n²) multiplication and decimal conversion.
* `Array.Sort` of arrays of non-comparable reference types, or with `IComparer<T>` objects.
