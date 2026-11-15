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
| Culture, number formatting and parsing (deletes the digits bridge) | planned (#426) | #426 |
| DateTime, calendars, TimeSpan parsing and formatting | to do | |
| Text and encoding, Convert, string helpers, hashing, BigInteger, floating-point math | to do | |
| Tracing | to do | |
| IO above the libc boundary | part 1 (streams, readers, writers: [System.IO.md](System.IO.md)) done; part 2 (the file system over a C# libSystem.Native contract) in progress | #315 |
| Net/Http above csharpc's socket engine | to do | |
| Crypto, X.509, SslStream (the OS-native TLS default stays the owner's call, #143, #144) | to do | |
