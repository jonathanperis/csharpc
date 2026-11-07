# ADR 0018 — Member reflection over compile-time metadata, as NativeAOT keeps it

- **Status:** Accepted (2026-10-25). Builds on [ADR 0006](0006-closed-world-compilation.md) (closed world)
  and [ADR 0016](0016-nativeaot-parity-and-end-state.md) (NativeAOT parity). Roadmap: M7 (the
  TechEmpower app).

## Context

ASP.NET Core starts up through reflection: dependency injection reads constructors and their parameters
and invokes them (`Type.GetConstructors`, `ConstructorInfo.Invoke`, `ParameterInfo.DefaultValue`), closes
open generic registrations (`typeof(OptionsManager<>).MakeGenericType(...)`), binds middleware by its
`Invoke` method, reads `[FromServices]`-style attributes, and copies route values out of anonymous
objects through properties (`PropertyHelper`). csharpc used to know only `System.Type` itself
([12-corelib.md](../12-corelib.md#type-metadata-systemtype-reflection)), so the TechEmpower app could not
compile.

The JIT answers any reflection query from the IL metadata it loaded. A closed world has no such
metadata, and keeping all of it would cost size and defeat dead-code elimination. NativeAOT's answer:
ILCompiler keeps metadata (and an *invoke stub* per invokable method) only for what the program can
reach through reflection, as the trimmer's dataflow proves it: `[DynamicallyAccessedMembers]` (DAM)
annotations on parameters, fields, return values and generic parameters, `typeof(X).GetMethod("Name")`
with a statically known `X` and constant name, and the instantiations it compiles. A query outside that
set gets less than the JIT would give (`null`, an empty list, or
`MissingMetadataException`/`NotSupportedException`), and the trimmer warns about it at compile time.

## Decision

csharpc keeps reflection metadata the same way, generated at compile time by
`ModuleCodeGen.Reflection.cs` and read by C# in the corelib (`corelib/Reflection/Members.cs`,
`Properties.cs`, `Invokers.cs`).

1. **What is kept (the dataflow).** A type's members of the kinds DAM names
   (`PublicConstructors`, `NonPublicConstructors`, `PublicMethods`, `NonPublicMethods`, `PublicProperties`,
   `NonPublicProperties`, `PublicFields`, `NonPublicFields`) are kept when:
   - a `typeof(X)` argument flows into an annotated parameter (both front-ends,
     `NoteDynamicallyAccessedArguments`);
   - a generic method or type is instantiated with `X` for an annotated type parameter
     (`NoteDynamicallyAccessedInstantiation`), e.g. `services.AddSingleton<TService, TImpl>()`;
   - `typeof(X).GetMethod/GetProperty/GetField/GetConstructor(...)` is called on a statically known `X`
     (`NoteReflectedType`). With a constant name only the members called that are kept, as ILLink does.
     A type described by name is listed again in full, in RuntimeType's order, once something asks for
     all of a kind.
   Open generic definitions marked this way (`typeof(Impl<>)`) keep the same members for every closed
   instantiation the program has. A pair of open `typeof` arguments, one of them annotated (DI's
   `ServiceDescriptor(typeof(IOptions<>), typeof(OptionsManager<>))`), registers the implementation for the
   service. Each closed instantiation of the service the program creates then gets the implementation
   instantiated with the same arguments, if the constraints hold (`SatisfiesConstraints`, the CLR's
   rules). That is the closed world `MakeGenericType` can answer from. Codegen alternates describing
   types with the lowering worklists until nothing new appears, because describing a member creates
   method tables and invoke stubs that may reach new code.
2. **Records.** The kept members become constant tables: `csrt_ReflectionInfo` per type (its
   constructors, methods, properties, attributes, and which kinds were kept), `csrt_MethodRecord`
   (name, declaring type, return type, parameters with names, attributes and default-value factories,
   method attributes, an *invoke stub*, and the function itself for `CreateDelegate`), and
   `csrt_PropertyRecord` (name, type, accessor records, attributes), and `csrt_FieldRecord` (name, declaring
   type, type, a getter and setter stub, attributes, `FieldAttributes`; listed in CoreCLR's order: instance
   fields, static fields, constants, then the non-private fields of base types). Attributes are factories that
   construct the attribute object on demand. The layouts are in `src/runtime/csrt.h`, mirrored by
   `ReflectionRecords` in the corelib.
3. **Invocation.** Each invokable method gets a generated stub `object Invoke(object target, object[] args)`
   that unboxes the arguments, calls the method and boxes the result. `MethodBase.Invoke`,
   `ConstructorInvoker`/`MethodInvoker` and `PropertyInfo.GetValue/SetValue` all go through it.
   `MethodBase.Invoke` wraps exceptions in `TargetInvocationException`, as .NET does. `MethodInfo.CreateDelegate` binds the record's function to a
   delegate whose `Invoke` signature is known at compile time (the delegate types `typeof(D)` names).
4. **Explicit failure, never a wrong answer.**
   - Asking a type for a kind of member that wasn't kept throws `NotSupportedException` ("... is not
     supported by csharpc yet"); it doesn't return an empty list.
   - `MakeGenericType` with an instantiation the program doesn't have throws `NotSupportedException`
     naming it.
   - An invoke stub csharpc can't lower yet (a library method without an implementation, an argument it
     can't unbox) throws when invoked, while the member's other metadata stays readable.
   - What NativeAOT can't do at all (`Reflection.Emit`, `MakeByRefType`, invoking through a custom
     `Binder`) is a `NotSupportedException` too.
   - The dynamic-code feature switches fold to `false` in IL, as under NativeAOT, so libraries take
     their reflection fallbacks instead of compiling expression trees.
