# ADR 0015 — The corelib as a precompiled IL assembly

- **Status:** Accepted (2026-10-24), implemented (`src/Csharpc/Frontend/CoreLibAssembly.cs`; default
  `--corelib il`, `--corelib source` / `CSHARPC_CORELIB=source` keeps the previous pipeline).

## Context

The corelib (`src/Csharpc/corelib`, 173 files, ~4.3 MB of C#) is csharpc's BCL. Until now every
compile parsed all of it into the program's compilation, and lowering bound the bodies the program
reached through Roslyn (semantic model, operation tree, `ControlFlowGraph`). After
[ADR 0014](0014-compile-time.md) that was the largest remaining fixed and per-method cost: parsing was
~0.5 s of wall (~1 s of CPU) for hello world, and a corelib method cost about twice an IL method
(binding and CFG construction, partly on background threads). ADR 0010's IL front-end already lowers
method bodies from IL for Roslyn's metadata symbols, with every library mapping shared with the
source path.

## Decision

**The corelib is compiled by Roslyn into an assembly once, cached, and programs reference it like an
IL assembly whose method bodies the IL front-end lowers; the corelib keeps its own policies.**

### 1. Building and caching the assembly

- `CoreLibAssembly.Ensure` compiles the shipped corelib sources into `CsharpcCoreLib.dll` (Release,
  unsafe, nullable off, deterministic) against the same reference assemblies programs bind against.
  A corelib that doesn't compile is a csharpc bug and fails the compile with Roslyn's errors. Building
  takes ~6 s on the shared VPS (1.3 MB of IL).
- Cached in `~/.cache/csharpc/corelib-<key>.dll`, keyed on: the sources (paths and contents), the
  preprocessor symbols **the corelib tests** among those it is parsed with (`CSHARPC`, the program's
  symbols, `CSHARPC_TARGET_*`, the globalization modes), Roslyn's version, the reference assemblies
  and a format number. Only tested symbols count, so the program's `DEBUG`/`NET11_0` and its own
  symbols don't split the cache; a program that defines a symbol the corelib tests gets its own
  assembly, exactly as the source corelib saw that symbol. One assembly per target OS and
  globalization mode in practice.
- A stamp (`corelib-stamp-<hash>.txt`, hash of every symbol and each file's size and modification
  time) maps to the content key, so a warm compile stats the files instead of reading and hashing
  4.3 MB (~45 ms, mostly start-up of that code path). Any change to a file falls back to hashing the
  contents; nothing stale can be used.
- Files are written under temporary names and renamed, the assembly last; concurrent compiles (test
  suites run 4 at once) may build it twice but never see a partial one.
- The language version is fixed (preview, the newest csharpc knows). The source corelib was parsed
  with the program's `LangVersion`; binding the corelib differently per program was accidental, and
  C# versions can change overload resolution (first-class spans in C# 14).

### 2. Shadow redirection (`CoreLib.Map`)

`[Shadows]`/`[ShadowMembers]` declarations are scanned from syntax while the assembly is built and
written next to it (`corelib-<key>.shadows`); `CoreLib` reads that index instead of scanning trees,
resolves shadow types by metadata name in the corelib assembly, and treats the corelib assembly as
"not library" like the source corelib was (only BCL symbols are redirected). The index also lists
the `[RuntimeExport]` methods, so a compile reads only their attributes from metadata (reading every
corelib member's was 83 ms of hello world's 268 ms of lowering). `[ShadowInstance]` and
`[OptimizeForSize]` are read from metadata attributes, per symbol, as needed. Matching by canonical
signature is unchanged: it compares symbols, which metadata symbols provide exactly.

### 3. Intrinsics, externs and per-target code

- `CoreIntrinsics` members are `[MethodImpl(InternalCall)] extern`; Roslyn's metadata symbols report
  them as not `extern`, so `ModuleCodeGen.IsExternal` also treats corelib InternalCall methods as
  bodiless. Call sites are recognized by containing type, as before.
- Intrinsics that need a constant argument (`LibraryFrames`) and the `StringComparison.Ordinal`
  rewrite get one: operands loaded by `ldc.*`/`ldstr` carry their value as `ConstantValue`.
- `[DllImport]`s come from metadata (`GetDllImportData`), as for any IL assembly.
- `#if` per target or mode: a different symbol set is a different assembly (section 1).

### 4. What changes for corelib code, and what doesn't

The corelib's code is now Roslyn's lowering of it instead of csharpc's lowering of Roslyn's
operation trees. The IL front-end already handles C# compiler idioms (interpolation handlers,
closures and `<>c` caches, iterator and async state machines through the corelib's builders, string
`switch`, checked arithmetic, `in`/`ref readonly` defensive copies, generic math through
`constrained.`). What the source path did *inside corelib bodies* by recognizing IOperation shapes:

