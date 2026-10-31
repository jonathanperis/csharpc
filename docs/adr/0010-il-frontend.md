# ADR 0010 — IL front-end: IL as a method-body source for Roslyn's metadata symbols

- **Status:** Accepted (2026-10-23). Vertical slice implemented; see [Status of the implementation](#status-of-the-implementation)
  and [Follow-up: phase 2](#follow-up-phase-2-2026-10-23) (compiled async, resources, AppContext, framework IL);
  the program's own assembly as input: [The program as IL](#the-program-as-il-2026-10-30).

## Context

Roadmap M5: programs depend on assemblies that ship only IL — NuGet packages, prebuilt DLLs given as
`<Reference>`, and eventually (M6) the BCL-adjacent libraries that csharpc and Roslyn themselves use.
csharpc is a closed-world compiler (ADR 0006): every method that can run must be lowered into the one
LLVM module. Today the only body source is C# source, via Roslyn's `ControlFlowGraph` (ADR 0003).

Facts about the existing code generator that decide the design:

- **Everything is keyed on Roslyn symbols.** Layouts (`ClassLayout`, `StructLayout`), vtable slots,
  interface maps, static fields, mangling, monomorphization (`TypeSubstitution`), corelib redirection
  (`CoreLib.Map`, `[Shadows]`), devirtualization and static-constructor triggers all take
  `INamedTypeSymbol`/`IMethodSymbol`/`IFieldSymbol`.
- **Roslyn already has symbols for referenced assemblies.** A DLL passed as a `MetadataReference`
  yields PE symbols for its types and members, with base types, interfaces, explicit implementations
  (`MethodImpl`), overrides, generic parameters and constraints decoded. With
  `MetadataImportOptions.All` it also imports private and internal members (the fields that layouts
  need). Source code that calls the library binds to exactly those symbols.
- **The library-mapping surface is written against `IOperation`.** `Intrinsics.cs` (BCL members →
  LLVM/csrt), `BclValueTypes`, the shadow corelib, delegate creation/invocation, interface dispatch,
  boxing and the generic-math paths take `IInvocationOperation`, `IObjectCreationOperation`,
  `IFieldReferenceOperation`, … and evaluate their operands themselves.

## Options

| | (a) IL → IOperation-shaped input to FunctionCodeGen | (b) separate IL → LLVM lowering | (c) decompile IL to C# (ICSharpCode.Decompiler), feed Roslyn |
|---|---|---|---|
| Correctness risk | Low: IL semantics are explicit (evaluation order, temporaries, overflow, defensive copies are already decided by the C#/F#/VB compiler); every library call goes through the same mapping as source | Medium: every BCL mapping must be re-implemented for IL call sites, two lowerings drift apart | **High and silent**: decompilation is not semantics-preserving by construction (pattern-based reconstruction of `?:`, `using`, `foreach`, lambdas, async); merging decompiled code into the user's compilation changes accessibility and overload resolution; unverifiable or non-C# IL (F#, VB, IL weavers, obfuscators) has no C# equivalent |
| Effort to first slice | Medium | Medium | Low |
| Effort to full coverage | Medium: the opcode set is small (~220) and closed; the library surface is shared | High: duplicates `Intrinsics`/corelib/delegate/interface lowering | Low for C#-compiled IL, unbounded for the rest |
| Compile time | Linear decode, no binding | Same as (a) | Decompile + re-parse + re-bind everything reachable (Roslyn-sized inputs: minutes) |
| Debuggability | `--dump-il`, diagnostics name the method and IL offset/opcode; DWARF can point at the IL offset (or the PDB's sequence points later) | Same | Synthetic sources, no mapping back to the package |
| M6 (Roslyn, csharpc as IL) | Scales: only reachable methods are decoded | Scales | Poor |

## Decision

**IL is a second method-body source for the same symbols** — option (a), implemented without a
Roslyn `ControlFlowGraph` (its classes are sealed) but sharing `FunctionCodeGen` itself, not only
`TypeMapper`/`ModuleCodeGen` as (b) would.

1. **Symbols.** Assemblies compiled from IL are added to the Roslyn compilation as ordinary metadata
   references; the codegen compilation imports them with `MetadataImportOptions.All`. Types and
   members of those assemblies count as *compiled by csharpc* (`ModuleCodeGen.IsUserType` /
   `IsUserMethod`), so they get layouts, method tables, interface maps, static fields and
   devirtualization exactly like source types. Source and IL code therefore share one object layout
   and one ABI and call each other directly.
2. **Bodies on demand.** `GetFunction` queues IL methods like corelib methods: only what the program
   reaches is decoded and lowered (a 5 MB package costs what the program uses of it). `EmitMethod`
   asks `IlAssemblySet` for the body (`ISymbol.MetadataToken` → `MethodDefinitionHandle` →
   `MethodBodyBlock`) instead of Roslyn for syntax.
3. **Tokens resolve to Roslyn symbols** (`Frontend/Il/IlAssemblySet.cs`): `TypeDef`/`MethodDef`/
   `FieldDef` through a token index of the assembly's PE symbols; `TypeRef`/`MemberRef` by name,
   arity and decoded signature against the referenced assembly symbol (following type forwarders);
   `TypeSpec`/`MethodSpec` through an `ISignatureTypeProvider` that builds constructed Roslyn types.
   Signatures are decoded in the **generic definition's** context (`!0`, `!!0` → the definition's
   `ITypeParameterSymbol`s), so `FunctionCodeGen`'s `TypeSubstitution` monomorphizes IL exactly as it
   monomorphizes source (one function per instantiation; no shared generics).
4. **Lowering** (`CodeGen/FunctionCodeGen.Il.cs` — blocks, stack, EH; `.IlInstructions.cs` — opcodes;
   `.IlCalls.cs` — call/callvirt/newobj): the importer splits the body into basic blocks
   (branch targets, EH region boundaries), simulates the evaluation stack, and spills the stack at
   block boundaries into per-block allocas (mem2reg builds the phis — the same trick the source path
   uses for flow captures). Each instruction is lowered either
   - **directly** to LLVM when its semantics are IL's own: locals/arguments, stack arithmetic with
     .NET's division/overflow checks (shared helpers), comparisons and branches, `switch`, field and
     array element access of csharpc-compiled types, `ldstr`, `ldnull`, `initobj`, `ldtoken` +
     `InitializeArray`, `localloc`, or
   - as a **synthetic `IOperation`** (`CodeGen/IlOperations.cs`: invocation, object creation,
     argument, field and property reference, delegate creation, array element, …) whose leaves are
     already-computed stack values, handed to the existing `EmitValue`. That is how an IL `call
     System.Console::WriteLine(int32)` reaches the same intrinsic, `newobj List<int>` the same corelib
     shadow, and `constrained. !!T callvirt IComparable<!!T>::CompareTo` the same per-instantiation
     interface dispatch as source.
5. **Corelib redirection** applies unchanged: synthetic operations carry the real BCL symbols and
   `S()`/`CoreLib.Map` redirect them as for source.
6. **Diagnostics.** Anything unsupported is a `CSLLVM0001` naming the method, the IL offset and the
   instruction with its operand resolved (`… [in 'Lib.Parser.Next()' (Lib.dll) IL_0012: calli …]`);
   missing library members stay `CSLLVM0002`. Unverifiable or malformed IL (stack underflow,
   inconsistent stack at a join, a backward branch with a non-empty stack) is rejected, never guessed at.
7. **Failures in code the program cannot reach.** A package's method tables and interface maps
   reference every virtual method of the types the program uses, so lowering a library pulls in code
   the program never runs (async stream helpers, serialization hooks). An IL method that fails to
   compile therefore becomes a *stub* that prints the diagnostic and aborts if it is ever called; at
   the end, a class-hierarchy analysis from the program's own code (direct references, plus every
   override/implementation a virtual or interface call site can reach) decides: failures in reachable
   methods are errors, exactly as in the source path; the others are `CSLLVM0007` warnings. Slots of
   methods whose *signature* csharpc cannot represent get the stub too — no call site can reach them
   without failing to compile first. This is NativeAOT's model (warnings plus throwing stubs), with one
   difference that keeps the rule: the stub cannot be caught; it ends the program with the message.

### How the listed concerns map

- **Generics:** definition-context decoding + `TypeSubstitution` (above). Generic virtual methods use
  the existing closed-world type switch; the class-hierarchy universe includes IL types.
- **Virtual dispatch, interfaces, layouts:** from the PE symbols (`IsVirtual`, `IsOverride`,
  `OverriddenMethod`, `ExplicitInterfaceImplementations`, `FindImplementationForInterfaceMember`), so
  a C# class can derive from an IL class and vice versa without any adapter. `newslot`/`final` and
  covariant returns come through Roslyn's PE override resolution. Explicit layout/`.pack`/`.size`
  (read from metadata, since they are pseudo-attributes) is rejected like `[StructLayout]` in source.
- **P/Invoke:** `GetDllImportData()` works on PE symbols, so IL `pinvokeimpl` methods use the existing
  stub generator (`ModuleCodeGen.PInvoke.cs`).
- **Exception regions:** try/catch/finally/fault/filter clauses become the same landing-pad +
  dispatch structure as source regions (ADR 0005): one landing pad per protected range listing the
  catch types of all enclosing handlers, a dispatch chain that tests catch types and runs filter
  blocks, and `leave`/`endfinally` routed through per-finally continuation selectors. The
  evaluation stack is empty on region entry/exit by ECMA rule (III.1.7.5); entering a handler
  pushes the exception object. Fault blocks are finally blocks that only the exceptional path enters.
- **Stack spilling around EH:** values live across a `leave` are impossible (the stack is emptied);
  values live across a call inside a try are SSA values or spills, which mem2reg handles with
  `invoke` edges like any other value.
- **`localloc`:** an `alloca` in the current block (bounded by the same stack-size rule as
  `stackalloc` in source).
- **`constrained.`:** mapped to the source form (`IInvocationOperation.ConstrainedToType` + an
  instance of type `T` held by address), so after substitution reference types load the pointer and
  value types call their implementation directly or box, as the source path does.
- **`ldtoken`/`RuntimeHelpers.InitializeArray`/`CreateSpan`:** the `newarr; dup; ldtoken <RVA field>;
  call InitializeArray` idiom copies the field's RVA data into the array; `ldsflda` of an RVA static
  (`ReadOnlySpan<byte>` literals) yields a pointer to a constant global holding the data.
- **Static constructors:** IL `.cctor` + the type's `beforefieldinit` flag drive the existing lazy
  `ClassInit` machinery: precise types check on entry of static methods/constructors, beforefieldinit
  types on first static field access (CoreCLR's behaviour).
- **Unverifiable IL** (pointer arithmetic, `cpblk`, `initblk`, unaligned/volatile prefixes) is
  lowered literally where it is well defined; constructs that depend on the GC's knowledge of
  interior pointers are fine because csharpc's GC is conservative and non-moving (ADR 0004).
- **What Roslyn's symbols hide.** Field-like events' backing fields are not exposed by Roslyn's PE
  symbols; the event symbol stands for its field and gets the slot in the layout. Struct layout flags
  (`.pack`, `.size`, explicit layout) and `beforefieldinit` are read from the metadata directly.
- **Compiler idioms of C#.** `DefaultInterpolatedStringHandler` (every compiled `$"..."`) is lowered
  like source interpolation (a StringBuilder, same per-hole formatting); operator methods of built-in
  types (`decimal.op_Addition`, generic math through `constrained.` on primitives), span/`Index`
  conversion operators and `string.op_Equality` reach the same lowering as the C# operators.
- **Framework assemblies as IL.** ReadyToRun images (the shared framework) keep their full IL and are
  accepted; an IL reference replaces the reference-pack assembly of the same name, so a framework
  assembly can be bound and compiled from its implementation IL (tried with
  `Microsoft.Extensions.Primitives.dll` — see the status section).

### Out of scope

`Reflection.Emit`, `System.Linq.Expressions` compilation, mixed-mode/C++/CLI assemblies (native
code inside the PE), `calli` (until function pointers from IL are needed), `arglist`/`mkrefany`/
`refanyval`/`refanytype` (TypedReference), `jmp`, multi-module assemblies. `tail.` is compiled as an
ordinary call (warning `CSLLVM0006`: deep tail recursion can overflow where CoreCLR would not).

### Project input

`-r X.dll`, `<Reference Include="X"><HintPath>X.dll</HintPath></Reference>` and `PackageReference`s
are all "assemblies without sources". For packages the driver runs
`dotnet msbuild -restore -t:ResolvePackageAssets -getItem:RuntimeCopyLocalItems` — the runtime
(implementation) assemblies NuGet selected for the target framework, transitive closure included,
i.e. what `dotnet build` copies next to the app — and binds and compiles against those. (Binding
against `ref/` assemblies while compiling `lib/` ones would need a second symbol world; the runtime
assets bind fine because they are a superset of the reference surface.)

## Consequences

- One lowering, two body sources: every improvement to the source path (intrinsics, corelib,
  dispatch, EH) applies to IL call sites, and IL-specific code is limited to the stack machine,
  token resolution and a handful of IL-only idioms.
- The synthetic operations implement Roslyn's public `IOperation` interfaces, which Roslyn marks
  `[InternalImplementationOnly]` (analyzer RS1009, suppressed for these files). A Roslyn upgrade that
  adds interface members breaks the build loudly, not silently; the classes are small.
- Packages pay only for what is reachable; decoding is linear in the reachable IL.
- C#-compiler idioms that csharpc implements by its own transform in source — async state machines
  driven by `AsyncTaskMethodBuilder`, iterator classes using `Environment.CurrentManagedThreadId` —
  need corelib implementations of those BCL types before IL that uses them works; until then they are
  reported as missing library members (CSLLVM0002), not miscompiled.
- `typeof(T).Assembly`, `Assembly.GetName()` and other identity queries see one closed-world program;
  they are rejected where reached.

## Status of the implementation

Measured 2026-10-23 on the branch that introduced the IL front-end.

**Opcodes.** All of ECMA-335 partition III except `calli`, `jmp`, `arglist`, `mkrefany`, `refanyval`,
`refanytype` and `ckfinite`, which are diagnosed. Prefixes: `constrained.`, `volatile.` (acquire/release
accesses), `readonly.`, `unaligned.` (ignored: x86-64/AArch64 handle unaligned access), `tail.` (warning).

**Features exercised against `dotnet run`** (`tests/il.sh`, `tests/projects/il/`, -O0 and -O2):
classes with fields, auto-properties, virtual/abstract/override/`base` calls, sealed classes, a C#
class deriving from an IL class; interfaces incl. default interface methods and static abstract
members; generic classes/methods with constraints, generic virtual methods, nested generic types,
generic static constructors; structs (in-place construction, `readonly` members, operators,
`IEquatable`), records (class and struct, synthesized `ToString`/`Equals`/`GetHashCode`/`with`),
enums; try/catch/finally, filters (`when`), rethrow, nested finally on early `return`, exceptions
crossing the source/IL boundary both ways; lambdas and closures, `<>c` caches, delegates,
field-like events, iterators (`yield`), LINQ (method and query syntax, anonymous types); string
interpolation (`DefaultInterpolatedStringHandler`), string `switch` (hash + `op_Equality`),
`StringBuilder`, `List`/`Dictionary`; `decimal`, checked arithmetic, generic math (`INumber<T>`);
`stackalloc`, `fixed`, pointers, `ReadOnlySpan<byte>` RVA literals, array initializers; `lock`,
`using`; `[DllImport]` P/Invoke; `static readonly` fields with `beforefieldinit` and precise cctors.

**Real NuGet packages** (restored into a fresh directory, compiled with `-r` / `PackageReference`):

| Package | Result |
|---|---|
| Ardalis.GuardClauses 5.0.0 | ✅ compiles; output byte-identical (`tests/projects/il/IlNuGetApp`) |
| ByteSize 2.1.2 | ❌ library surface: `NumberFormatInfo.CurrentInfo` |
| Semver 3.0.0 | ❌ library surface: `List<T>.AsReadOnly`, `BigInteger.Parse/ToString(IFormatProvider)`; its dependency `Microsoft.Extensions.Primitives` compiles from the shared framework's IL except `ResourceManager` (resource strings) |
| SimpleBase 4.0.2 | ❌ library surface: `Range.EndAt`, `string.GetHashCode(StringComparison)`; unreachable async helpers become CSLLVM0007 stubs as designed |

Every failure is a diagnostic naming the method and instruction; none of the four produced wrong
output.

**Framework IL at scale.** `System.Collections.Immutable.dll` from the shared framework (ReadyToRun,
~1 MB of IL), used by a program exercising `ImmutableList`/`ImmutableArray`/`ImmutableDictionary`/
`ImmutableStack`: every reachable method decodes and lowers except 19 sites, all library surface —
`Array.Copy`/type tests on values typed `System.Array`, `Math.Max(byte, byte)`, `Array.LastIndexOf`,
`ResourceManager` (resource strings), `AppContext.TryGetSwitch`, `InlineArray2<T>` (params spans).
No IL construct was missing. The remaining blockers are BCL surface (roadmap M2), not IL constructs: once the IL is
decoded, a package needs exactly the library members a source program calling the same APIs needs.

**Known semantic differences** (documented, explicit where observable): filters run during dispatch
in the frame that catches (as in the source path), after inner `finally` blocks have run — .NET runs
them in the first pass; `tail.` is not a guaranteed tail call. (An exception thrown inside a filter
counts as `false`, as in .NET: filter code has its own catch-all landing pad.)

## Follow-up: phase 2 (2026-10-23)

Done on top of the vertical slice (tests: `tests/il.sh` IlAsyncApp, IlResourcesApp, IlFrameworkApp;
samples 343–346):

- **Compiled `async`.** The builders (`AsyncTaskMethodBuilder(<T>)`, `AsyncValueTaskMethodBuilder(<T>)`,
  pooling variants, `AsyncVoidMethodBuilder`, `AsyncIteratorMethodBuilder`), awaiter
  `OnCompleted`/`UnsafeOnCompleted`, `YieldAwaitable`, `ExceptionDispatchInfo`,
  `ManualResetValueTaskSourceCore<T>` and `ValueTask`s over `IValueTaskSource` are corelib/intrinsics
  over csrt tasks ([10-async.md](../10-async.md#async-state-machines-compiled-from-il)). Covered against
  `dotnet run`: synchronous completion, exceptions before/after the first await, await in
  catch/finally, `ConfigureAwait`, `ValueTask(<T>)`, generic/recursive/instance async methods,
  `WhenAll`/`Task.Run`, async lambdas, custom awaiters, async `void`, cancellation, async iterators
  (yield without await, `finally` on early break, exceptions, manual enumeration, `WithCancellation`).
  An `OperationCanceledException` escaping an async method now cancels the task in both paths.
- **Resource strings.** Every IL assembly's embedded `.resources` (string entries) is read at compile
  time into one constant; the corelib `ResourceManager` serves `GetString` by base name, so framework
  `SR` messages and package `Properties.Resources` messages are .NET's (neutral culture).
- **AppContext.** `GetData`/`SetData`/`TryGetSwitch`/`SetSwitch` with .NET's semantics, starting from
  runtimeconfig.json's `configProperties`: the project's `RuntimeHostConfigurationOption` items, or a
  file-based app's defaults (`dotnet run app.cs`) for single files. Host properties
  (`APP_CONTEXT_BASE_DIRECTORY`, `TRUSTED_PLATFORM_ASSEMBLIES`, ...) read as null.
- **System.Collections.Immutable from the shared framework's IL** now compiles and matches .NET
  (`tests/projects/il/IlFrameworkApp`); its 19 remaining sites were library surface (`System.Array`
  parameters and type tests, `Array.LastIndexOf`, `typeof(T).IsValueType`, `InlineArrayN<T>` for
  `params` spans, structural array interfaces, a missing `SortedDictionary` constructor overload).
- **Compile time.** IL bodies are decoded once per generic definition (decoding was ~1% of lowering);
  token resolution was already cached per assembly. The profile's hot spots were elsewhere: shadow
  lookups for unmapped library types rebuilt metadata names on every `S()`/`TypeOf()` (~7% of the
  compile, now cached) and every LLVM type getter was a P/Invoke. `csharpc -v` prints CPU time,
  allocations and GCs per phase and splits lowering between IL, source and corelib methods.

  Immutable program above, `-O0`, before → after (CPU seconds; the shared 4-core machine made wall
  time unreliable): Roslyn parse+bind ~7 s (unchanged; ~3 s with csharpc published ReadyToRun),
  lowering ~14–17 s → ~13–14 s for 1644 IL + 1390 corelib methods (184 MB allocated, 30 gen0 GCs),
  LLVM backend ~3–6 s; peak RSS ~290 MB. At `-O2` the LLVM pipeline dominates: optimization ~18 s and
  code generation ~14 s, both single-threaded.
- **Survey harness** (`tools/il-survey/`): the top pure-IL nuget.org packages compiled with
  `--il-survey` (every non-generic method lowered), diagnostics ranked by packages affected
  ([18-compatibility.md](../18-compatibility.md#il-front-end-survey)).

**Known differences** (explicit, not silent): `WithCancellation` with a cancelable token on an async
iterator compiled from C# source throws `NotSupportedException`; resource lookups for a base name with
no embedded resources throw `NotSupportedException`; `IStructuralComparable/Equatable` between arrays
of different element types throws `NotSupportedException`.

## The program as IL (2026-10-30)

Inside the dotnet pipeline (ADR 0016) csharpc's input is what csc built, not sources, so the IL
front-end has to take the program's own assembly too: `csharpc app.dll -o app` (or `app.exe`).

- **Input.** One `.dll`/`.exe` among the inputs makes it the program (mixing it with `.cs` files is an
  error; other assemblies stay `-r`). `Frontend/Il/IlProgram.cs` reads its name and the CLI header's
  `EntryPoint` token — for an async or top-level Main that is csc's synthesized sync `<Main>` wrapper,
  which calls .NET 11's `AsyncHelpers.HandleAsyncEntryPoint` (a corelib shadow: wait, rethrow the original
  exception). A library (no entry point) or a native entry point is rejected.
- **Compilation.** The assembly joins the IL references like a `-r`; the Roslyn compilation has no
  sources of the program (only the corelib facade) and is named `<name>.csharpc` so the two identities
  never clash. `ModuleCodeGen.MainIl`/`EntryPoint` replace Roslyn's `GetEntryPoint`, and Main (with what
  it reaches, and the module initializers) is lowered before `main()` registers the builtin types'
  interfaces, as the eager source Mains are. Everything else is on demand, through the stubs and the
  class-hierarchy analysis above.
- **Identity.** `Assembly.GetEntryAssembly()`, `GetExecutingAssembly()`, `Type.Assembly` and
  `Assembly.Load` report the IL assembly's identity; the default output name is its assembly name.
- **Module initializers.** csc calls them from `<Module>..cctor`, which Roslyn's symbols hide: its IL
  (a list of `call`s) is read and run before Main, in csc's order. Any other module constructor (IL
  weavers) is a diagnostic.
- **Runtime configuration.** `app.runtimeconfig.json` next to the assembly — what `dotnet app.dll` reads —
  gives AppContext's `configProperties` (`EntryPointFilePath` of a file-based app included), the
  globalization switches (invariant mode, predefined cultures) and `IsReflectionEnabledByDefault`. A
  file-based app's `#:` directives were applied by the build that made the assembly.
- **Framework IL.** `FrameworkIl.Select` already followed the IL assemblies' references: for an IL
  program that is the whole choice, the program's assembly references being the "uses".

Semantics that sources gave the lowering and IL has to restate:

- A `call` in IL is never `[Conditional]`-omitted (csc decided with its own symbols); a `newobj` of a
  delegate is always a new object (C#'s method-group cache is a static field in the IL); `ldarg` decides
  whether Main reads `args`; `InitializeArray` on a rectangular array writes after its bounds; a
  constrained `object::Equals/GetHashCode` on an empty `T?` answers without boxing; a struct's
  `base.Equals(obj)` is a non-virtual call on a box of `this`; a boxed enum (`box E`) still has its
  static type for `Enum.CompareTo`/`IsDefined`; `typeof(E)` is `ldtoken` + `GetTypeFromHandle` for
  `Enum.GetValues(Type)` & co.; body-less `extern` methods throw `BadImageFormatException` and
  `[UnsafeAccessor]` externs are accessors; compiled async methods save and restore the
  ExecutionContext (AsyncLocal, cultures) in `Start` and resume in the context captured by each await,
  as `AsyncMethodBuilderCore` and `AsyncStateMachineBox` do; `DefaultInterpolatedStringHandler` formats
  library structs without an IFormattable box (BigInteger, Int128, Index, Range) as their own
  `ToString(format, provider)` (also a fix for `string.Create(provider, $"...")` in source, sample 700).
- **Stack traces** (ADR 0012) come from the assembly itself: a frame per method of the program's
  assembly, named from its metadata (the names csc chose for lambdas, local functions and closures; a
  state machine's `MoveNext` is its kickoff method, `+MoveNext()` for iterators), and the line of the
  last non-hidden sequence point at or before the call's IL offset in the PDB beside the assembly (or
  embedded), as `System.Diagnostics.StackTrace` reads it. The landing pads of IL try regions record the
  frame like the source path's, `rethrow` doesn't list it again, and an exception leaving a finally run
  on the normal path lists the method twice, as CoreCLR's funclets do (the method's first sequence
  point; the try's first one when `[Debuggable]` says the IL is optimized). Traces are recorded when the
  assembly names an exception type or `StackTrace`; without a PDB, frames have no location, as in .NET.
  The program's own types (the IL assembly's) keep their custom attributes for `Type.GetCustomAttributes`
  as source types do.
- **Reflection-based `System.Text.Json`** gets the source path's synthesized contexts, with the roots read
  from the program's bodies, and the program grants csharpc's binding compilation internals access so the
  JSON generator sees its types as it does from source (docs/12-corelib.md#systemtextjson). A call on a
  root the contexts can't cover (a private nested type, a compiler-generated one) is a diagnostic where it
  is lowered. **`[GeneratedRegex]`** members (`ldsfld <Generated>::Instance; ret`, the SDK generator's
  output built on RegexRunner internals) become cached corelib interpreters built from their attribute.
- What IL can't reach yet is explicit: `dynamic` (ADR 0019 binds from source; see its "From IL" note),
  and turning an exception into text in IL that names no exception type (no traces were recorded).

**Differential mode.** `tests/run.sh --via-il` builds every sample with `dotnet build NAME.cs` (the build
`dotnet run` makes, runtimeconfig.json included) and compiles `NAME.dll`; the recordings are the same.
The informational CI workflow `il-main.yml` runs it on Linux x86-64 at -O0 and -O2 against
`tests/il-main-known-failures.txt`.

Measured 2026-10-30 (Linux x86-64, -O0 and -O2; "Program as IL" run 41151570621, then on `main` with
expression trees, whose samples 672–676 pass through IL too): **459 of 476 samples** pass through IL
(96.4 %), the source path being 476/476. The 17 failures, by cause:
reflection-based JSON 8 (316–321, 325, 535),
`dynamic` 7 (680–686), `[GeneratedRegex]` output 1 (305: it sets `Regex`'s protected fields, which the
corelib's Regex doesn't have), non-generic enumeration of a rectangular array 1 (393). All of them are
diagnostics or a run-time `NotSupportedException`; none prints wrong output. The first runs found
72 failures, 19 of them wrong output with .NET's exit status (module initializers not run, `[Conditional]`
calls dropped, builtin types' interfaces registered before Main was lowered, `new` delegates cached,
ExecutionContext not flowing through compiled async, BigInteger formats ignored, stack traces printed
without the program's frames), the rest crashes and diagnostics.

Next for the IL path: JSON metadata for types defined in IL (the source generator over metadata
symbols, which needs access to the program's internal types), `dynamic` call sites from their IL
(`CallSite<T>` + the binder's arguments), `[GeneratedRegex]` output, and debug info (`-g`) from the PDB.

## Next steps

Toward **arbitrary pure-IL NuGet packages** (ordered; size in focused days):

1. ~~Compiled `async`~~ and ~~resource strings / `AppContext`~~: done (phase 2, above).
2. `typeof(X).Assembly` as a compile-time identity (`ResourceManager(baseName, assembly)` in
   `Properties.Resources` designers, `Assembly.GetName()`), satellite resource assemblies for
   non-neutral cultures. (1–2)
3. BCL surface packages use first — measured by compiling the top-N pure-IL packages from nuget.org:
   `ReadOnlyCollection<T>`/`AsReadOnly`, `System.Array` as a static type (`Array.Copy`,
   `is Array`), `CultureInfo`/`NumberFormatInfo` objects (invariant), `BitConverter`, more `Math`
   overloads, `string.GetHashCode` overloads, `InlineArray`. A survey harness (`tools/compat`-style)
   turns this into a ranked list. (ongoing, M2)
4. `calli` and function pointers from IL; `ckfinite`; TypedReference only if a package needs it. (1)
5. Package assets beyond `lib/`: RID-specific runtime assets (`runtimes/<rid>/lib`), native assets
   linked or copied for P/Invoke, satellite resource assemblies; `ref/` vs `lib/` binding (bind
   against `ref/` symbols, map to `lib/` bodies by name) for packages whose surfaces differ. (2)
6. Compile-time scale: decoding once per definition and cached token resolution are done. Next, in
   order of measured payoff: ship csharpc ReadyToRun (halves Roslyn binding; NativeAOT once M6
   compiles csharpc), parallel LLVM code generation (full-LTO-style module partitioning, e.g.
   lld's `--lto-partitions`, or splitting the module before `-O2`), then parallel lowering (needs
   one LLVM context per thread and symbol-keyed cross-unit references). (2–3)

Toward **M6 (csharpc and Roslyn compiled by csharpc)**:

1. Everything above (Roslyn and csharpc use async, resources, `Lazy<T>`, immutable collections).
2. ~~Framework IL as a library strategy~~: done, [ADR 0013](0013-framework-il.md). Framework
   assemblies the program uses are compiled from the shared framework's IL when csharpc implements
   none of their types (System.Reflection.Metadata, System.Collections.Immutable and the
   Microsoft.Extensions abstractions compile and match `dotnet run`); `--framework-il` forces or
   disables the choice.
3. Reflection used by Roslyn at startup (`typeof(X).Assembly`, resource lookup, `Activator` for
   analyzers): generated metadata for what's statically known, diagnostics for the rest (NativeAOT's
   rd.xml-style model). (5+)
4. Roslyn source vs IL: compile Roslyn from source (the primary path) with its dependencies from IL,
   then csharpc itself; measure compile time and memory of a ~40 MB-of-IL closed world, and add
   codegen units/ThinLTO (ADR 0006) where needed. (5+)
5. The LLVM C API through `[LibraryImport]` in csharpc itself already works for P/Invoke from C#
   source; validate the same stubs when csharpc's own `Native.cs` is compiled. (1)

## Alternatives considered

See the table: (b) was rejected because it duplicates the IOperation-based library mapping and
guarantees divergence between source and IL call sites; (c) because a decompiler can silently change
semantics, cannot express all valid IL, and does not scale to M6. A decompiler remains useful as an
inspection aid (`ilspycmd`) when debugging the importer.