5. **Pay for play.** A program that uses none of this carries no table, no stub and no code: the
   tables exist only once a member that reads them is lowered. A type contributes only the kinds it
   was marked for, and an invoke stub exists only for a kept member.

## Consequences

- The TechEmpower app, with DI, hosting, routing and Kestrel, compiles from the shared framework's IL
  with no stubbed methods, and answers TechEmpower's requests like the NativeAOT build.
- A program that reflects over members its code never names through DAM or a constant `typeof`
  pattern gets an explicit `NotSupportedException` where the JIT would answer. NativeAOT has the same
  boundary, with trimming warnings at compile time; csharpc doesn't issue those warnings yet.
- Fields: a type whose non-public fields csharpc can't see (a reference-assembly base type) or a field it
  can't describe (fixed buffers, ref fields) leaves that kind undescribed, so asking for it throws.
- Not covered yet (when this ADR was written; `GetInterfaces` and `Activator.CreateInstance(Type)` came later): `MakeGenericMethod`,
  `GetBaseDefinition` of overrides, inherited attribute lookup on overrides (answered when the overridden
  methods have no inheritable attributes). Each is an explicit `NotSupportedException` or compile-time
  error until it is added in the same scheme.
- Events (2026-11-06): `Type.GetEvent`/`GetEvents` and `EventInfo` work over `csrt_EventRecord`s (`PublicEvents` /
  `NonPublicEvents` in the dataflow, `typeof(X).GetEvent("Name")`). Like the table itself they cost nothing until the
  program calls one of them: events are described only once a `GetEvent`/`GetEvents` call is lowered
  (`NoteEventsQueried` replays what was marked before). The list follows RuntimeType (declared events, then the base
  types' non-private ones; `GetEvents` hides a base event by name, a case-sensitive `GetEvent` takes the most derived one);
  accessors are the methods' own records (so `AddEventHandler` goes through their invokers). Explicit failures: an event
  whose custom attributes csharpc can't list exactly (declared in a reference assembly, or a source event in a
  nullable-annotated type that has annotated events: csc synthesizes `[Nullable]`) throws `NotSupportedException` from
  `GetCustomAttributes*`/`IsDefined` only, as does `GetOtherMethods` of an IL event with `.other` accessors.
  Tests: `tests/samples/990_reflection_events`, `991_reflection_events_library`.
- Backing fields of events (2026-11-06): `Type.GetFields`/`GetField` also list the compiler-generated private backing
  field of every field-like event, as .NET does (named like the event, of the handler type, `Private`, `Static` for a static
  event, in declaration order among the fields, `[CompilerGenerated]` and `[DebuggerBrowsable(Never)]` on it); Roslyn hides
  it, so csharpc describes it from the event (`ModuleCodeGen.EventBackingFieldRecord`; for IL, the FieldDef named like the
  event, with its real flags and attributes). `GetValue`/`SetValue` read and replace the delegate in the event's slot (a
  class's layout slot, a static event's global). Events with accessors, abstract and interface events have no field. Explicit
  failures: the field's custom attributes where csc adds `[Nullable]` (the event rule of `NeedsNullableAttribute`,
  `CSRT_FIELD_ATTRIBUTES_UNKNOWN`), a struct's event field (`GetValue`/`SetValue` throw; structs lower no events yet) and a
  partial event. Tests: `tests/samples/992_reflection_event_backing_fields`, `994_reflection_event_backing_field_kinds`.