| Source-path behaviour | With IL |
|---|---|
| Intrinsics/BCL mappings at call sites | Same code: IL call sites become synthetic operations on the same symbols (ADR 0010) |
| `CoreIntrinsics` with constant arguments, ordinal-comparison rewrite | IL operands carry `ldc`/`ldstr` constants |
| `t == null` folded for value-type `T` | `box T` followed only by a null test no longer allocates |
| Array initializers stored directly | Constant indices into a `newarr` of constant length skip null and bounds checks |
| `p[i]` as an element GEP | `p + i * sizeof(T)` becomes an element GEP |
| Comparisons in source polarity | Forward compare branches branch on the negated condition into the fall-through |
| csharpc's own iterator/async transform (LLVM coroutines) | Roslyn's state machines over the corelib builders (`AsyncBuilders.cs`) |
| Non-generic static initializers run at startup | The type's `.cctor`, lazily (beforefieldinit: on first static field access) |
| `fixed` over a span: element 0 even when empty | `GetPinnableReference`: a null pointer for an empty span, as in .NET |
| DWARF subprograms for corelib functions (paths `<corelib>/X.cs`, not on disk) | None: corelib frames appear by symbol in gdb (`--no-strip`); user code's debug info is unchanged |

Stack traces ([ADR 0012](0012-stack-traces.md)) never listed corelib frames; they don't now.
Devirtualization, class-hierarchy analysis and the generic-virtual type switch consider the
program's types (and package IL), never the corelib's, as before.

Lowering the whole corelib from IL once (`CSHARPC_CORELIB_SURVEY=1 --il-survey`) found what the IL
front-end lacked, all now implemented: `calli` and `ldftn` as a function pointer (AppContext's switch
lookup, ICU's calendar enumeration), `fixed` over spans and strings (`GetPinnableReference`, 48
sites), `new Range(Index, Index)`, the address of a library's readonly static, `box` of `nint` before
a null test, and `localloc` on wasm32. The 5 survey failures left are library surface csharpc doesn't
implement yet (`Type.Module`, the `Rune` and `StreamingContext` structs, a `CharUnicodeInfo`
overload); a program reaching them gets the same diagnostic with either corelib.

### 5. Corelib policies the IL assembly keeps

An IL assembly from a package is lowered with ADR 0010's tolerance: methods that fail become stubs,
its types join class-hierarchy analysis, its references don't count as the program's. The corelib
assembly is flagged (`IlAssembly.IsCoreLib`) and keeps the source corelib's rules: a corelib method
that fails to lower is an error; its types stay out of class-hierarchy analysis; references from
corelib code are roots for the reachability analysis of package IL; diagnostics name
`<corelib>/CsharpcCoreLib.dll` rather than the cache path.

### 6. Calls between the program and the corelib

Code generated into the program used corelib internals, which a referenced assembly doesn't expose:

- `[GeneratedRegex]` code calls `Regex.CreateForGeneratedRegex`, now public.
- Reflection-style System.Text.Json generated `partial` method implementations *into* a corelib
  class. The generated code is now an ordinary program class, `CsharpcCoreLib.ProgramHooks`, using
  public corelib entry points; the corelib calls it through two core intrinsics (`JsonConfigureHook`,
  `JsonBuiltInHook`) that lower to a direct call when the program has the hook and to nothing (null)
  otherwise. This works with either corelib and links exactly what it linked before.

