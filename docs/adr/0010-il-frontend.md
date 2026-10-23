# ADR 0010 — IL front-end: IL as a method-body source for Roslyn's metadata symbols

- **Status:** Accepted (2026-10-23). Vertical slice implemented; see "Status of the implementation".

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
4. **Lowering** (`CodeGen/FunctionCodeGen.Il.cs`): the importer splits the body into basic blocks
   (branch targets, EH region boundaries), simulates the evaluation stack, and spills the stack at
   block boundaries into per-block allocas (mem2reg builds the phis — the same trick the source path
   uses for flow captures). Each instruction is lowered either
   - **directly** to LLVM when its semantics are IL's own: locals/arguments, stack arithmetic with
     .NET's division/overflow checks (shared helpers), comparisons and branches, `switch`, field and
     array element access of csharpc-compiled types, `ldstr`, `ldnull`, `initobj`, `ldtoken` +
     `InitializeArray`, `localloc`, or
   - as a **synthetic `IOperation`** (`CodeGen/Il/IlOperations.cs`: invocation, object creation,
     argument, field reference, delegate creation, array element/creation, …) whose leaves are
     already-computed stack values, handed to the existing `EmitValue`. That is how an IL `call
     System.Console::WriteLine(int32)` reaches the same intrinsic, `newobj List<int>` the same corelib
     shadow, and `constrained. !!T callvirt IComparable<!!T>::CompareTo` the same per-instantiation
     interface dispatch as source.
5. **Corelib redirection** applies unchanged: synthetic operations carry the real BCL symbols and
   `S()`/`CoreLib.Map` redirect them as for source.
6. **Diagnostics.** Anything unsupported is a `CSLLVM0001` naming the method, the IL offset and the
   opcode or feature (`'Lib.Parser.Next()' IL_0012: calli is not supported by csharpc yet`); missing
   library members stay `CSLLVM0002`. Unverifiable or malformed IL (stack underflow, inconsistent
   stack at a join, branch into a region) is rejected with a diagnostic, never guessed at.

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

### Out of scope

`Reflection.Emit`, `System.Linq.Expressions` compilation, mixed-mode/C++/CLI assemblies (native
code inside the PE), `calli` to managed function pointers with unknown signatures until function
pointers are needed, `arglist`/`mkrefany`/`refanyval` (TypedReference), `jmp`, `tail.` (lowered as
a normal call where that is unobservable; deep tail recursion may overflow where CoreCLR would not —
reported as a warning), multi-module assemblies.

### Project input

`<Reference Include="X.dll">` and resolved `PackageReference` runtime assets are both "assemblies
without sources". The driver collects them from MSBuild (`ReferencePath` / `ReferenceCopyLocalPaths`
after `ResolveAssemblyReferences`), binds user code against them (as `dotnet build` does) and lowers
reachable IL from the *implementation* assembly. The first slice handles explicit DLL references
(`-r` on the command line, `<Reference>` items in projects); NuGet resolution reuses the same path.

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

## Alternatives considered

See the table: (b) was rejected because it duplicates the IOperation-based library mapping and
guarantees divergence between source and IL call sites; (c) because a decompiler can silently change
semantics, cannot express all valid IL, and does not scale to M6. A decompiler remains useful as an
inspection aid (`ilspycmd`) when debugging the importer.
