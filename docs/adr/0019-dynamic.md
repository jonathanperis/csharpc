# ADR 0019 — `dynamic`: bound at compile time, per run-time type, with Roslyn

- **Status:** Accepted (2026-10-30), implemented (`CodeGen/ModuleCodeGen.Dynamic*.cs`, `CodeGen/FunctionCodeGen.Dynamic.cs`,
  `CodeGen/DynamicSite.cs`, `CodeGen/DynamicErasure.cs`, `corelib/Dynamic/DynamicObjects.cs`).
- **Context:** [ADR 0006](0006-closed-world-compilation.md) (closed world), [ADR 0013](0013-framework-il.md) (framework IL),
  [ADR 0016](0016-nativeaot-parity-and-end-state.md) (NativeAOT parity), [ADR 0018](0018-reflection-metadata.md) (reflection metadata).

## Context

C# compiles a `dynamic` operation to a call site: `CallSite<Func<...>>.Create(Binder.InvokeMember(flags, "Foo", typeArgs,
typeof(Context), argumentInfos))`, then `site.Target(site, receiver, args...)`. On the first call (and for every new
combination of run-time types) Microsoft.CSharp's runtime binder — a port of the C# compiler's semantic analysis — binds the
operation "as if the user had written it" against the run-time types of the dynamic operands (the *best accessible* type:
the nearest type the calling context can see, `TypeManager.GetBestAccessibleType`), builds an expression tree for the rule,
and the DLR compiles it into the site's delegate. Under NativeAOT the binder runs on reflection metadata and the rule
delegates are interpreted (System.Linq.Expressions' LightCompiler).

What NativeAOT does is a weak target. The corpus program that measured parity (`using-dynamic`) works under NativeAOT
because it only produces a binder error; a dynamic call to a method nothing else references fails with "'Point' does not
contain a definition for 'Add'" (the trimmer removed it), and with `[DynamicDependency]` keeping it the binder fails with an
`ArgumentNullException`. `built-in-types` (`value.GetType()` on a dynamic) crashes under NativeAOT.

Taking the same route through the IL front-end — Microsoft.CSharp and System.Linq.Expressions compiled from the shared
framework's IL (ADR 0013) — was measured on a hand-written call site: over ten minutes at `-O0`, then 159 unsupported
constructs (the Reflection.Emit compiler, full reflection over fields and events, `MakeGenericMethod` for the update
delegates). Even complete, it would carry the binder (~3.7 MB under NativeAOT) into every program using `dynamic` and run
an interpreter on every call.

## Decision

**csharpc binds every dynamic operation at compile time, with Roslyn, once per run-time type that can reach it.**

A closed world knows every type a value can have at run time: the types the program has method tables for. For each
dynamic operation csharpc emits a *site function* that switches on the method tables of the dynamic operands; each case
calls a function holding the code Roslyn bound for that combination of types.

1. **Binding = what the runtime binder does.** Each case is bound *speculatively*
   (`SemanticModel.TryGetSpeculativeSemanticModel`) at the operation's own source position — the binder's context for
   member lookup and accessibility — as a small block that declares a local per operand and evaluates the operation on
   them: `{ global::Point __csdyn0; int __csdyn1; var __csdynr = __csdyn0.@Add(__csdyn1); }`. Operands follow
   `CSharpArgumentInfo`: dynamic ones are locals of the best accessible run-time type, statically typed ones locals of
   their (substituted) static type, constants are constant expressions of the same type and value, a dynamic `null` is
   the null literal, a struct receiver variable is passed by reference and a boxed struct receiver is the box's payload
   (both are mutated, as on .NET). The block's control-flow graph is lowered like any other.
2. **Which types get a case.** Per operand role (ModuleCodeGen.DynamicBinding.cs): a receiver needs a case for every type
   with a member of that name (others: the binder's "does not contain a definition"); an argument for every type
   implicitly convertible to some candidate parameter other than `object` (others bind as `object`, which selects the
   same overload) — and for every type, up to a nesting depth of 2, when a candidate is generic and infers from it; an
   operator operand for value types, strings, delegates and types with user-defined operators or conversions; a
   conversion for every type with a conversion to the target (reference conversions share one case).
   Reference equality between two "plain" classes is checked at run time (unrelated classes are a binder error).
3. **Errors are the binder's.** A failed binding throws `RuntimeBinderException` with Microsoft.CSharp's message
   (`Strings.resx`), derived from Roslyn's symbol information: no member, bad argument count or types, ambiguity,
   static/instance mismatch, inaccessible members, read-only properties, void results, operator and conversion errors.
   Messages that name a run-time type the case doesn't know (the default case) are completed at run time from a
   generated name table. A case csharpc can't lower (a library member it doesn't implement yet, for a type the value
   may never have) throws `NotSupportedException` when reached instead of failing the compile.
4. **ExpandoObject and DynamicObject** follow their meta-objects' rules in C# that calls `corelib/Dynamic/DynamicObjects.cs`:
   an ExpandoObject's member get/invoke reads its dictionary first, a set stores; a DynamicObject binds its real members
   first, then the matching `Try*` override (with binders named and shaped like Microsoft.CSharp's), then the binder's
   error. A member `TryInvokeMember` declines is fetched with `TryGetMember` and invoked dynamically — a dynamic
   operation inside bound code, bound in the context of the site that contains it. Other `IDynamicMetaObjectProvider`
   implementations (custom expression-tree meta-objects) are an explicit `NotSupportedException`.
5. **The fixed point.** The set of run-time types grows while the program is lowered (and bound cases can create
   types), so site bodies are emitted in codegen's fixed point and re-emitted while a site gains a type it dispatches on;
   cases are kept across re-emissions. The type-name table is emitted after the fixed point.
6. **`dynamic` is `object` at run time.** `CoreLib.Map` erases it from every symbol codegen reads, so `List<dynamic>`,
   `Task<dynamic>` and `dynamic[]` are the same types (layouts, method tables, statics) as their `object` forms.
7. **The IL front-end** rejects csc's call sites (`Microsoft.CSharp.RuntimeBinder.Binder.*`) with an explicit diagnostic:
   from IL there is no source position to bind at, and the binder's accessibility context (internals of the library's
   own types) has no public Roslyn equivalent.

## Consequences

- `dynamic` programs print what `dotnet run` prints: samples 670–675 (members, overloads by run-time type, named/optional/
  params/ref/out arguments, constructors, generic inference, operators, conversions, indexers, events, delegates, struct
  receivers, ExpandoObject, DynamicObject, generic code, lambdas, iterators, async, field initializers, binder errors);
  both corpus programs that use `dynamic` (`using-dynamic`, which NativeAOT runs; `built-in-types`, which it doesn't).
- Speed: a bound case is a type test and a direct call — no binder, no rule cache, no interpreter. A loop of 5M
  `acc += p.Add(i); p.X = i & 7;` on `dynamic` values takes 62–70 ms with csharpc, 308–403 ms on the JIT; NativeAOT
  can't run it (2026-10-30, shared VPS).
- Size is pay-for-play: programs without `dynamic` are unchanged (`tests/size.sh`). A program using it pays for the cases
  of its sites: the benchmark is 234 KB (NativeAOT: 3.7 MB); the 75-site binder-error sample is 767 KB.
- Compile time grows with sites × relevant types: the 75-site sample binds in ~2.3 s (`CSHARPC_DYNAMIC_TRACE=1`
  prints per-site planning and emission times; `=2` also each case).
- Not supported yet, each an explicit diagnostic or NotSupportedException: `dynamic` in IL (above), `await` on a
  dynamic value, object initializers on a dynamically bound constructor call, a dynamic value passed by `ref`/`out`, a
  simple-name dynamic call through a struct's `this`, generic inference with a type nested deeper than 2, run-time types
  csharpc can't name (generic types over anonymous types), custom `IDynamicMetaObjectProvider`s, and run-time types
  csharpc has no table for (tasks share one method table across `Task<T>`).