- `typeof(X)` through a local (2026-11-06): ILLink's dataflow follows `var t = typeof(C); t.GetFields()` and
  `Show(t)` for an annotated parameter, so csharpc does too (`FunctionCodeGen.TypeofLocals.cs`): the `typeof`s assigned to
  the local (flow-insensitive; the query itself still runs on the Type the program holds, so this only adds metadata) mark the
  type as the direct form does. In IL, a local stored `ldtoken T; GetTypeFromHandle` and loaded again. Not tracked: only
  direct `typeof` initializers and assignments count, not `cond ? typeof(A) : typeof(B)` (which ILLink merges); fields and
  `obj.GetType()` aren't followed by ILLink either. A local captured by a lambda is followed in source but not in IL (the
  closure field). Test: `tests/samples/993_reflection_typeof_local_flow`.
- Attributes csc synthesizes (2026-11-06, `ModuleCodeGen.SourceAttributes.cs`): Roslyn's symbols list the attributes the
  source writes (pseudo-custom ones like `[MethodImpl]` included) and omit those csc adds to metadata, so a list taken from
  them differs from the JIT's. Source members now list what csharpc reproduces exactly: `[CompilerGenerated]` (with
  `[DebuggerBrowsable]` on the field) on auto-property and field-like event accessors and backing fields, `[ParamArray]`,
  `[Extension]`, always before the source's own attributes; `[Optional]`/`[In]`/`[Out]` and the other pseudo-custom ones
  (`[DllImport]` + its `[PreserveSig]`, `[NonSerialized]`, `[FieldOffset]`, `[MarshalAs]`) once, after them in
  `GetCustomAttributes()` and before them in `GetCustomAttributesData()` (CoreCLR's two orders; the count word of an
  attribute list carries how many trailing records are pseudo). `[MethodImpl]`, `[SpecialName]` and `[StructLayout]`, which
  reflection never lists, are dropped. Where csc adds attributes csharpc doesn't (nullable annotations on any reference
  type or type parameter in a `#nullable` annotations context, async and iterator methods, readonly struct members,
  `in`/`scoped` parameters, `dynamic`/tuple-name/`nint` types, `decimal` constants and defaults, records, anonymous types,
  `required`/fixed members), the list is marked *unknown* (bit 30 of the count word): an untyped query
  (`GetCustomAttributes()`, `GetCustomAttributesData`, `IsDefined` of a System attribute) throws `NotSupportedException`,
  while a query for an attribute csc never synthesizes (the program's own, `[Obsolete]`) stays exact. Methods csc adds to a
  type that Roslyn has no symbol for (local functions and lambdas that use `this`) make the non-public-method list of that
  type unknown the same way instead of silently one method short. The nullable context is the project's setting
  (`Nullable`, per file with its `#nullable` directives), not Roslyn's: codegen binds a compilation with the context off.
  Tests: `tests/samples/1070_reflection_source_attributes`, `1071_reflection_nullable_attributes`.
- `[NullableContext]` and `[Nullable]` (2026-11-07, `ModuleCodeGen.NullableAttributes.cs`): reproduced exactly for source in
  an annotations context, by csc's own algorithm. Codegen binds with the nullable context off, so the annotations are read
  from the declarations' syntax, aligned with the symbols' structure. A type is a list of bytes in preorder (0 oblivious,
  1 not annotated, 2 annotated; every reference type, array and type parameter, containing types' arguments first; a generic
  struct 0 plus its arguments; other value types nothing, and `X?` for a struct X what X writes). A member's type votes for
  its byte when the bytes are all equal; a method votes once, with the most common value of its return type, parameters and
  type parameters; a type votes with its base type (`[0, arguments...]`), the interfaces it lists itself (all, but its base
  type's), its type parameters, and every member: fields, properties and events (accessors, auto-property backing fields),
  methods, nested types (lambdas and local functions don't). The winner is the most common value, ties to 0, then 1; the
  type or method carries `[NullableContext]` when it differs from what its container passes down (nothing at the top: 0),
  and a type with a base type `[Nullable]` for it unless its bytes are the inherited value; whatever else differs from the
  context gets `[Nullable]` (a byte, or the array when not all equal). The order is `[NullableContext]`, `[Nullable]`, then
  the other synthesized attributes, then the source's own. The attribute classes are the public ones of the .NET 11 reference
  assemblies (csc embeds nothing; the corelib shadows them). Validated against the JIT's reflection on ~80 hand-written shapes (sample 1222) and
  several hundred randomly generated types. Still the explicit unknown list (untyped queries throw): records, delegates,
  indexers, primary constructors, type parameters with constraint types or `new()`, generic overrides and explicit
  implementations, nested types of generic types, a type that mixes `#nullable` regions, names csharpc can't match to the
  symbol (aliases of constructed types, tuples over seven elements), pointers, `dynamic`; and the attributes of a
  *return value*, which `ReturnParameter` doesn't list for any method yet. `NullabilityInfoContext` can be built on this data.
- Added for expression trees (2026-10-30, [18](../18-compatibility.md#expression-trees)): standalone
  records of the members a program names directly — `ldtoken` + `GetMethodFromHandle`/`GetFieldFromHandle`
  and the `MethodInfo`/`FieldInfo` constants of lowered expression trees — with a per-method identity, so
  every record of one method is the same `MethodInfo` (`ModuleCodeGen.MemberRecords.cs`); a delegate
  type's `Invoke` for programs that use `System.Linq.Expressions` or `Delegate.DynamicInvoke`;
  `typeof(Impl<>).GetMethod("Name")` keeping only the named member on the instantiations.
- Assembly-level reflection (2026-11-06, `ModuleCodeGen.AssemblyTypes.cs`, `corelib/Reflection/Assembly.cs`): the program's own
  assemblies are known at compile time, so `Assembly.GetTypes()`/`DefinedTypes`/`GetExportedTypes()`/`ExportedTypes`,
  `Module.GetTypes()` and `Assembly.GetType(name)` of a synthesized or generic-definition name are answered from compiled-in
  type lists (arrays of method tables, read once per assembly), only for programs that reach them. An IL assembly's list is its
  TypeDef table in metadata order. The program's source assembly is what csc would write: the compilation csharpc started from
  is emitted once with Roslyn (as the program's own build is configured, Debug or Release) and its TypeDef table is read back,
  which gives csc's order and the types it synthesizes (`<>c`, closure classes, state machines, `<>O`,
  `<PrivateImplementationDetails>`). Declared types map to their source symbols (so `typeof(X)` is the listed Type); synthesized
  ones get name-only Type objects (kind, visibility, nesting, base type; no members, attributes or interface list). The same
  emitted image gives `GetReferencedAssemblies()` of the program (its AssemblyRef table). `Assembly.EntryPoint` is the
  entry method's record (`<Main>$` of top-level programs included); an async or Task-returning `Main` is bridged by a method csc
  synthesizes, and stays a `NotSupportedException`. `Module`: one manifest module per assembly (`Name`/`ScopeName` the file name
  .NET loads it from, `MetadataToken` 1, `Type.Module`, `MemberInfo.Module`, `GetModules`/`GetModule`); `ModuleVersionId` is
  known for IL assemblies only, `FullyQualifiedName` (a path) never. `GetName()` of an unsigned assembly has the empty public key
  .NET reports. Explicit failures: a type list of a shared-framework assembly, a program with anonymous types or generic closures
  or state machines (csc's generic synthesized types), several projects folded into one assembly, and a source program csc
  couldn't emit; `GetCustomAttribute<T>`/`GetCustomAttributes<T>` of the program's assembly for `Attribute` itself and the
  attributes csc synthesizes ([CompilationRelaxations], [RuntimeCompatibility], [Debuggable]); `Type.MetadataToken` and
  `MemberInfo.MetadataToken`. Tests: `tests/samples/1072_assembly_types`, `1073_assembly_identity`, `tests/projects/il/IlTypesApp`.
- Tests: `tests/samples/604_reflection_constructors`, `617_reflection_create_delegate`,
  `623_reflection_properties`, `667_reflection_fields`, plus the DI and hosting paths exercised by the TechEmpower workflow.
