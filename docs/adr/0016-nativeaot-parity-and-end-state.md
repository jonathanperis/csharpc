# ADR 0016 — NativeAOT parity as the support target, csharpc as dotnet's AOT backend as the end state

- **Status:** Accepted (2026-10-24). Direction for the roadmap; the end state is a plan, not code yet.
- **Context:** [06-roadmap.md](../06-roadmap.md) (Standing goal, M4, M6, M7),
  [08-drop-in.md](../08-drop-in.md), [ADR 0006](0006-closed-world-compilation.md),
  [ADR 0010](0010-il-frontend.md), [ADR 0013](0013-framework-il.md).

## Context

"Drop-in replacement for the .NET toolchain" needs a measurable definition. csharpc is an
ahead-of-time compiler for a closed world: it can't do what only a JIT can (run-time code generation),
and nobody publishing a native executable today expects that either — the toolchain they compare
against is .NET's own AOT path, `dotnet publish -p:PublishAot=true` (NativeAOT, the ILCompiler).

## Decision

1. **Support target: 100 % parity with NativeAOT.** Every program and library that works when
   published with NativeAOT must compile with csharpc and behave identically (stdout, exit code,
   exception types and messages). What NativeAOT can't do is out of scope and stays an explicit
   diagnostic, never a guess: `Reflection.Emit`, `MakeGenericType`/`MakeGenericMethod` over
   instantiations the compiler never saw, compiled expression trees, `dynamic` (the DLR),
   `Assembly.Load*` of code not in the closed world.
2. **The JIT is the oracle and a benchmark baseline, not a support target.** Tests keep recording
   expectations with `dotnet run` (where NativeAOT and the JIT agree, which is everywhere in scope);
   benchmarks compare against both the JIT and NativeAOT; compile time is compared against
   `dotnet publish -p:PublishAot=true`.
3. **The compatibility metric becomes NativeAOT parity** (M4): the harness also publishes each corpus
   program with NativeAOT; programs NativeAOT can't build or run identically leave the denominator,
   and the headline number is the share of NativeAOT-working programs csharpc matches.
4. **End state: csharpc as the AOT backend of a fork of dotnet.** In the dotnet SDK, `PublishAot=true`
   hands the IL assemblies `csc` produced (plus the framework's) to ILCompiler through
   `Microsoft.DotNet.ILCompiler`'s MSBuild targets and an `.ilc.rsp` response file. The end state is a
   dotnet fork in which that step runs csharpc instead — same inputs, same MSBuild properties, same
   outputs — so every existing project gets csharpc's code by flipping the AOT backend, not by
   learning a new tool.

## Consequences

- **Parameter parity.** `Csharpc.Sdk` (and later the fork) must honour the properties that steer
  NativeAOT, with NativeAOT's names and semantics: `PublishAot`, `RuntimeIdentifier`,
  `OptimizationPreference` / `IlcOptimizationPreference` (Speed/Size), `IlcInstructionSet`,
  `InvariantGlobalization`, `UseSystemResourceKeys`, `StackTraceSupport` /
  `IlcGenerateStackTraceData`, `StripSymbols`, `NativeLib` (Static/Shared libraries with
  `UnmanagedCallersOnly` exports), `DirectPInvoke` / `NativeLibrary` items, `TrimMode`, trimming
  descriptors (`ILLink` XML, `rd.xml`, `DynamicDependency`, `DynamicallyAccessedMembers`) and the
  trim/AOT analysis warnings (`IL2xxx`/`IL3xxx`) — reported where csharpc's closed-world analysis can
  prove the same thing, `AutoreleasePoolSupport`, `EventSourceSupport` and the other feature switches
  (runtimeconfig `RuntimeHostConfigurationOption`s, which csharpc already reads).
  A property csharpc doesn't honour yet is a warning naming it, not silently ignored.
- **The IL front-end becomes the primary path in the end state.** Inside the dotnet pipeline the
  input is IL from `csc`, not sources. csharpc's source front-end (Roslyn CFG) stays the best path for
  `csharpc file.cs` and for debug info today, but every feature must also work through the IL
  front-end ([ADR 0010](0010-il-frontend.md)); the corelib already goes that way
  ([ADR 0015](0015-corelib-il.md)). Gaps between the two front-ends are bugs.
- **Output parity.** What NativeAOT produces next to the executable (the `.dbg`/`.pdb` symbol file
  split by `StripSymbols`, `.map` with `IlcGenerateMapFile`, static/shared libraries for `NativeLib`)
  becomes part of the drop-in contract.
- **Out-of-scope list is NativeAOT's.** The roadmap's ❌ status means exactly "NativeAOT doesn't
  support it either"; anything NativeAOT supports is ⬜/🟡/🚧 until it's ✅.
- **Definition of done (roadmap M8):** the fork, with NativeAOT's code removed and csharpc in its
  place, is built and used to compile the ASP.NET Core TechEmpower Framework Benchmarks entries that
  .NET runs with NativeAOT; they run in the TechEmpower environment and beat the NativeAOT-compiled
  entries. That makes ASP.NET Core/Kestrel from IL and server-grade throughput (thread pool, sockets,
  epoll, GC under load) requirements, measured early by a local NativeAOT-vs-csharpc load harness.
- **The fork is the last step, not the next one.** It needs M5/M6 done (IL as the primary input,
  csharpc compiling Roslyn and itself) and parity on the compatibility corpus; until then
  `Csharpc.Sdk` is the integration point and must track the same property names.
