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
| Decimal arithmetic | `runtime/decimal.c` |
| TimeSpan, DateTime, Stopwatch | `runtime/datetime.c` |
| `System.Type` objects | `runtime/type.c` |
| BigInteger | `runtime/bigint.c` |
| Array helpers, introsort, Convert, Environment, collation, StringBuilder extras | `runtime/bclmisc.c` |
| Decimal / custom-section number formatting | `runtime/format.c` |

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
* **Arithmetic** (`runtime/decimal.c`) computes the *exact* result with a fixed 384-bit bignum,
  then fits it into 96 bits / scale ≤ 28 by dividing by a power of ten with round-half-to-even,
  which is the CLR's contract. Scale rules follow .NET: `+`/`-` keep the larger scale, `*` adds
  scales, `/` produces the smallest scale that represents the quotient exactly (up to 28 digits),
  `%` keeps the larger scale and the dividend's sign. Overflow throws `OverflowException`
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

* `TimeSpan` and `DateTime` are lowered to their **tick count** (`i64`, 100 ns units).
  `DateTime.Kind` is not modelled (always Unspecified); `DateTime.Now` uses the local UTC offset.
* Calendar math is the CLR's own algorithm (proleptic Gregorian, years 1–9999):
  `AddMonths` clamps the day, `AddDays(double)` adds integral and fractional parts separately
  (.NET 7+ behaviour), range errors throw `ArgumentOutOfRangeException` with .NET's messages.
* `TimeSpan.FromX(double)` truncates exact ticks (.NET 7+); the .NET 9 integer overloads
  (`FromSeconds(long)`, `FromHours(int, long minutes = 0, ...)`) are recognised by parameter
  names. `TimeSpan * double` and `/ double` round ticks to even like .NET.
* Formatting follows the invariant culture: `TimeSpan` `c`/`g`/`G` and custom formats
  (`hh\:mm\:ss`); `DateTime` standard formats (`d D f F g G M Y t T s u o R`) and custom formats
  (`yyyy-MM-dd HH:mm:ss.fff`, `dddd`, `MMMM`, `tt`, `F` trimming that drops a dangling `.`).
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

A `BigInteger` value is a pointer to an immutable GC-heap bignum (`null` is zero, so
`default(BigInteger)` and zero-initialized arrays are correct). Schoolbook multiplication,
Knuth algorithm D for division (remainder takes the dividend's sign), exponentiation by squaring,
`ModPow`, `GreatestCommonDivisor`, `Parse/TryParse`, `ToString("D"/"N"/"X")`, all comparison
operators including the `(BigInteger, long)` overloads, implicit/explicit conversions, boxing.

## Array helpers and sorting

* `Array.Reverse/Copy/Clear/Fill/IndexOf/Resize/BinarySearch/Empty`, and `Exists/Find/FindIndex/
  TrueForAll/ForEach` with predicate delegates (IR loops calling the delegate).
* `Array.Sort` is a **port of .NET's `ArraySortHelper<T>.IntrospectiveSort`**
  (`runtime/bclmisc.c`), so the order of elements that compare equal matches the CLR exactly. It
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

* `DateTimeKind`/time zones, `DateTimeOffset`, `DateOnly`/`TimeOnly`, `DateTime.Parse`.
* Decimal `G` with precision follows double-style rules only approximately; `decimal.Parse`
  does not accept exponents (NumberStyles.Number, as .NET's default).
* `Type` members other than names/identity; generic type names print as `List`1` without arguments.
* Full ICU collation (see above). `BigInteger` uses O(n²) multiplication and decimal conversion.
* `Array.Sort` of arrays of non-comparable reference types, or with `IComparer<T>` objects.
