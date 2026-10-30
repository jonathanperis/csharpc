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
- Not covered yet: events, `GetInterfaces`, `Activator.CreateInstance(Type)`, `MakeGenericMethod`,
  `GetBaseDefinition` of overrides, inherited attribute lookup on overrides (answered when the overridden
  methods have no inheritable attributes). Each is an explicit `NotSupportedException` or compile-time
  error until it is added in the same scheme.
- Added for expression trees (2026-10-30, [18](../18-compatibility.md#expression-trees)): standalone
  records of the members a program names directly — `ldtoken` + `GetMethodFromHandle`/`GetFieldFromHandle`
  and the `MethodInfo`/`FieldInfo` constants of lowered expression trees — with a per-method identity, so
  every record of one method is the same `MethodInfo` (`ModuleCodeGen.MemberRecords.cs`); a delegate
  type's `Invoke` for programs that use `System.Linq.Expressions` or `Delegate.DynamicInvoke`;
  `typeof(Impl<>).GetMethod("Name")` keeping only the named member on the instantiations.
- Tests: `tests/samples/604_reflection_constructors`, `617_reflection_create_delegate`,
  `623_reflection_properties`, `667_reflection_fields`, plus the DI and hosting paths exercised by the TechEmpower workflow.
