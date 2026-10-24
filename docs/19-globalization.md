# 19 — Globalization (cultures, ICU)

csharpc programs honour cultures exactly like .NET: the same current culture, the same number, date
and currency formats, the same parsing rules, the same collation and casing. As in .NET on Linux and
macOS (and on Windows 10 1903+), the culture data and algorithms come from **ICU**, loaded from the
machine at run time; csharpc ships no ICU data.

## Architecture

The implementation is .NET's own, ported (dotnet/runtime release/11.0, MIT; see
`THIRD-PARTY-NOTICES.md`):

| Layer | Where | Upstream |
|---|---|---|
| Managed globalization model | `src/Csharpc/corelib/Globalization/` | `System.Private.CoreLib/src/System/Globalization`: `CultureInfo`, `CultureData` (ICU path), `NumberFormatInfo`, `DateTimeFormatInfo` (+ scanner), `DateTimeFormat`, `DateTimeParse`, all calendars, `CalendarData`, `CompareInfo`, `TextInfo`, `RegionInfo`, `GlobalizationMode`, `IcuLocaleData`, ... |
| P/Invoke declarations | `corelib/Globalization/Interop.Globalization.cs` | `Common/src/Interop/Interop.*.cs`, as `[DllImport("__Internal")]` into the shim |
| Native shim | `src/runtime/globalization/` (compiled into the runtime, `csrt`) | `src/native/libs/System.Globalization.Native` |
| Number layout and parsing | `corelib/Number.Formatting.cs`, `Number.Parsing.Culture.cs` | `Number.Formatting.Common.cs` (`NumberToString`, `NumberToStringFormat`), `Number.Parsing.cs` (`TryParseNumber`) |
| Culture-sensitive string operations | `corelib/Globalization/CultureStrings.cs` | `String.Comparison.cs`, `CompareInfo` dispatch |

The port keeps the upstream code nearly verbatim. Mechanical adaptations (scripted): namespace
`CsharpcCoreLib.Globalization` with `[Shadows("System.Globalization.X")]`, NLS (Windows-only) branches
and browser/mobile `#if`s removed, generic `TChar` code specialized to `char`, collection expressions
and static field initializers rewritten into lazily created fields (corelib types get no static
constructor, and eagerly initialized statics would run at startup), `InlineArray` replaced by fields.