`CSHARPC_CHECK_CORELIB=1` (tests/programs.sh) still binds the corelib's sources inside a program's
compilation, since that is what it checks.

### Rejected or deferred

- **Building the assembly with csharpc** (an MSBuild step) instead of on first use: the symbol sets
  aren't known at build time (target × globalization mode × symbols programs define), building all
  likely ones costs a minute per csharpc build, and the first-use cost is ~6 s once per machine and
  csharpc version. Prebuilding the host target's default variant is a cheap follow-up.
- **Caching the compiled IR or objects of the corelib:** ADR 0014's reasons hold (whole-program
  layouts, slots, devirtualization).
- **Keeping the source corelib for generic methods only** (the staged option): unnecessary; every
  test suite passes with the whole corelib from IL.

## Consequences

Measured 2026-10-24 on the shared 4-vCPU VPS (load ~6–7 from another agent), interleaved, best of 3,
`-j 2`, `--corelib source` (the previous pipeline, same build) vs the default: wall / CPU / peak RSS.

| program | `-O0` before | `-O0` after | `-O2` before | `-O2` after |
|---|---:|---:|---:|---:|
| hello world | 1.47 s / 1.75 s / 200 MB | **0.92 s / 0.99 s / 165 MB** | 1.62 s / 1.96 s / 212 MB | **1.08 s / 1.14 s / 176 MB** |
| double + DateTime formatting | 2.85 s / 3.92 s / 235 MB | **1.76 s / 2.00 s / 196 MB** | 5.93 s / 7.38 s / 255 MB | **5.03 s / 5.59 s / 223 MB** |
| tests/programs/numbers | 2.58 s / 3.47 s / 240 MB | **1.93 s / 2.19 s / 204 MB** | 5.08 s / 6.24 s / 265 MB | **4.76 s / 5.37 s / 230 MB** |
| IlMetadataApp (System.Reflection.Metadata from IL) | 4.58 s / 6.62 s / 288 MB | **3.74 s / 4.98 s / 261 MB** | 13.37 s / 16.36 s / 332 MB | **12.31 s / 14.91 s / 305 MB** |

At `-O0` the gain is the corelib's parsing (Roslyn's phase for hello world: 1.1–1.5 s → 0.4–0.5 s)
and binding (the metadata program's corelib methods: 1368 ms of lowering plus ~1.8 s of CFG threads
→ 592 ms). At `-O2` LLVM dominates and is unchanged; the IR is the same size within 1 %.

- Executables: the -O2 size-budget programs are within 80 bytes of the source corelib's (hello,
  exceptions, typeinfo identical; async +32, collections +80, linq −48, numbers +16 bytes).
- Run time (executed instructions, callgrind, -O2): listsort −3.6 %, fileio −1.8 %, fannkuchredux
  −0.7 %, strbuild +2.3 % (register allocation in an ASCII scan loop of culture-sensitive IndexOf),
  the other 16 benchmarks within 0.1 %.
- Peak memory falls with the corelib's syntax trees and bound operations gone (−30 to −40 MB).
- The first compile on a machine (per corelib version and symbol set) builds the assembly: ~6 s
  more, once.
- Debug info no longer describes corelib functions (they had `<corelib>/` paths no debugger could
  open); mapping IL offsets through the assembly's PDB would restore lines for them.

## Next steps

1. Prebuild the host's default corelib variant in the csharpc build (removes the first-use cost).
2. Lowering on several threads (ADR 0014's next hot spot 2) applies to IL bodies too, now the bulk of
   lowering.
3. The remaining fixed cost of a small program is Roslyn's: reference-assembly symbols, source
   generators and binding the program (~0.4 s for hello world).
4. Retire `--corelib source` once a release has shipped with the IL corelib.
