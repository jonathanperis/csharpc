# ADR 0013 — Framework assemblies from the shared framework's IL (`--framework-il`)

- **Status:** Accepted (2026-10-24), implemented (`src/Csharpc/Frontend/FrameworkIl.cs`).

## Context

csharpc serves the BCL in two ways: corelib shadows and intrinsics for what `System.Private.CoreLib`
and a few libraries implement (collections, LINQ, text, JSON, regex), and, since the IL front-end
([ADR 0010](0010-il-frontend.md)), method bodies decoded from IL. Until now a framework assembly
reached the IL front-end only when a project referenced its implementation by hand
(`<Reference HintPath=".../shared/Microsoft.NETCore.App/<ver>/System.Collections.Immutable.dll">`).
M6 (csharpc and Roslyn compiled by csharpc) needs the leaf framework libraries Roslyn uses —
`System.Collections.Immutable`, `System.Reflection.Metadata`, and in .NET 11 the
`Microsoft.Extensions.*` abstractions that ship in `Microsoft.NETCore.App` — without writing them
again as shadows, and without every project spelling out shared-framework paths.

Not every framework assembly can be taken as IL:

- `System.Private.CoreLib` is the runtime's own code: FCalls, JIT intrinsics, the GC and type-system
  interface. It stays shadows and intrinsics.
- Facades (`System.Runtime`, `System.Collections`, `System.Text.Json` …) forward some or all of their
  types to other assemblies, mostly CoreLib. Binding against the implementation would resolve those
  forwards to an assembly the compilation doesn't have.
- Assemblies that P/Invoke into the runtime's native shims (`libSystem.Native`,
  `libSystem.Security.Cryptography.Native.*`, QCalls: `System.Console`, `System.Net.*`) would link
  against libraries a csharpc executable doesn't ship.
- Assemblies with a type csharpc already implements (a `[Shadows]`/`[ShadowMembers]` corelib class or
  an intrinsic: `System.Linq`, `System.Runtime.Numerics`) would get two implementations of one type.

## Decision

**A framework assembly is compiled from the shared framework's IL when the program uses it and csharpc
can take it as IL; `--framework-il` overrides the choice.**

1. *Eligible:* an implementation assembly of `Microsoft.NETCore.App` (the version csharpc binds
   against) that is not `System.Private.*`, forwards no type into `System.Private.*` (a facade of
   CoreLib; forwards to other assemblies are followed by Roslyn), calls no runtime native shim and
   has no top-level type csharpc implements itself (shadow names read from the shipped corelib
   sources, intrinsic keys from `Intrinsics`/`BclValueTypes`). With the .NET 11 RC2 framework 50 of
   181 assemblies are eligible, among them System.Collections.Immutable, System.Reflection.Metadata,
   System.Formats.Asn1, System.IO.Pipelines, System.Threading.Channels, System.Linq.Expressions and
   all the Microsoft.Extensions abstractions.
