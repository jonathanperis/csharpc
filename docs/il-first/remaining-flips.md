# Remaining flips (#423)

Every library area below moves from csharpc's own copy in `src/Csharpc/corelib` to the pinned .NET 11 GA
framework IL (ADR 0023), in one PR. Each area lands as its own commit series on `feat/flip-framework-il`,
checked locally first (build, `tests/size.sh`, touched samples and neighbours at -O0/-O2/via IL, wasm32 for
numeric, pointer-size or slicer changes). Each gets its own `docs/il-first/<library>.md` row with the NativeAOT
column, and the shadow files it replaces are deleted.

What stays csharpc's: the compiler, IL front-end, driver, toolchain, the C runtime (GC, exceptions, threads,
OS boundary) and the contract layer (reflection/type system, task/async glue, interop).

| Area | Status | Issue |
|---|---|---|
| ManualResetEventSlim, SpinWait, Barrier, other sync types | done locally: ManualResetEventSlim, SpinWait, CountdownEvent, Barrier, ReaderWriterLock, LockCookie from the framework (SemaphoreSlim in #419); docs/il-first/System.Threading.md | #414 |
| BFloat16, Decimal32/64/128 | done: `docs/il-first/System.Numerics.IeeeTypes.md` (also hosts every generic-math interface's static defaults; follow-ups #420, #425) | #405 |
| Hashtable, ArrayList, BitArray | done: `docs/il-first/System.Collections.Hashtable.md`, `System.Collections.ArrayList.md` (SR.Format contract for every slice) | #372, #373 |
| Guid | done locally: from the framework; docs/il-first/System.Guid.md | #416 |
| Culture, number formatting and parsing (deletes the digits bridge) | culture done locally: cultures, culture data, casing, collation, `CharUnicodeInfo`, `IdnMapping`, `StringInfo` from the framework (docs/il-first/System.Globalization.md); number formatting and parsing in progress on topic/flip-number | #426 |
| DateTime, calendars, TimeSpan parsing and formatting | done locally with culture: `DateTimeFormatInfo`, every calendar, `DateTimeFormat`, `DateTimeParse`, `TimeSpanFormat`/`TimeSpanParse`; DateOnly/TimeOnly/TimeSpan parsing hosted from IL; the corelib keeps those types' representation and their formatting members (TimeSpan's own formatter still the corelib's) | #426 |
| Text and encoding, Convert, string helpers, hashing, BigInteger, floating-point math | in progress: Ascii, Base64, CompositeFormat, StandardFormat ([System.Text.Ascii.md](System.Text.Ascii.md)) the Encoding family with Utf8 ([System.Text.Encoding.md](System.Text.Encoding.md)) Convert ([System.Convert.md](System.Convert.md)) and Rune ([System.Text.Rune.md](System.Text.Rune.md)) done; string helpers, BigInteger, floating-point math to do. Follow-ups #432, #433, #434, #435 | #423 |
| Tracing | to do | |
| IO above the libc boundary | done locally: part 1 (streams, readers, writers) and part 2 (`File`, `Directory`, `FileInfo`/`DirectoryInfo`, `FileStream`, `Path`, `RandomAccess`, `SafeFileHandle`, enumeration over the C# libSystem.Native contract `corelib/IO/SystemNative.cs`; security review and measurements in [System.IO.md](System.IO.md)); Windows (the Windows CoreLib slice over kernel32) waits for CI; linking the pack's libSystem.Native instead stays the owner's decision ([21-ownership.md](../21-ownership.md)) | #315 |
| Net/Http above csharpc's socket engine | to do | |
| Crypto, X.509, SslStream (the OS-native TLS default stays the owner's call, #143, #144) | to do | |
