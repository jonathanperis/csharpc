# ADR 0024 — Where the runtime boundary sits: our C runtime (A), or NativeAOT's runtime with csharpc as the LLVM backend (B)

- **Status:** Proposed (2026-11-08). A study, as decided by the owner: **no code, no fork, M8 stays on hold**; nothing in this
  ADR changes a default or a file outside `docs/`. Issue [#207](https://github.com/jonathanperis/csharpc/issues/207), child of
  the policy issue [#187](https://github.com/jonathanperis/csharpc/issues/187).
- **Context:** [ADR 0004](0004-memory-management.md), [ADR 0005](0005-exceptions.md), [ADR 0013](0013-framework-il.md),
  [ADR 0015](0015-corelib-il.md), [ADR 0016](0016-nativeaot-parity-and-end-state.md), [ADR 0017](0017-runtime-tiers.md),
  [ADR 0018](0018-reflection-metadata.md), [ADR 0021](0021-finalizers.md), [ADR 0023](0023-framework-il-first.md),
  [04-runtime.md](../04-runtime.md), [21-ownership.md](../21-ownership.md), the M8/M9 sections of [06-roadmap.md](../06-roadmap.md).

## The question

The owner's principle: **csharpc should own only the code that enables LLVM; everything else comes from upstream.** ADR 0023
and the ownership map already apply it to the libraries: the corelib shrinks to a thin contract layer over *our* C runtime,
and everything above comes from the framework's IL. Taken literally the principle moves the line one level lower: the C
runtime (GC, unwinding, threads, object and method-table layout, about 21 000 lines) and the reflection metadata would also be
upstream's, with csharpc as the LLVM backend of NativeAOT. Which boundary should the project commit to?

- **A: our C runtime plus upstream above it.** Today's goal. csharpc owns the compiler, the C runtime (`src/runtime`) and the
  contract layer of the corelib; CoreLib's non-runtime-coupled code and everything above comes from the framework's IL.
- **B: NativeAOT's runtime and ILC front end, csharpc as the LLVM backend only.** ILC (the NativeAOT compiler) keeps the IL
  reader, the closed-world dependency analysis, the type system, the layout of every runtime data structure, the reflection
  metadata and the generic-sharing machinery; csharpc turns method bodies into LLVM IR/objects; the program links NativeAOT's
  runtime libraries (`libRuntime.WorkstationGC.a` and friends) and NativeAOT's own `System.Private.CoreLib`.

B has two ways to plug in, which differ in cost and in what they preserve; the study keeps them apart.

- **B-ILC:** a code generator inside ILC, replacing the RyuJIT-based `Compilation` with one that builds a whole-program LLVM
  module (the shape of dotnet/runtimelab's NativeAOT-LLVM). Needs a fork of dotnet/runtime's `src/coreclr/tools/aot`
  (which is what M8 is: a fork). Keeps whole-program LLVM optimization.
- **B-JIT:** csharpc as a replacement for `libclrjit`, loaded by ILC through `--jitpath` and the JIT-EE interface (the shape of
  the abandoned LLILC). Needs no fork of ILC, but compiles one method at a time: no whole-program LLVM optimization.

A third shape, "csharpc's own front end emitting NativeAOT-ABI objects" (B2), is not considered a form of B: it would mean
reimplementing ILC (dependency analysis, EEType and metadata emission, generic dictionaries) inside csharpc, i.e. *owning*
what B is meant to take from upstream.

## How the evidence was gathered, and how to read it

No network was used (no `WebFetch`/`WebSearch`): the study rests on the repository, the .NET 11 RC2 SDK, the shared framework,
the NativeAOT runtime pack and the ILCompiler package that are in the local NuGet cache (the same version the compiler is pinned
to, `11.0.0-rc.2.26504.105`), and on what I know. Every claim is tagged:

- **[V]** verified on this machine (the command or file is named);
- **[M]** from memory of dotnet/runtime, dotnet/runtimelab or LLVM, **not verified here**; treat as a hypothesis to check
  before it carries a decision;
- **[U]** unknown: nobody measured it, and the ADR does not guess a number.

Measurements ran niced and one at a time on a shared 4-core VPS whose load average was 4 to 14 during the runs, with other
agents compiling: sizes and counts are reliable, wall clock is not.

## 1. What csharpc would own, delete and have to build

### Where we start (tree at `6a68aad3`, lines of code)

| Part | Lines | Notes |
|---|---:|---|
| Compiler (`src/Csharpc` without `corelib`) | about 50 100 | CodeGen 39 577, Frontend 5 635 (of which `Frontend/Il` 1 275), Driver 3 216, Llvm bindings 1 122, Wasm 547 |
| C runtime proper (`src/runtime/*.c`, `*.h`) | 14 065 | `gc.c` 3 797, `async.c` 1 800, `threads.c` 1 738, `eh.c` 1 540, `csrt.c` 1 106, `type.c` 1 084, `bclmisc.c` 538, `csrt.h` 517, the rest under 380 each |
| ICU shim port and Windows shims (`src/runtime/globalization`, `windows-shims`) | 6 744 | .NET's own `libSystem.Globalization.Native`, ported |
| Corelib (`src/Csharpc/corelib`) | 152 973 in 315 files | after the Regex flip (#206/#213); ownership map: keep 13 505 (67 files), shrink 6 846 (16), delete-when-IL-wins 132 524 (231) |
| **Owned in all** | about 224 000 | |

The compiler's *runtime-specific* lowering (the part that exists because of the runtime's ABI), by file group:

| Group | Files | Lines |
|---|---|---:|
| Method tables, interface/vtable stubs, type info, reflection and attribute metadata emission | `ModuleCodeGen.{VTables,TypeInfo,Reflection,GenericInterfaces,InterfaceStubs,Devirtualization,AssemblyTypes,ObjectArrayDelegates,VariantDelegates,SourceAttributes,NullableAttributes,MemberRecords,Resources}`, `TypeMapper`, `Generics`, `Interfaces`, `BclValueTypes.Reflection`, `FunctionCodeGen.{Reflection,AssemblyAttributes}` | 6 788 |
| Exceptions: try/catch/finally/filter lowering, stack-trace sites, wasm propagation | `FunctionCodeGen.{Exceptions,Filters,StackTraces,IlStackTraces}`, `StackTraces`, `Wasm/ExceptionLowering` | 2 596 |
| Async: LLVM coroutines, task types, runtime-async from IL | `FunctionCodeGen.{Async,RuntimeAsync}`, `ModuleCodeGen.{Async,TaskTypes,TaskSources,Cancellation}`, `Coroutines` | 1 224 |
| GC contract: write barriers, concurrent-marking barriers, finalizer thunks, wasm root spilling | `ModuleCodeGen.{WriteBarriers,ConcurrentBarriers,Finalizers}`, `ConcurrentBarrierElision`, `MemoryFacts`, `Wasm/RootSpilling` | 921 |
| Threads, runtime exports, static init | `FunctionCodeGen.Threading`, `CoreLibRuntime`, `ModuleCodeGen.{RuntimeExports,StaticInit}` | 1 150 |
| **Total** | | **about 12 500 (31 % of CodeGen and Wasm)** |

### Under A

| | What | Lines |
|---|---|---:|
| **Owns** | the compiler (about 50 100); the C runtime (14 065); the ICU port (6 744, shrinks if the shim question of 21-ownership.md is answered by linking upstream's `libSystem.Native`/`libSystem.Globalization.Native` from the pack, which the pack provides); the contract layer (keep + shrink: at most 20 351) | about 91 000 |
| **Deletes** | per library by ADR 0023, each flip with its A/B: at most 132 524 lines of corelib | up to 132 500 |
| **Has to build** | what the blockers of 21-ownership.md name: the SIMD mapping of `Vector128/256/512`, `EqualityComparer<T>.Default` and friends synthesized by codegen, the framework's regex and JSON source generators linked instead of our own, the culture cluster over the ICU shim, `libSystem.Native`/OpenSSL shims for the IL route, `Memory<T>` layout, .NET 11 `Decimal32/64/128`/`BFloat16`, reflection against upstream's `RuntimeType` family, the `Thread`/`Task` contract. And, for M8's gate, whatever dotnet's NativeAOT test suites ask of our runtime that no sample has asked yet [U: nobody has run them] |
| **Tracks upstream** | the CoreCLR CoreLib's runtime-coupled surface of the pinned version: 200 FCalls and 328 QCalls on 80 types (21-ownership.md, `ownership.sh --contract`) [V], and the framework IL itself, one .NET version at a time |

### Under B (B-ILC, the shape that keeps whole-program optimization)

| | What | Lines |
|---|---|---:|
| **Deletes** | the whole corelib, contract layer and glue included, because NativeAOT's CoreLib replaces it (152 973); the whole C runtime (14 065) and the ICU port (6 744), because `libRuntime.*.a`, `libSystem.Native.a` and `libSystem.Globalization.Native.a` come from the pack; the Roslyn source front end (`RoslynFrontend`, the source generators glue, `MethodGroupRewriter`: about 1 500) and every part of the 39 600 lines of CodeGen written against Roslyn symbols and `IOperation`s, i.e. nearly all of it; ADR 0018's reflection tables (ILC emits the metadata) | at least 173 800 (78 % of what is owned), plus most of CodeGen |
| **Keeps** | the Llvm bindings (1 122), the driver's toolchain, hardening, LTO and partitioned-optimization pieces (parts of 3 216), the test corpora (samples, conformance, programs, benchmarks: all recorded from real .NET and independent of the boundary) | a few thousand |
| **Has to build** | a backend written against ILC's type system and `MethodIL` instead of Roslyn symbols: IL to LLVM IR for NativeAOT's calling conventions, helper calls, GC reporting, EH tables and async protocol (section 2), plus its tests; the fork and its upkeep (M8) | [U]: no honest estimate; the ABI items are listed in section 2 |
| **Tracks upstream** | everything above, every release: ILC and the runtime are versioned together (the `ReadyToRun` header constants, `RuntimeImports`, the JIT-EE interface), so the backend moves with the fork's base | |

What the table says in one sentence: B deletes about four fifths of what csharpc owns, but what it keeps is the part that
speaks Roslyn and csrt, and what it has to build is a different product, the backend of someone else's compiler.

## 2. The ABI csharpc would have to match under B

Layouts and contracts of NativeAOT as I know them are tagged [M]; the symbol names are read from the pack's libraries
(`llvm-nm`, [V]) and from the `ilc` binary's strings ([V]).

| Item | NativeAOT's contract | csharpc today | Match |
|---|---|---|---|
| Object header and layout | an 8-byte header word before the method-table pointer (lock/hash) [M]; fields after the pointer; strings `{MT, int32 length, UTF-16}`, arrays `{MT, uint32 length, data}` [M] | no header word: `{mt, fields}`; strings `{mt, int32, UTF-16}`; arrays `{mt, int64 length, data @ 16}` (`csrt.h`) | strings only |
| Method table (EEType) | `{component size, flags, base size, related type, vtable slot count, interface count, hash code}`, vtable, interface map, optional fields (finalizer, dispatch map, generic dictionary, GC series before the table) [M]; read by managed code in CoreLib (`Internal.Runtime.MethodTable`, `[V]` the type name is in the pack's CoreLib) | `{base, name, instance_size, flags, interfaces*, interface_count, reserved}` (40 bytes), vtable; GC-relevant info is one flag bit (`CSRT_MT_NO_REFERENCES`) | no: every size and offset in `TypeMapper` and the emitted tables changes |
| Allocation | helpers `RhpNewFast`, `RhpNewArrayFast`, `RhpNewObject`, `RhpNewFinalizable`, `RhpNewVariableSizeObject`, `RhNewString` [V], thread-local allocation context in asm stubs (`AllocFast.S`) | `csrt_alloc*` into `gc.c`; the fast path is inlined into the program by runtime LTO | helpers differ; the LTO inlining is lost (the stubs are prebuilt objects, not bitcode) |
| GC contract | precise, generational, compacting, concurrent (the `gc.cpp` family). Every call site reports its live references, including interior pointers, in per-method GC info; frames are found by the unwinder; objects can move [M] | conservative, non-moving mark-sweep; no GC info, no stack maps; whole stacks, registers and TLS scanned; wasm spills references to the stack (`RootSpilling`) | no |
| Write barriers | `RhpAssignRef`, `RhpCheckedAssignRef`, `RhpCheckedXchg`, `RhpCheckedLockCmpXchg`, `RhBulkMoveWithWriteBarrier` [V]: card marking on every heap reference store | none by default; inline barriers only for the opt-in nursery and concurrent marker | no |
| Precise GC info from LLVM | needs a statepoint/stack-map to NativeAOT `GcInfo` encoder, or a shadow stack that costs loads and stores at every safepoint (ADR 0004's table) | n/a | [M] LLILC took the first route; NativeAOT-LLVM the second (wasm only) |
| Stack walking | NativeAOT's own `StackFrameIterator` over unwind info, with its libunwind port (`Runtime.PrivateLibunwind.o`, 49.9 KB in a hello program [V]); `RhpSfiInit`, `RhpSfiNext` [V] | the system unwinder (`libgcc_s`) driving `csrt_personality` (`eh.c`) | no |
| Exception handling | managed dispatch in CoreLib (`System.Runtime.EH`) [M] that walks frames, reads per-method EH clause tables and calls the compiler's **funclets**: `RhpCallCatchFunclet`, `RhpCallFilterFunclet`, `RhpCallFinallyFunclet`, `RhpEHEnumInitFromStackFrameIterator`, `RhThrowEx`, `RhRethrow`, `RhpThrowHwEx` [V] | LLVM `invoke`/`landingpad` with an Itanium-style personality; filters are separate functions run in the search phase | no, and the hardest item: LLVM has funclets only for the Windows EH model (`catchpad`/`cleanuppad`) [M], so catch/finally/filter bodies would have to be outlined by csharpc itself |
| Hardware exceptions | null dereference and divide traps become managed exceptions through a signal handler (`HardwareExceptions.cpp.o` [V]); the compiler relies on implicit null checks [M] | explicit checks branching to throw helpers (RESULTS.md names hardware-trap null checks as the real fix needing the EH runtime) | different, in B's favour for speed |
| Thread suspension | cooperative with return-address hijacking and GC polls: `RhpGcPoll`, `RhpGcProbeHijack`, `RhpSuspendingThread`, `RhpTrapThreads`, `RhpWaitForGC` [V]; every native call is bracketed by a transition frame (`RhpPInvoke`, `RhpPInvokeReturn`, `RhpReversePInvoke*`) [V] | signal-based stop-the-world (`SIGPWR`/`SIGXCPU`), conservative scan of the stopped thread; no transition frames | no |
| Generic dictionaries, GVM, interface dispatch | shared canonical code for reference-type instantiations with runtime dictionary lookups; generic virtual methods and interface calls through dispatch cells: `RhpInterfaceDispatch`, `RhpCidResolve`, `RhpDispatchResolve`, `RhpRegisterDispatchCache` [V]; runtime generic instantiation by `System.Private.TypeLoader` (207 KB in the pack [V]) | full monomorphization; interface calls by slot lookup (`csrt_interface_slots`); a generic-virtual type switch | no; unknown whether ILC can be asked not to share code [U] |
| Reflection metadata | ILC's `MetadataTransform` produces a compact metadata blob and mapping tables, read by `System.Private.Reflection.Execution` (82 KB in the pack [V]); stack trace metadata in `System.Private.StackTraceMetadata` (43 KB [V]) | own records (`csrt_TypeInfo`, `csrt_MethodRecord`, ADR 0018): `type.c` 1 084 + CodeGen reflection about 2 250 + corelib Reflection 4 624 lines | different format; under B-ILC it is upstream's, which is the strongest argument for B |
| runtime-async (.NET 11) | in the JIT-EE interface: `CORINFO_ASYNC_INFO`, `getContinuationType`, `AsyncResumptionStub`, `AsyncContinuationType`, `GetAsyncVariantMethod`, `ReturnDroppingAsyncVariantMethod` all appear in `ilc` [V]; the JIT performs the state capture and the runtime resumes through `Continuation` objects (`AsyncHelpers` in CoreLib [V name]) [M] | LLVM coroutines whose frames are runtime objects, a scheduler in `async.c` (1 800 lines), Task and builders shadowed in the corelib | no; the continuation protocol must be implemented by the backend [U: whether LLVM coroutines can express it] |
| Startup | `libbootstrapper.o` defines `main`, calls `RhInitialize`, `RhRegisterOSModule`, `InitializeModules`, then `__managed__Main`; it needs the sections `__start___modules`, `__start___managedcode`, `__start___unbox` and CoreLib's exports `GetRuntimeException`, `AppendExceptionStackFrame`, `OnFirstChanceException`, `OnUnhandledException`, `ResolveDispatch`, `GetSystemArrayEEType`, `ThreadEntryPoint`, `RuntimeFailFast` [V, `llvm-nm -u`] | `csrt_init`/`csrt_shutdown` called by an emitted `main` | no; ILC generates the module header blobs under B-ILC; under B-JIT ILC does |
| Frozen data, preinitialized statics, thread statics | `RhRegisterFrozenSegment`, `RhRegisterInlinedThreadStaticRoot`, `RhGetThreadStaticStorage` [V]; static constructors interpreted at compile time [M] | statics are globals initialized at startup or lazily | no |
| Targets | the ILCompiler package's `runtime.json` lists freebsd-x64/arm64, linux-arm/arm64/x64, linux-musl-arm/arm64/x64, osx-x64/arm64, win-x86/x64/arm64 [V]; no wasm RID | linux x64/arm64, macOS arm64, Windows x64, wasm32-wasip1 | B gives more platforms (musl, arm32, x86, FreeBSD, macOS x64, Windows arm64); **no wasm32 in the 11 RC2 packs** |

Reading: of the sixteen rows, one (the string layout) already matches. Four are structural for a precise-GC, funclet-EH
runtime and need compiler technology csharpc has never needed: precise GC info (or a shadow stack), funclets, P/Invoke
transition frames and the async continuation protocol. The rest is the same work seen from the table layouts.

## 3. What happens to the Roslyn-source path and to `--via-il` under B

- **The Roslyn-source path ends as a lowering path.** `csharpc app.cs -o app` stays possible as a *driver*: Roslyn compiles to
  IL in process (as `csc` does) and the IL goes through ILC with csharpc's backend. The operation-tree and
  `ControlFlowGraph` lowering (ADR 0001, ADR 0003: the reason csharpc exists in its present form), the source-level debug-info
  and `--stack-traces` machinery built on Roslyn's PDB (ADR 0012), `--dump-cfg` and the source generators' integration would be
  removed or rewritten against ILC. "Every feature must also work through the IL front-end" becomes trivially true, because
  there is only one front end, and it is not csharpc's.
- **`--via-il` and the IL front-end (ADR 0010) become the only path, and ILC's, not csharpc's:** `Frontend/Il` (1 275 lines),
  `FrameworkIl` and the shadow index (ADR 0013, 0023), `CoreLibAssembly`/`CoreLibFacade` (ADR 0015) are replaced by ILC's
  own reading of IL and its trimming, which is NativeAOT's behaviour by construction (that is the parity the ADR 0016 asks for).
- **Under B-JIT** the Roslyn-source path survives only as a producer of input for ILC; csharpc's compile step no longer sees
  the program, only methods.
- **Under A** nothing changes: both front ends stay, the IL one gains weight as ADR 0023 flips libraries.

## 4. What the NativeAOT runtime pack contains

`Microsoft.NETCore.App.Runtime.NativeAOT.linux-x64` 11.0.0-rc.2.26504.105, restored by `NativeAotPack` into
`~/.nuget/packages` (already present on this machine, so no download; `runtimes/linux-x64/native`, 45 MB in all, `lib/net11.0`
27 MB of framework assemblies). Symbols counted with `llvm-nm-18 -g`; "defined" are global definitions, "undefined" the symbols
the archive needs from outside (libc, the managed side, other archives).

| File | Bytes | Members | Defined | Undefined | What it is |
|---|---:|---:|---:|---:|---|
| `libRuntime.WorkstationGC.a` | 6 518 670 | 75 | 2 605 (173 `Rh*`, of which 83 `Rhp*`; 2 355 C++-mangled) | 786 | the runtime: GC (a unity build, `unity_0_cxx.cxx.o`), handle tables, thread store, stack frame iterator, libunwind port, hardware-exception handling, asm stubs (`AllocFast.S`, `WriteBarriers.S`, `ExceptionHandling.S`, `GcProbe.S`, `StubDispatch.S`, `PInvoke.S`, ...), PAL |
| `libRuntime.ServerGC.a` | 9 261 772 | 78 | 3 700 | 830 | the same with the server GC |
| `libRuntime.VxsortEnabled.a` / `VxsortDisabled.a` | 784 552 / 22 024 | 9 / 1 | 152 / 5 | 12 / 0 | vectorized sort used by the GC |
| `libbootstrapper.o` / `libbootstrapperdll.o` | 7 472 / 8 520 | 1 / 1 | 1 (`main`) / 0 | 20 / 20 | process entry, or the entry for a shared library |
| `libeventpipe-disabled.a` / `-enabled.a` | 247 772 / 2 892 992 | 5 / 28 | 278 / 1 333 | 0 / 547 | EventPipe stubs, or the tracing runtime |
| `libstandalonegc-disabled.a` / `-enabled.a` | 30 316 / 120 502 | 1 / 1 | 3 / 68 | 3 / 74 | loader for an out-of-process GC (`DOTNET_GCName`) |
| `libaotminipal.a` | 224 530 | 19 | 48 | 51 | `minipal_*` (mutex, time, cpu features, thread ids) |
| `libstdc++compat.a` | 11 826 | 1 | 7 | 2 | libstdc++ symbol compatibility |
| `libSystem.Native.a` | 693 322 | 29 | 284 | 215 | POSIX shim (files, sockets, signals, process, time) |
| `libSystem.Globalization.Native.a` | 507 772 | 12 | 151 | 164 | the ICU shim csharpc already ports (`src/runtime/globalization`) |
| `libSystem.Security.Cryptography.Native.OpenSsl.a`, `libSystem.Net.Security.Native.a`, `libSystem.IO.Compression.Native.a` | 1 459 540, 57 192, 33 726 | | | | the other shims; `NativeAotPack.Shims` already links the compression one (with `libz` 1 124 428, `libzstd` 3 988 424, `libbrotli*` 2 743 046) and the Apple/GSS ones |
| `System.Private.CoreLib.dll` | 6 161 704 | | | | **NativeAOT's** CoreLib (the shared framework's, which csharpc binds against and shadows, is CoreCLR's: 17 855 824 bytes, with `libcoreclr.so` 5 281 936 and `libclrgc.so` 682 kB beside it) |
| `System.Private.TypeLoader.dll`, `Reflection.Execution.dll`, `StackTraceMetadata.dll` | 207 184, 81 704, 42 792 | | | | the managed runtime services ILC-compiled programs carry |

What a link would need and could use:

- The runtime's undefined set that the managed side must provide is small and named: `RhThrowEx`, `RhRethrow`, `RhThrowHwEx`,
  `RhExceptionHandling_FailedAllocation`, `RhpCalculateStackTraceWorker`, `RhpCidResolve`, `ProcessFinalizers` and the symbols
  listed in section 2 for the bootstrapper; the rest of its 786 undefined symbols is libc, pthreads and `minipal`. That is the
  *whole* contract at link level, and it is concrete.
- The only pieces that **could** be taken without taking the rest are the shims (`libSystem.Native.a`,
  `libSystem.Globalization.Native.a`, OpenSSL, compression), which is the question 21-ownership.md already raises and which
  belongs to A as much as to B. `libstandalonegc-enabled.a` is a loader: the collector itself is inside the `libRuntime`
  archive, and the shared framework's `libclrgc.so` exports only `GC_Initialize` and `GC_VersionInfo` [V]; its contract is the
  object the host passes in (`IGCToCLR`, dozens of callbacks [M]), so using upstream's collector alone under A would still need
  precise roots or its conservative mode (`gcConservative` [M, unverified]).
- Nothing is bitcode, so nothing links through csharpc's runtime LTO.

## 5. The precedent, and what the local toolchain says about the direction

What I remember of dotnet/runtimelab's NativeAOT-LLVM [M, none of it checked here; confidence in brackets]:

- It existed as a runtimelab branch with an `ILCompiler.LLVM` project: a compilation that converts IL to LLVM IR through a C#
  binding of the LLVM C API, built from ILC's own object model [high].
- Its target was WebAssembly (browser and WASI), not native x64/arm64 [high]. ADR 0004's table already records the consequence
  for the GC: a **shadow stack** for GC roots, because wasm cannot scan its own stack [high]; the same table lists LLVM statepoints
  as the route for native targets, which that project did not take [medium].
- Exception handling and the other items of section 2 were reworked for wasm rather than matched to the native ABI [low; I do not
  remember the details with enough confidence to list what it implemented and what it never finished].
- It stayed experimental. My recollection is that it was wound down when the platform moved to bringing CoreCLR and
  NativeAOT to WebAssembly with RyuJIT [low-medium].
- LLILC (an LLVM JIT for CoreCLR, through the JIT-EE interface, using statepoints and CoreCLR's GC info encoder) is the other
  precedent; it was abandoned around 2017 [medium].

What the local toolchain adds [V]:

- The .NET 11 RC2 `ilc` contains a **WebAssembly target of its own**: `libclrjit_universal_wasm_x64.so` beside the other JIT
  libraries, source paths `Target_Wasm/WasmJumpStubNode.cs`, `WasmReadyToRunHelperNode.cs`, `WasmUnboxingStubNode.cs`,
  `NodeFactory.Wasm.cs`, `JitInterface/WasmLowering.cs`, and an error string "NAOT wasm type signature lowering not yet
  implemented". There is no mention of LLVM anywhere in `ilc` (0 hits in either encoding). Upstream is building NativeAOT-wasm on
  RyuJIT, which removes the reason that made an LLVM backend attractive there.
- The 8.0 ILCompiler package shipped `libobjwriter.so` (20.8 MB, an LLVM-based object writer [M on the "LLVM-based"]); the 10 and 11
  packages do not contain it.
- `ilc` has `--jitpath` ("Path to JIT compiler library", UTF-16 string [V]) and loads `libclrjit_*` and `libjitinterface_x64.so`:
  the B-JIT plug-in point exists today.

Conclusion for the precedent: the only completed LLVM-backed NativeAOT work I know of was wasm-only and was not carried into
the product; upstream's current direction does not use LLVM. B would not be following a path upstream is walking, and I found
no evidence in the local toolchain that a native-x64 LLVM backend for ILC has ever existed [U for anything outside it].

## 6. Measurements (cheap, niced, no code changes)

All on x86-64 Linux, .NET 11 RC2, `-O2`, stripped. The programs are the repository's `tests/size/hello.cs` and `benchmarks/gc/gclatency.cs` (run by a copy of
`benchmarks/gc/gc.sh` that points at a compiler built on 2026-11-07 instead of rebuilding it; the NativeAOT hello is
`Console.WriteLine("Hello, World!")` published with `PublishAot` and `InvariantGlobalization`, with
`-Wl,-Map` added through `LinkerArg` to attribute the bytes; csharpc's non-LTO map comes from `--cc` wrapping `clang`
with `-Wl,-Map`). No file of the repository was changed for it.

### 6.1 Hello world: size of the runtime in the program

| | Bytes | Where it goes |
|---|---:|---|
| csharpc `hello.cs`, runtime LTO (default) | 28 536 (text 21 442) | the whole program, runtime included |
| csharpc, `--no-runtime-lto --no-strip`, objects linked | 174 552 (text 123 844, bss 155 116) | runtime objects pulled in: `gc` 22 479, `async` 22 262, `eh` 21 553, `csrt` 19 794 (+131 136 bss), `threads` 17 036, `pinvoke` 3 580, `console` 1 127, `strings` 334: **108 165 bytes of code and data**; most of the rest is symbol and dynamic-linking tables (`--no-strip`) |
| NativeAOT hello (`PublishAot`, invariant globalization, stripped) | 1 193 032 | linker map: ILC output (`hello.o`: managed code, metadata and data) 870 523 (+6 048 bss); `libRuntime.WorkstationGC.a` 361 353 (+34 747 bss); `libSystem.Native.a` 29 456; `libaotminipal.a` 3 708; others under 500 each |
| inside `libRuntime.WorkstationGC.a` (361 353) | | the unity build 202 739; the libunwind port 49 911; `objecthandle` 17 026; `gcconfig` 9 537; `handletablecore` 6 466; `StackFrameIterator` 5 970 |

Summing the map overstates the file (1 309 990 against 1 193 032), so ratios are approximate. The point is structural: **the
native runtime alone, at 361 KB, is 13 times the whole csharpc hello**, and ILC's managed part is another 870 KB. Under B the
floor for hello is the pack's runtime plus NativeAOT's CoreLib startup path; LLVM could make the managed part smaller than
RyuJIT's [U], but not the 361 KB, which is prebuilt upstream code. Start-up of the two hello programs, 200 runs each, three
repeats, best repeat (machine loaded, so only the order of magnitude is meaningful): csharpc 8.1 ms, NativeAOT 16.0 ms
(the other repeats: 12.1 and 14.8 ms against 20.5 and 44.5).

### 6.2 Allocation and GC: ours against upstream's

`benchmarks/gc/gc.sh`, 3 runs, median, 4 threads, the checksum equal to real .NET's for all three engines. The program has three
phases: `churn` (many short-lived objects with a sliding window), `livebuild` (a pointer-dense graph) and `livemutate` (garbage
while the graph stays live). "jit" is the .NET 11 JIT with its default (workstation, concurrent) GC, "aot" NativeAOT, which
runs the same GC as the `libRuntime.WorkstationGC.a` above. The machine was at load 4 to 14 during these runs.

| engine | phase | wall ms | ops/s | pause ms | collections | max stall us | heap MiB |
|---|---|---:|---:|---:|---:|---:|---:|
| csharpc | churn | 1 290 | 1 550 387 | 273.9 | 17 | 199 424 | 35.2 |
| jit | churn | 1 402 | 1 426 533 | 437.4 | 50 | 216 102 | 17.3 |
| aot | churn | 1 832 | 1 091 703 | 718.8 | 50 | 475 398 | 17.2 |
| csharpc | livebuild | 236 | 3 389 830 | 0.0 | 0 | 156 152 | 79.6 |
| jit | livebuild | 656 | 1 219 512 | 432.1 | 7 | 222 634 | 48.9 |
| aot | livebuild | 1 002 | 798 403 | 622.7 | 7 | 318 597 | 48.9 |
| csharpc | livemutate | 997 | 481 444 | 284.6 | 2 | 192 728 | 98.7 |
| jit | livemutate | 4 897 | 98 019 | 3 889.6 | 33 | 604 240 | 75.8 |
| aot | livemutate | 5 175 | 92 753 | 4 313.4 | 33 | 1 406 185 | 75.9 |

What this does and does not say:

- On this workload, with heaps under 100 MiB, **our collector is not behind upstream's: it is ahead on every phase**, by
  1.1x to 5x in wall time (against the JIT 1.1x to 4.9x, against NativeAOT 1.4x to 5.2x). Part of that is policy rather than algorithm: ours collects less often (17 against 50 collections;
  a 32 MiB minimum trigger) and holds a larger heap (35 to 99 MiB against 17 to 76 MiB). Resident set size could not be
  measured (`/usr/bin/time` is not installed here), so the memory side of the trade is [U].
- It says nothing about what upstream's generational, compacting and concurrent design buys on **large live heaps** (gigabytes),
  with many cores, under the server GC or under a steady high-allocation service: all of that is untested and is the scenario M7 and M9
  (TechEmpower) measure. The harness does not scale to it. Treat "upstream's GC is better for big heaps" as a
  hypothesis [M], and "ours is not slower for small ones" as measured [V].
- It cannot say what B would do to allocation speed *in compiled code* (calls to `RhpNewFast` stubs instead of an
  LTO-inlined bump/bitmap allocation, `RhpAssignRef` card marking instead of no barrier): that needs a built B [U]. The CI
  table in `benchmarks/RESULTS.md` (2026-10-23, not rerun) shows csharpc ahead of NativeAOT on binarytrees (181 against 254 ms) and
  churn (142 against 171 ms) on x86-64, which is the same direction.
- For codegen alone: the compute-only rows of that table (nbody 262/297, spectralnorm 125/130, mandelbrot 106/110, matmul 51/49,
  fannkuch 246/243, quicksort 568/593 ms) put csharpc within about 0 to 12 % of NativeAOT; the large gaps (`output` 23 against 1 333 ms,
  `fib` 55 against 85) come from the runtime and start-up, which B would give up. [Indicative: one CI run, not rerun here.]

### 6.3 Hardening of the two hello binaries

Both are PIE with full RELRO, `BIND_NOW` and a non-executable stack; neither carries a CET (IBT/SHSTK) property by default.
csharpc's `--hardening max` adds CET/BTI (ADR 0009). The NativeAOT runtime archives are prebuilt: a binary with CET could not be
linked from them unless the fork rebuilds the runtime with the flags [M: the linker keeps the property only when every input has
it]. The NativeAOT binary needs only `libm`, `libc` and the loader; ours also needs `libgcc_s` (the system unwinder).

## 7. Effect on the priorities (speed, then security, then size)

| Priority | A | B |
|---|---|---|
| **Speed** | the standing lead (13 of 20 CI benchmarks against NativeAOT, GC phases above) comes from owning code and runtime together: whole-program LLVM with the runtime in bitcode, inlined allocation, tuned collector policy, a buffered console. Upstream's GC could be adopted later behind a flag if big-heap measurements ask for it [U] | the backend's share is codegen only (0 to 12 % on compute rows) [indicative]; allocation and barriers become calls into prebuilt stubs; B-JIT loses whole-program optimization; the precise-GC and funclet-EH requirements add cost to generated code that csharpc does not pay now (statepoint spills or a shadow stack, transition frames) [U how much]. In B's favour: upstream's GC for large heaps, hardware-trap null checks, upstream's tiered library work |
| **Security** | a smaller native attack surface (14 065 lines of C plus the ICU port, against the pack's 361 KB of C++ and asm in a hello) with our hardening flags on the whole program; but a conservative collector and our own unwinder, reviewed by us only | upstream's runtime has years of field use, fuzzing and a CVE process; it is also more native code, prebuilt without our flags (CET), and csharpc's own mitigations (stack probes, hardened runtime) would not reach it unless the fork rebuilds it |
| **Size** | hello 28.5 KB; the budgets in `tests/size/budgets.txt` hold | structurally at best near NativeAOT's: hello at least the 361 KB runtime plus the managed startup path (NativeAOT's whole hello is 1.19 MB). The goal of ADR 0016 "smaller than NativeAOT" would be reduced to whatever LLVM saves on managed code [U] |

By the owner's order, B is a loss on speed in everything this study could measure and a certain loss on size, and its security
effect is two-sided. It buys **parity by construction** (reflection, generic sharing, trim analysis and warnings, `rd.xml`,
feature switches, platform breadth, M8's acceptance tests mostly inherited) and moves maintenance of GC/EH/threads to upstream.

## 8. Risks and open questions

Risks of A (staying):

1. **The runtime is ours to get right for the M8 gate**: dotnet's NativeAOT runtime and library tests exercise GC, finalizers,
   weak handles, `GC.*` APIs, reflection edge cases and threading in ways our samples never did. How many of them fail on csrt is
   unknown [U]; it is the main unmeasured cost of A and should be sampled before M8, not at it.
2. **A conservative, non-moving, non-generational collector** (ADR 0004, 0020) may not scale to large heaps; the benchmarks here do
   not reach that regime. The nursery and concurrent marker are opt-in.
3. **Two surfaces to track:** the CoreCLR CoreLib's 528 FCall/QCall entry points per .NET version, and `RuntimeType`-based
   reflection that upstream's code expects (REFLECT: 20 495 lines gated in the ownership map).
4. **Reflection and generic-sharing parity is re-implemented, not inherited:** ADR 0018's records follow NativeAOT's *model*, not its
   format; every behaviour difference is a potential silent difference, and the rule of this project is that wrong output is
   the worst bug.
5. Platform ports of the runtime (Windows, macOS, WASI) are ours.

Risks of B:

1. **Feasibility of the structural items is unproven**: funclets under LLVM on Itanium targets, GC info from LLVM, transition
   frames, the async continuation protocol, canonical-shared generics. I know of no completed native LLVM backend for ILC [U].
2. **A new product and a long fork:** a backend against ILC's API (not Roslyn's), upkeep against a fast-moving `src/coreclr/tools/aot`
   and the runtime's ABI, every release.
3. **The differentiators go**: wasm32 (no pack; upstream's own wasm is RyuJIT-based), the standalone `csharpc app.cs` experience as it
   is, the 28 KB binary, the debug-info and stack-trace machinery on Roslyn, the compile-time work of ADR 0014 (ILC's analysis
   pipeline runs first).
4. **Sunk work**: 224 000 lines of which at least 173 800 are deleted, and the ADR 0023 flips would be moot for any library the
   fork takes whole.
5. Under B-JIT: no whole-program optimization, the thing that makes csharpc faster in the first place.

Open questions, each a cheap follow-up that would make the decision less of a guess (none done here, by instruction):

- **Q1.** How does csrt fare on dotnet/runtime's own NativeAOT smoke and GC tests (a sample of, say, a few hundred, built with
  csharpc on CI)? It prices A's biggest risk.
- **Q2.** A large-heap run of `benchmarks/gc`-style code (1 to 4 GB live, 4 to 16 threads) against upstream's workstation and server GC, and
  resident set size for all engines. It prices the hypothesis that upstream's collector is needed.
- **Q3.** Can LLVM express NativeAOT's EH on x64 Linux (outlined funclets that reach the parent frame via `llvm.localescape` or
  equivalent) and can a `.llvm_stackmaps` section be converted to NativeAOT's GC info? A two-method spike in a scratch tree
  answers feasibility; it is also the exit criterion for reopening B.
- **Q4.** Can ILC be told not to share generic code (a flag or a policy), so that B's codegen keeps csharpc's monomorphization?
- **Q5.** Does `gcConservative` exist and work in the pack's GC build, so that upstream's collector could drive csrt-style
  conservative roots (a hybrid under A)?
- **Q6.** What does NativeAOT's CoreLib need from the runtime beyond the 173 `Rh*` exports (managed `RuntimeImports` count)? If it is
  smaller than CoreCLR's 528, "NativeAOT's CoreLib on a csharpc-owned runtime" (a hybrid that adopts NativeAOT's object and
  method-table layout but keeps our collector and EH) deserves its own study; as defined here it is neither A nor B and is not
  recommended without Q3 and Q6.

## Decision

**Recommendation: keep boundary A for now.** Continue ADR 0023 (everything above the contract layer from the framework's IL, the
shims from the pack where they remove glue), and answer Q1 and Q2 before M8 so that the risk of A is measured rather than assumed.
Reopen B only if Q3 shows the two hard items (funclet EH and GC info from LLVM) are tractable *and* Q1 or Q2 shows that csrt
cannot reach parity or scale.

Reasons, in the owner's order:

1. **Speed.** Everything measured favours A or is neutral (GC phases, hello start-up, console, the CI table), and nothing measured favours B;
   B's possible gains (large-heap GC, hardware traps) are unmeasured. B-JIT would also give up whole-program LLVM.
2. **Security.** Two-sided; B does not clearly win, and loses the hardening of the runtime unless the fork rebuilds it.
3. **Size.** A is smaller by an order of magnitude or more and B cannot get below the 361 KB runtime.
4. **Principle.** The owner's rule, "csharpc owns only the code that enables LLVM", is met by A in the places where it costs
   nothing (the libraries, ADR 0023). Taken to the runtime it replaces the product with a different one (the backend of ILC); that
   is a legitimate choice, but it is a change of what csharpc is, and the owner should make it knowing that.

**The decision the owner has to make**: *is csharpc an LLVM compiler with a runtime it owns (A), or the LLVM backend of NativeAOT (B)?*

| Choice | It commits us to |
|---|---|
| **A** (recommended) | owning and maintaining the runtime (GC, EH, threads, async, type system, about 14 000 lines of C) across five platforms; passing dotnet's AOT tests with it at M8; keeping the corelib contract layer in step with each .NET version's CoreLib surface; re-implementing, and testing against real .NET, the reflection and generic-sharing behaviour NativeAOT gets from ILC. In return: the speed and size lead, the standalone compiler, wasm32, and the Roslyn path |
| **B-ILC** | a fork of ILC with an LLVM backend built from scratch (no estimate possible [U]), riding every upstream release; deleting about 78 % of what is owned and the Roslyn path; giving up hello-world size (floor 1.19 MB), wasm32 and the standalone experience; making funclet EH, precise GC info, transition frames and the async protocol work in LLVM before anything runs. In return: NativeAOT parity by construction, upstream's GC and platforms, M8's tests mostly inherited |
| **B-JIT** | the same ABI work, with per-method compilation (no whole-program LLVM optimization), but no fork of ILC; speed expected below A and probably below NativeAOT's [U] |

Whichever is chosen, the next ADR 0023 flips and the shim question of 21-ownership.md are valid under both A and B-ILC's
interim, so they need not wait for this decision.

## Unverified in this study

- Every NativeAOT layout and protocol tagged [M] (object header, MethodTable fields, array length width, GC info, EH funclet
  protocol, `System.Runtime.EH`, runtime-async calling convention, `ReadyToRun` header, preinitialization, `gcConservative`).
- All of the dotnet/runtimelab NativeAOT-LLVM and LLILC history in section 5.
- That LLVM has funclets only for the Windows EH model and cannot outline for Itanium targets.
- The cost of B in lines or months, and the speed of generated code under B.
- Resident set size and start-up with a quiet machine; GC behaviour on large heaps and with the server GC.
- Whether the unity build in `libRuntime.WorkstationGC.a` is gc.cpp (assumed from its size and its place among the members).
