# ADR 0008 — Move the library half of the runtime to the C# corelib

- **Status:** Accepted (2026-10-22). Follows [ADR 0007](0007-runtime-in-c-first.md).

## Context

ADR 0007 started the runtime (csrt) in C because the compiler couldn't yet compile a corelib. That's
no longer true: generics, structs, `ref struct`s, spans, `stackalloc`, exceptions and interfaces all
work, and `src/Csharpc/corelib` already holds the collections and LINQ. csrt still contained a lot of
plain library code, though: number formatting and parsing, string operations, `StringBuilder`, char
classification. That code has several costs:

- **It's opaque to the optimizer.** Even with runtime LTO, the C helpers were written as generic
  entry points (`csrt_format_f64(v, fmt, single)`) that parse their format at run time. C# corelib
  code is lowered per call site with the rest of the program, so constant format strings and
  arguments propagate into it.
- **It isn't memory safe.** Fixed `char[64]` buffers, `snprintf` truncation and manual
  realloc-based buffers are exactly the bugs this project is supposed to avoid. Corelib code is
  bounds-checked C#.
- **It doesn't match .NET.** It went through libc (`printf("%.17e")`/`strtod` loops), which only
  approximates .NET's algorithms. Examples: the scientific notation threshold of `double.ToString()`,
  custom formats seeing 15 significant digits, half-up rounding for integers and decimals, `"-0"`
  in custom formats, `Math.Round(1.115, 2)`, and the `byte`/`sbyte` overflow messages.
- **It goes against the project goal** of writing as much of the implementation as possible in C#.

## Decision

**What stays in C (`src/runtime`):** what has to be native or sits below the object model: the
GC and allocator, exception unwinding (personality routine, throw/catch ABI, exception messages),
threads and synchronization, the async scheduler core, console and OS syscalls, process startup,
type-system primitives (casts, interface dispatch tables, boxing method tables), and what hasn't
moved yet: culture collation and the remaining string helpers in `bclmisc.c`, plus `Enum.Parse`
(`parse.c`).

**What moves to C# (`src/Csharpc/corelib`):** library logic that is ordinary C#:

| Area | Corelib file | Replaced C |
|---|---|---|
| Number formatting: shortest round-trip (`R`/`G`/`ToString()`), `C D E F G N P R X B`, custom formats (sections, quotes, `%`, `‰`, `E+00`), hex floats, composite formatting, alignment, decimals, Int128 | `Number.Formatting.cs`, `Number.cs` | `format.c`, `format_real` + `csrt_string_from_f64/f32` in `csrt.c`, `csrt_format_i128` |
| Number parsing: `int/long/double.Parse/TryParse` with `NumberStyles`, correctly rounded decimal→double, `Math.Round(double, digits, mode)` | `Number.Parsing.cs` | `parse.c` (number part), parsers in `strings.c`, `csrt_math_round_*` |
| Arbitrary-precision helper for the above | `BigNum.cs` | (glibc's printf/strtod internals) |
| String operations (`Substring`, `IndexOf`, `Replace`, `Split`, `Trim`, `Pad`, case mapping, ...) | `StringOps.cs` | string functions in `strings.c`, `bclmisc.c` |
| `char` classification and invariant case mapping, with Unicode tables generated from .NET | `StringOps.cs`, `UnicodeData.g.cs` (`tools/unicode`) | Latin-1 tables in `strings.c` |
| `System.Text.StringBuilder` | `StringBuilder.cs` | `csrt_sb_*` in `strings.c`, `bclmisc.c` |
| Integer → string | `Number.cs` | `csrt_string_from_i32/...` call sites (C keeps a small itoa for its own use) |
| `System.Decimal`: arithmetic with exact 96-bit rounding, conversions, parsing, hashing | `DecimalOps.cs` | `decimal.c` |
| `System.Numerics.BigInteger`: Knuth division, `Pow`/`ModPow`/`GCD`, conversions, parsing, formatting | `BigIntegerOps.cs` | `bigint.c` |
| `TimeSpan`/`DateTime`: arithmetic, the CLR calendar, standard and custom formats | `DateTimeOps.cs` | `datetime.c` (the clocks stay in C) |

### Mechanisms

- **`CoreLibRuntime`** (`CodeGen/CoreLibRuntime.cs`) maps a former C entry point name to a corelib
  method. `CallRuntime("csrt_format_f64", ...)` transparently calls `Number.FormatDouble`, so moving a
  function never touches its call sites.
- **`[ShadowInstance]`**: a static corelib method whose first parameter is the receiver implements
  a BCL *instance* method (`string.Replace`, `string.Split`, ...) without a shadow type.
- **Core intrinsics** for the few things C# can't express without `unsafe`: `FastAllocateString`,
  `StringChars` (a writable span over a fresh string), double/float bit casts, reading and building
  decimal fields.
- **`[RuntimeExport("csrt_symbol", "trigger")]`** for the reverse direction, where C calls C#.
  Example: a boxed double's virtual `ToString` lives in a C method table. The compiler emits an
  external wrapper named `csrt_symbol` only when the program references `trigger` (here
  `csrt_Boxed_Double_MT`). The C side declares a weak fallback that is never reached, so programs
  that don't box doubles don't compile or link the formatter.

## Consequences

- C shrank from 7,286 to 4,727 lines, and the corelib grew from 5,500 to 9,698 lines.
- Formatting and parsing follow .NET's own algorithms: exact digits with ties-to-even for doubles,
  Steele & White / Burger & Dybvig shortest digits, .NET's `NumberToString`/`NumberToStringFormat`
  structure. They're verified differentially (`tests/samples/160`–`166`, including 20,000 random
  round-trips through `R` and `E16`). Matching .NET's own algorithms also fixed several
  divergences from .NET in the old C code:
  - the scientific-notation threshold of `double.ToString()`;
  - `Math.Round(1.115, 2)`;
  - decimal `%` scales and `(decimal)double` digits;
  - .NET 11's hex-float `"X"` format;
  - boxed `DateTime`/`TimeSpan`/`IFormattable` values in composite formats;
  - the overflow messages for `byte`, `sbyte` and `char`.
- No fixed-size C buffers remain in these paths. BigNum storage is `stackalloc` with bounds
  checks.
- Code that doesn't use formatting pays nothing: corelib code is compiled on demand and
  dead-stripped. Code that does use it gets it inlined and specialized.
- Each compile does a little more work when a program uses formatting, because the corelib
  formatter goes through Roslyn and codegen instead of being precompiled bitcode.
- Corelib code must stay inside the subset csharpc compiles well and must not need `unsafe`, since
  user projects may disable it. Hence the intrinsics above.

## Alternatives considered

- **Keep C and fix the divergences there:** that leaves the safety and optimizer problems, and a
  correctly rounded `strtod`/shortest printer in C is no smaller than in C#.
- **Ship .NET's own `System.Private.CoreLib` sources:** it depends on the CoreCLR type system,
  generic virtual methods, reflection and globalization data far beyond what csharpc implements.
  Selective ports of its algorithms (as done here) give the same results.