2. *Used:* the sources name one of its namespaces (every dot-separated segment appears among the
   identifiers of the program's files and global usings) together with one of that namespace's
   public types, or one of its extension methods when no other framework assembly defines types in
   the namespace (`list.ToImmutableArray()` counts, `Append` in System.Linq does not); or an assembly
   compiled from IL (the program's `-r`/`<Reference>`/package assemblies, or a framework assembly
   chosen this way) references it — transitively, so System.Reflection.Metadata brings
   System.Collections.Immutable along.
3. Chosen assemblies join the IL references exactly like `-r`: Roslyn binds against the implementation
   instead of the reference-pack assembly of the same name, and only reachable methods are lowered.
4. Other shared frameworks a project references (`<FrameworkReference Include="Microsoft.AspNetCore.App">`,
   implicit in `Microsoft.NET.Sdk.Web`, or `--framework Microsoft.AspNetCore.App`) are indexed the same
   way, next to Microsoft.NETCore.App; Roslyn also binds against their reference packs (the SDK's implicit
   global usings name namespaces of assemblies a program may not use). Unknown framework references are
   an error. ([18-compatibility.md](../18-compatibility.md#aspnet-core-and-the-techempower-app).)
5. `--framework-il A,B` forces eligible assemblies in (an ineligible one is an error that says why);
   `--framework-il none` turns the automatic choice off; explicit `<Reference>`s keep working.
   `csharpc -v` prints each choice with its reason.

The scan is a token pass over the sources plus a cached index of the framework (namespaces, public
type and extension-method names, references and eligibility per assembly, ~160 KB in the csharpc
cache directory, one per framework directory, keyed on it and the csharpc build): ~30 ms per compile, ~180 ms
the first time a csharpc build sees a framework. Programs that use no eligible assembly — hello world,
everything the corelib covers — compile and link exactly as before.

### Why not the alternatives

- *Shadows for everything* (the M2 route) means rewriting libraries that already exist as correct,
  portable IL; for Roslyn's dependencies that is hundreds of thousands of lines.
- *A fixed allowlist* would need maintenance with every corelib change; deriving eligibility from
  what the corelib actually implements keeps the two in step, and the index makes it cheap.
- *Choosing by semantic binding* (bind once against the reference pack, collect the assemblies the
  program's symbols come from, rebind) is exact but binds the program twice. A false positive of the
  token test costs only metadata loading (nothing reachable is lowered), so precision isn't worth a
  second binding.

## Consequences

- Programs using System.Reflection.Metadata, System.Collections.Immutable or the
  Microsoft.Extensions abstractions compile with no project changes and print what `dotnet run`
  prints (`tests/il.sh`: IlMetadataApp, IlExtensionsApp, IlFrameworkApp; the `--framework-il` checks).
- The BCL surface those libraries call is what remains to do, and it is the same surface a source
  program calling the same APIs needs; failures stay CSLLVM0001/0002 diagnostics (or the CSLLVM0007
  stubs of ADR 0010 for code the program can't reach).
- When a later corelib shadow covers a type of an eligible assembly, that assembly stops being taken
  as IL automatically (the index is rebuilt for every csharpc build).

## Measurements (2026-10-24, shared 4-core VPS, csharpc on the JIT)

| Program | Framework IL | IL methods lowered | `-O0` wall / peak RSS | `-O2` wall / peak RSS | `-O2` binary |
|---|---|---|---|---|---|
| IlMetadataApp (SRM: headers, metadata tables, signatures, attributes, IL bodies, mmap) | System.Reflection.Metadata, System.Collections.Immutable | 1318 (+1407 corelib) | ~9 s / 257 MB | ~20 s / 314 MB | 771 KB |
| IlExtensionsApp (Primitives, Options, DI and Logging abstractions) | 4 Microsoft.Extensions + System.Linq.Expressions | 179 (+748 corelib) | ~7 s / 234 MB | ~11 s / 270 MB | — |

Per phase for IlMetadataApp (CPU seconds, `-v`): framework choice 0.03, IL load 0.02, Roslyn
parse + bind 2.5, lowering 4.0 (IL 1.0 of which decoding 0.06, corelib 2.1, program 0.45; 133 MB
allocated), LLVM: `-O0` code generation 1.2; `-O2` optimization 7.0 and code generation 4.7.

Hot spots for M6 step (3), compile-time scale:

1. Corelib methods cost twice as much per method as IL ones (2.1 s for 1407 vs 1.0 s for 1318):
   they are bound by Roslyn lazily inside lowering. Caching bound corelib bodies across compiles
   (or precompiling the corelib to IL and lowering it through the IL path) is the largest lever.
2. Roslyn's fixed cost (~2.5 s) is mostly parsing and binding the corelib for every program;
   csharpc published ReadyToRun roughly halves it (ADR 0010).
3. At `-O2` the single-threaded LLVM pipeline is 60 % of the compile; module partitioning for
   parallel code generation comes next.
4. Peak memory grows ~60 MB from the LLVM backend over the lowering peak; the managed side allocates
   ~130 MB for ~2700 functions, so a 40 MB-of-IL closed world needs streaming per-function emission
   (dropping IR builders and operation trees as soon as a function is lowered) rather than more RAM.