The shim is compiled **headerless** on every target: it declares the ICU functions it uses itself
(`pal_icushim_internal_android.h`, upstream's declarations extracted from the ICU headers) and binds them
at run time with `dlopen`/`dlsym`, probing versioned library names exactly like .NET
(`libicuuc.so.NN` / `libicui18n.so.NN` from the newest version down; `DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU`
for an app-local ICU). macOS uses `/usr/lib/libicucore.dylib`, Windows `icu.dll` (Windows 10 1903+).
No ICU headers or development packages are needed to build csharpc or its programs.

ICU is loaded **lazily**, on the first operation that needs culture data (.NET loads it at startup).
A program that never formats, parses, compares or cases culture-sensitively doesn't touch ICU, and the
globalization code isn't linked into it at all.

## Which culture is current

As in .NET:

* **Linux:** ICU's default locale, which ICU derives from `LC_ALL`, then `LC_MESSAGES`, then `LANG`
  (`pt_BR.UTF-8` → `pt-BR`). `C` and `POSIX` (or no setting) give the **invariant culture**.
* **macOS:** the user's locale from the system settings (`CFLocale`, `language-country`), as .NET's
  `NSLocale` lookup; the environment variables do not matter.
* **Windows:** the user's locale (`GetLocaleInfoEx` / `GetUserPreferredUILanguages` for the UI culture).
  As in .NET with ICU, the user default culture takes its number, currency, list-separator, time,
  date-pattern, first-day-of-week and calendar settings from NLS (`GetLocaleInfoEx`,
  `GetCalendarInfoEx`), where Windows keeps the user's regional settings, and everything else from ICU
  (`CultureData.Windows.cs`, `CalendarData.Windows.cs`). en-US therefore formats `-0.75m` as `($0.75)`
  on Windows and `-$0.75` elsewhere, exactly like .NET.
* `CultureInfo.CurrentCulture` / `CurrentUICulture` can be set per thread, and
  `DefaultThreadCurrentCulture` / `DefaultThreadCurrentUICulture` apply to threads that never set one.

The current culture **flows with the execution context** like in .NET (where it is kept in an
`AsyncLocal`): a new `Thread`, thread-pool work items (`Task.Run`, `ThreadPool.QueueUserWorkItem`) and
`await` continuations run with the culture of the code that started them, and setting the culture
inside an `async` method doesn't leak to its caller after the first `await`. csharpc implements this
with a per-thread flow object (`csrt_tl_flow`, `src/runtime/threads.c`), captured when a thread is
started, a work item is queued or a continuation is registered (`src/runtime/async.c`), and restored
around async method bodies (`FunctionCodeGen.Async.cs`).

On **WASI** (`--target wasm32-wasip1`) nothing can be loaded dynamically, so there is no ICU: programs
run in invariant globalization mode, as .NET's WebAssembly builds do by default. The shim isn't built
there (the `Interop.Globalization` imports are stubs). One deliberate difference: linguistic comparisons
approximate ICU's root collation for Latin text (`csrt_root_collation_compare`, `src/runtime/bclmisc.c`)
instead of becoming ordinal, so sorting agrees with .NET on Linux, which the tests are recorded with.

## Invariant mode

`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (or `true`) at run time, or the project property
`<InvariantGlobalization>true</InvariantGlobalization>` (also `#:property InvariantGlobalization=true` in a
file-based app) at compile time, select invariant mode exactly like .NET: no ICU, every culture behaves
like the invariant culture, comparisons are ordinal. The lookup order is .NET's
(`AppContextConfigHelper.GetBooleanConfig`): the environment variable, then the
`System.Globalization.Invariant` switch of the runtimeconfig (the project's RuntimeHostConfigurationOption
items, compiled into the program, or `AppContext.SetSwitch` before the first culture use), then the
project property, also compiled in (corelib define `CSHARPC_INVARIANT_GLOBALIZATION`, which covers
file-based apps' `#:property`). `PredefinedCulturesOnly` works the same way
(`DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY`).

Without ICU and without invariant mode, the program stops with .NET's message ("Couldn't find a valid
ICU package installed on the system. ...") and exit status 134. Difference: .NET fails at startup,
csharpc at the first culture-sensitive operation (a program that never needs ICU runs).

## Size and startup

Measured on Linux x86-64, `-O2`, stripped, ICU 74:

| Program | Before (invariant-only corelib) | With globalization |
|---|---:|---:|
| `Console.WriteLine("Hello, World!")` | 28,440 B | 28,440 B |
| prints a `double`, an `int` and `{d:N2}` | 36,792 B | 237,032 B |
| formats a `DateTime` and compares strings | 43,984 B | 373,496 B |

A program that formats anything culture-sensitively links the culture model once (`CultureInfo` /
`CultureData`, `NumberFormatInfo`, the ICU shim); dates add `DateTimeFormatInfo`, the calendars and
the date formatter. Three mechanisms keep the rest out:

* vtable pruning (`ModuleCodeGen.VTables.cs`) links only virtual methods that some call site
  dispatches to, so `CompareInfo`, `TextInfo` and calendar methods a program never uses are not compiled;
* composite formatting reaches the date formatter through the runtime (`csrt_format_boxed_ticks`), and
  `CultureInfo.GetFormat` reaches `DateTimeFormatInfo` the same way (`csrt_culture_format_dtfi`); their
  C# implementations are runtime exports compiled only into programs that box a `DateTime`/`TimeSpan`
  or use `DateTimeFormatInfo`;
* arrays of numeric constants (the culture tables of `IcuLocaleData`) are initialized from a constant
  table instead of one store per element.

Startup (average of 50 runs, same machine): hello world 2 ms before and after; the two culture programs
about 2.3 → 6.6 ms and 2.5 → 7.1 ms. The difference is loading ICU (`dlopen` of libicuuc/libicui18n,
binding ~150 functions) and building the current culture's data on first use, as .NET does at startup.

Compile time (`-O2`): hello world is unchanged (about 3 s); the number program takes about 10 s instead
of 6 s (it lowers some 420 corelib methods of the culture model), the date program about 13 s. This is
the main cost of the port (CI runs about 1.5-2 times longer) and a target for caching the corelib's
lowered code.

## Tests

* The recorded expectations come from Linux in the C locale (invariant culture). `tests/platform.sh`
  sets `LC_ALL=C.UTF-8` on Linux for both real .NET and csharpc; on macOS and Windows real .NET runs with
  `tests/oracle-hook` and csharpc programs are compiled with `tests/culture-pin/CulturePin.cs` (a
  `[ModuleInitializer]` that makes the invariant culture current).
* A sample with a `NAME.env` file (e.g. `LC_ALL=pt_BR.UTF-8`) runs both sides under that environment
  instead: `366_culture_from_environment`, `367_culture_invariant_mode`.
* Samples 360–376 cover number/date formatting and parsing in several cultures, collation, casing
  (Turkish `i`), current-culture rules, threads and async flow, `[ModuleInitializer]`,
  `FormattableString`, exact `float` parsing and boxed dates in composite formats (plus two
  codegen regressions the culture fuzzer found: 374, 375). They were recorded with ICU 74; the ones
  whose output depends on culture data are `live:` entries of `tests/platform-skips.txt` on macOS and
  Windows (another ICU), compared against real .NET on the same machine.
* `tools/fuzz/corelib.sh` has a `culture` mode: random numbers, dates, format strings, parse inputs,
  comparisons and casing in random cultures, against real .NET.

## Not supported yet

* NLS globalization on Windows (no `icu.dll`, or `DOTNET_SYSTEM_GLOBALIZATION_USENLS`): the program
  stops with a "not supported by csharpc yet" message.
* Windows: the Hijri date adjustment from the registry (`AddHijriDate`) is not read.
* `char.GetUnicodeCategory` / `CharUnicodeInfo` for supplementary-plane code points (throws
  `NotSupportedException`).
* `StringBuilder.Append(IFormatProvider, $"...")` (compile-time diagnostic).
* `TimeSpan.Parse` with a culture other than the invariant one; hexadecimal floating-point parsing.
* `float.Parse` parses as `double` and narrows, which can differ from .NET in the last bit for inputs
  that lie extremely close to a midpoint between two `float` values (double rounding).
* Setting `CurrentCulture` of a thread other than the current one before it starts.
