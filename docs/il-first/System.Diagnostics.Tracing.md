# System.Diagnostics.Tracing

A CoreLib library served from the framework's IL: its row of the per-library table of [ADR 0023](../adr/0023-framework-il-first.md)
(columns explained there, under "Measurements"). Flipped by #423: the shadows `corelib/Tracing/EventSource.cs`, `EventListener.cs`,
`Counters.cs` and `SelfDescribing.cs` are deleted, and so is the compile-time event table they read (`ModuleCodeGen.EventSource.cs`'s
`EventsBlob`, `CoreIntrinsics.EventSourceAttribute`).

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| System.Diagnostics.Tracing (`src/Csharpc/upstream-types/System.Diagnostics.Tracing.txt`, #423): `EventSource` with the manifest, `WriteEvent*`, `Write<T>` (TraceLogging), `GenerateManifest`, `SendCommand`, `GetSources`; `EventListener` and its event args; `EventCounter`, `IncrementingEventCounter`, `PollingCounter`, `IncrementingPollingCounter` with the counters' polling thread; the attributes | the tracing samples (446, 447, 1040 to 1044, 1210, 1211, 846) and the two new ones byte-identical to real .NET at -O0 and -O2 (24 runs) and via IL (12); `tests/size.sh` 12 of 12, none grown (hello 28 544 B, exceptions 36 928 and typeinfo 43 784 unchanged, async 266 128 to 266 032, json 1 661 656 to 1 661 224); `tests/slicer.sh` (see the commit). New samples, recorded from .NET: 1470 (event counters delivered to a listener: the four counter kinds' payloads, metadata, the polling thread), **silently different on the shadow** (`EventCounter.ToString` printed `Mean NaN`, and no counter payload ever arrived); 1471 (`GenerateManifest` with keywords, tasks, opcodes, enum maps and the string table, the Strict errors, `SendCommand`, `GetSources`), a compile error on the shadow (CSLLVM0002) | `tracing_throughput` 1 072 456 to 2 337 640 B (2.18x: the framework's manifest builder, TraceLogging's type analysis and the counters, the reflection metadata of the event source's methods; **NativeAOT 3 788 432**). Programs that never touch EventSource do not grow (above) | 22.9 / 47.7 s user (the framework's EventSource is 4 300 lines of IL against the shadow's 1 450) | `tracing_throughput`: median of 7 0.57 to 0.50 s, **4 524 M to 3 826 M instructions (0.85x)** | 1 579 (`corelib/Tracing/*`, the compile-time event blob); the whole change 1 642 deleted, 1 107 added | **From IL, flipped.** What the IL asks of the runtime is csharpc's (below): reflection over the event source class, EventPipe's QCalls as a disabled EventPipe, and four small contract members | `tracing_throughput`: 2 337 680 / 3 788 432 B; compile cpu 45.1 / 18.9 s; run **454 / 354 ms (1.28x)** (median of 3, loaded host; follow-up #454) |

## What stays csharpc's (the contract)

- **Reflection** (csharpc's own layer, ADR 0018). `EventSource` carries `[DynamicallyAccessedMembers(PublicMethods | NonPublicMethods |
  PublicNestedTypes)]` on the class; csharpc now applies a class-level annotation to every class derived from it, as ILLink and NativeAOT
  do (`ModuleCodeGen.NoteAnnotatedHierarchy`), and keeps the enum types of the kept methods' parameters with their fields (the manifest
  maps them). `Type.GetNestedType(s)` is new: the nested types reaching a `PublicNestedTypes` location are kept whole
  (`ModuleCodeGen.NestedTypes.cs`, `corelib/Reflection/NestedTypes.cs`); for any other type it stays an explicit NotSupportedException.
  `Type.FindInterfaces` is new. The slicer now copies `[DynamicallyAccessedMembers]`, `[DynamicDependency]` (generic parameters included)
  and TraceLogging's `[EventData]`/`[EventField]`/`[EventIgnore]`, keeps an `[EventData]` type's property accessors and what a
  `[DynamicDependency(PublicProperties)]` names; the slice's own types answer the framework's attribute queries with the attributes kept.
- **EventPipe** (`corelib/UpstreamTracing.cs`): CoreLib's QCalls bind by name to C# exports, like the libSystem.Native contract. No session
  ever exists (`Enable` answers 0, which the framework's dispatcher takes as "not enabled"), events are never defined or written,
  `CreateProvider` answers a handle that is never dereferenced, and the thread's activity id is kept per thread with
  `eventpipeinternal.cpp`'s semantics. A QCall without a contract is now a stub that ends the program with the "not supported by csharpc
  yet" diagnostic, never a dlopen of "QCall".
- **Contract members** (slicer profile): `GetHelperCallFirstArg` answers -1 (NativeAOT's CoreLib has no IL to read for the WriteEvent id
  check); `InitializeDefaultEventSources` does nothing (below); LTTng's persistent listener is null (CoreCLR-only); TraceLogging's
  `IsGenericMatch` does not ask a non-public type for its definition; its reference-type property getter reads through
  `PropertyInfo.GetValue` (no `MakeGenericType`/`CreateDelegate` at run time), wrapped so a null container is the delegate's
  NullReferenceException.

Fixes this flip needed outside tracing, each general: the slicer wrote a type reference nested in a type definition of its own
module (dead code naming a nested type left outside the slice), which made the writer give the next type's property map the wrong
parent (EventSource lost all its properties); class-hierarchy devirtualization ignored program classes deriving from a type the slice
serves (a program `EventListener` was missing from the subtree, so the framework's `OnEventSourceCreated` call went to
`XplatEventLogger`'s override); unions of reference-free structs (TraceLogging's `PropertyValue.Scalar` overlays Guid, DateTimeOffset
and decimal); slice types are named as the BCL names them (an InvalidCastException to `System.Diagnostics.Tracing.EventPayload`); the
slice counts as framework IL for attribute queries; `ResourceManager` creates non-string resource objects (date, time-span and Guid
parsing, the TypeConverters) only in a program that has such a resource (`CoreIntrinsics.HasNonStringResources`, folded like a feature
switch), which is what kept the manifest builder's `ResourceManager` from linking System.ComponentModel.

## Differences from NativeAOT (explicit, or the runtime's)

The probe of 2026-11-20 (a listener created at start, `EventSourceSupport=true`): .NET and NativeAOT both show
`Microsoft-Windows-DotNETRuntime`, `System.Diagnostics.Eventing.FrameworkEventSource`, `System.Buffers.ArrayPoolEventSource`,
`System.Runtime` and `System.Diagnostics.Metrics` (later `TplEventSource`), deliver the runtime's GC events through EventPipe and the 27
`System.Runtime` counters. These are the runtime's own sources, events and counters (GC, JIT, thread pool, contention), which csharpc's
runtime does not produce: a csharpc listener sees the program's own sources only, as it did with the shadow. Moving that boundary is the
owner's decision (#455). Out-of-process sessions (EventPipe, the diagnostics port, LTTng, ETW on Linux) do not exist.

## Security review

- **Less attack surface than NativeAOT with `EventSourceSupport=true`**: no EventPipe, no diagnostics IPC socket (`DOTNET_DiagnosticPorts`),
  no LTTng listener driven by environment variables; nothing outside the process can enable a source or read events.
- **Native boundary**: the only native writes the contract makes are the activity id through the caller's `Guid*`, null-checked;
  every other export ignores its pointers. `WriteEventCore`'s `EventData` pointers are the caller's, read by the framework's IL exactly as
  in .NET (an unsafe API by design; the shadow read them the same way).
- **Text the framework builds**: the manifest XML is the framework's ManifestBuilder (escaping, string table), not csharpc's.
- **Reflection exposure**: an event source class's methods, nested types and its enum parameters' fields are visible to reflection, as
  under NativeAOT; nothing else is kept for it. The polling thread is the framework's background thread.
