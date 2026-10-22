# 03 — Lowering C# to LLVM IR

This document describes how csharpc maps each C# construct onto LLVM IR, as implemented in
`src/Csharpc/CodeGen/`. All IR excerpts are real csharpc output (LLVM 18; `--emit llvm-ir`,
`-O0` unless noted).

- [Primitive types](#primitive-types)
- [Locals, parameters and mem2reg](#locals-parameters-and-mem2reg)
- [Integer semantics](#integer-semantics)
- [Floating-point semantics](#floating-point-semantics)
- [Conversions](#conversions)
- [Strings](#strings)
- [Arrays](#arrays)
- [Classes](#classes)
- [Virtual dispatch](#virtual-dispatch)
- [Properties](#properties)
- [Null checks and other runtime checks](#null-checks-and-other-runtime-checks)
- [Type tests and casts](#type-tests-and-casts)
- [Patterns](#patterns)
- [String concatenation and interpolation](#string-concatenation-and-interpolation)
- [Exceptions](#exceptions)
- [Static initialization](#static-initialization)
- [Entry point](#entry-point)
- [Local functions](#local-functions)
- [Symbol mangling](#symbol-mangling)
- [Library calls (intrinsics)](#library-calls-intrinsics)

## Primitive types

`TypeMapper` (`src/Csharpc/CodeGen/TypeMapper.cs`) maps each C# type to two LLVM types, the way clang
does. The **value type** is used for SSA values. The **storage type** is used in memory (allocas,
fields, array elements, statics).

| C# | value type | storage type | signedness |
|---|---|---|---|
| `bool` | `i1` | `i8` | — |
| `sbyte` / `byte` | `i8` | `i8` | signed / unsigned |
| `short` / `ushort` / `char` | `i16` | `i16` | signed / unsigned / unsigned |
| `int` / `uint` | `i32` | `i32` | signed / unsigned |
| `long` / `ulong` | `i64` | `i64` | signed / unsigned |
| `nint` / `nuint` | pointer-sized int | same | signed / unsigned |
| `float` / `double` | `float` / `double` | same | — |
| enums | underlying type | underlying type | underlying |
| classes, `string`, arrays, pointers | `ptr` | `ptr` | — |
| `void` | `void` | — | — |

Signedness isn't part of LLVM integer types. It's carried by the C# type and chosen per instruction:
`sdiv`/`udiv`, `ashr`/`lshr`, `sext`/`zext`, `slt`/`ult`, `sitofp`/`uitofp`. LLVM 15+ uses opaque
pointers, so every managed reference is plain `ptr`.

`LoadValue`/`ToStorage` (`FunctionCodeGen.cs`) convert between the two forms: `bool` loads as
`load i8` + `trunc` to `i1`, and stores as `zext` to `i8`.

`decimal`, nullable value types (`int?`), user-defined structs and generic type parameters are
rejected with `CSLLVM0001` for now.

## Locals, parameters and mem2reg

Following clang, csharpc never builds phi nodes itself:

- Each parameter gets a `<name>.addr` alloca in the entry block and is stored there on entry, so
  parameters are assignable like locals. `ref`/`out` parameters are already addresses and are used
  directly.
- Each local gets an alloca on first use (`LocalSlot`).
- Each Roslyn flow capture gets an alloca (`CaptureSlot`), or an address-holding alloca for lvalue
  captures ([02-architecture.md](02-architecture.md#flow-captures-and-lvalue-captures)).

The entry block holds only allocas and spills, then branches to `start`. At `-O1+`, `mem2reg`/`SROA`
turn all of it into SSA:

```llvm
define internal i32 @"_CN7Program7<Main>$3DivEii"(i32 %a, i32 %b) #0 {
entry:
  %a.addr = alloca i32, align 4
  store i32 %a, ptr %a.addr, align 4
  %b.addr = alloca i32, align 4
  store i32 %b, ptr %b.addr, align 4
  br label %start
start:
  br label %bb1
bb1:
  %0 = load i32, ptr %a.addr, align 4
  %1 = load i32, ptr %b.addr, align 4
  ...
```

## Integer semantics

C# integer arithmetic is **unchecked by default and wraps** (two's complement). LLVM's plain `add`,
`sub` and `mul` wrap too. csharpc **never emits `nsw`/`nuw`**, which would make overflow poison.
LLVM may still *infer* `nsw` where it can prove overflow is impossible (see the Fib example in the
README).

**Shifts.** C# masks the shift count to the operand width (`x << 33 == x << 1` for `int`). In LLVM,
shifting by ≥ the bit width is poison, so csharpc emits the mask explicitly
(`Arithmetic` in `FunctionCodeGen.Expressions.cs`):

```llvm
%count = and i32 %r, 31
%res   = shl i32 %l, %count     ; >> is ashr for signed, lshr for unsigned; >>> is always lshr
```

**Division and remainder.** `sdiv`/`srem` by zero, and `MinValue / -1`, are undefined behavior in
LLVM (and trap on x86). In .NET they throw `DivideByZeroException` and `OverflowException`. Integer
division is therefore guarded (`CheckedDivide`):

```llvm
bb1:
  %2 = icmp eq i32 %1, 0
  br i1 %2, label %throw_divide_by_zero, label %ok
ok:
  %3 = icmp eq i32 %0, -2147483648
  %4 = icmp eq i32 %1, -1
  %5 = and i1 %3, %4
  br i1 %5, label %throw_overflow, label %ok1
ok1:
  %6 = sdiv i32 %0, %1
  ret i32 %6
throw_divide_by_zero:
  call void @csrt_throw_divide_by_zero()
  unreachable
```

When the divisor is a constant, the checks fold away at `-O1+`.

**Compound assignment on small types.** `byte b += 10` computes in `int` and narrows back, as in C#
(`OperationTypeFor` promotes `byte`/`sbyte`/`short`/`ushort`/`char` to `int`).

**Not yet supported:** `checked` arithmetic and checked conversions (`CSLLVM0001`). The plan is
`llvm.sadd.with.overflow.*` and friends.

## Floating-point semantics

- `+ - * / %` map to `fadd fsub fmul fdiv frem`. C# `%` on doubles is `fmod`-like, which matches
  `frem`.
- Comparisons follow IEEE: every comparison involving NaN is false except `!=`. So `==`, `<`, `<=`,
  `>`, `>=` use **ordered** predicates (`oeq`, `olt`, …), and `!=` uses **unordered** `une`.
- No fast-math flags are ever set.

## Conversions

`ConvertNumeric` (`FunctionCodeGen.Expressions.cs`):

| from → to | instruction |
|---|---|
| int → wider int | `sext` if the source is signed, else `zext` (`char` is unsigned) |
| int → narrower int | `trunc` |
| int → float | `sitofp` / `uitofp` |
| float ↔ double | `fpext` / `fptrunc` |
| float → int | **`llvm.fptosi.sat.*` / `llvm.fptoui.sat.*`** |

The last row matters. Since .NET 9, float→int conversions are **saturating**, with NaN → 0
(`(int)1e20 == int.MaxValue`). LLVM's plain `fptosi` would return poison for out-of-range values;
the `.sat` intrinsics match .NET exactly.

Reference conversions: an implicit upcast is a no-op (same pointer). An explicit downcast calls
`csrt_castclass` (see [type tests](#type-tests-and-casts)). Boxing and unboxing are not supported
yet. User-defined conversion operators compile to direct calls.

## Strings

Layout, shared with `src/runtime/csrt.h`:

```
string: { ptr mt, i32 length, [length x i16] chars }     ; UTF-16, like .NET
```

**String literals are statically initialized `System.String` objects in `.rodata`.** There's no
allocation and no interning table at run time (`ModuleCodeGen.StringLiteral`). Identical literals are
deduplicated per module:

```llvm
@.str = private unnamed_addr constant { ptr, i32, [3 x i16] }
        { ptr @csrt_String_MT, i32 3, [3 x i16] [i16 46, i16 46, i16 46] }, align 8   ; "..."
```

`s.Length` is a load of field 1 after a null check. `s[i]` calls `csrt_string_char_at`, which
does the bounds check. `==`/`!=` on two `string` operands call `csrt_string_equals` (null-safe,
ordinal); other reference types compare pointers.

## Arrays

Layout:

```
T[]: { ptr mt, i64 length, [0 x T] data }        ; data at offset 16 on 64-bit targets
```

`new T[n]` and collection expressions (`int[] a = [1, 2, 3];`) call
`csrt_new_array(elementSize, length)` (zero-filled, `OverflowException` for negative lengths) and
then store the initializer elements. Element access does a null check and then **one unsigned
compare** of the sign-extended index against the length, which also catches negative indices
(`ElementAddress` in `FunctionCodeGen.Calls.cs`):

```llvm
bb1:
  %0 = load ptr, ptr %xs.addr, align 8
  %1 = load i32, ptr %i.addr, align 4
  %2 = sext i32 %1 to i64
  %3 = icmp eq ptr %0, null
  br i1 %3, label %throw_null_reference, label %ok
ok:
  %4 = getelementptr inbounds { ptr, i64, [0 x i32] }, ptr %0, i32 0, i32 1
  %len = load i64, ptr %4, align 8
  %5 = icmp uge i64 %2, %len
  br i1 %5, label %throw_index_out_of_range, label %ok1
ok1:
  %6 = getelementptr inbounds { ptr, i64, [0 x i32] }, ptr %0, i32 0, i32 2, i64 %2
  %7 = load i32, ptr %6, align 4
  ret i32 %7
```

`a.Length` truncates the `i64` length to `i32`; `a.LongLength` returns it as is. Jagged arrays
(`long[][]`) work naturally, since the elements are `ptr`. Multi-dimensional arrays and `Index`/`Range`
indexing are not supported yet.

`int`/`uint` indices are actually compared in 32 bits against the truncated length (lossless, the
length is below 2^31) and zero-extended afterwards, so the check has the same width as the usual
`i < a.Length` loop condition and LLVM can fold one into the other.

**Memory facts** (`MemoryFacts.cs`, `FunctionCodeGen.ArrayAccess.cs`). Length loads carry
`!range [0, Array.MaxLength]` (strings: `[0, 0x3FFFFFDF]`) and `!noundef`, so arithmetic checked
against a length provably doesn't overflow. Array-length, string-length and "managed data" (fields,
array elements, span elements) accesses get distinct TBAA tags under a csharpc root: stores to
elements or fields never clobber a length, so LLVM keeps the length in a register across them and
removes the bounds checks that become loop-invariant or dominated. Lengths are immutable after
allocation (RyuJIT assumes the same); the C runtime's accesses use clang's TBAA root and untagged
accesses (pointers, refs) alias everything, so neither is affected.

## Classes

`ModuleCodeGen.GetLayout` builds a `ClassLayout` for each class.

**Instance layout:** a named LLVM struct whose first field is the method-table pointer, followed by
the inherited fields (at the same indices as in the base class) and then the class's own instance
fields, including auto-property backing fields:

```llvm
%class.Rectangle = type { ptr, double, double }     ; mt, <Width>k__BackingField, <Height>k__BackingField
; a subclass `Square : Rectangle` with no own fields has the same element list
```

**Method table** (one internal constant global per class, `_CN<type>2MTE`):

```
{ ptr base_mt, ptr name, i32 instance_size, i32 flags, [N x ptr] vtable }
```

The first 24 bytes are `csrt_MethodTable` from `src/runtime/csrt.h`: `base` (the base class's method
table, or `csrt_Object_MT`), `name` (CLR-style full name, e.g. `Ns.Outer+Inner`, as a C string),
`instance_size` (from the target data layout), and `flags` (bit 0 = abstract). The vtable follows
immediately. Real output for `class Animal { public virtual string Sound() … }` and
`class Dog : Animal { public override string Sound() … }`:

```llvm
@_CN6Animal2MTE = internal constant { ptr, ptr, i32, i32, [4 x ptr] } { ptr @csrt_Object_MT, ptr @.str.c.1, i32 8, i32 0,
    [4 x ptr] [ptr @csrt_object_to_string_default, ptr @csrt_object_equals_default,
               ptr @csrt_object_get_hash_code_default, ptr @_CN6Animal5SoundEv] }
@_CN3Dog2MTE = internal constant { ptr, ptr, i32, i32, [4 x ptr] } { ptr @_CN6Animal2MTE, ptr @.str.c.2, i32 8, i32 0,
    [4 x ptr] [ptr @csrt_object_to_string_default, ptr @csrt_object_equals_default,
               ptr @csrt_object_get_hash_code_default, ptr @_CN3Dog5SoundEv] }
```

**VTable slot assignment:**

- Slots 0, 1 and 2 are always `System.Object`'s `ToString()`, `Equals(object)` and `GetHashCode()`.
  By default they point at csrt implementations; the default `ToString()` returns the type name,
  like .NET.
- A derived class copies its base's vtable. An `override` replaces the slot of its **root**
  method, found by walking `OverriddenMethod` back to the introducing `virtual`
  (`ClassLayout.RootOf`). A new `virtual`/`abstract` method appends a slot. Abstract slots are null.
- Overriding library members other than the three `object` virtuals is rejected.

**Object creation:** `new C(args)` calls `csrt_alloc(@C_MT, instance_size)` (zeroed, header set)
and then calls the constructor directly with `this`. Constructors return `void`.

**Constructors.** C# runs field initializers *before* the base-constructor call, except when the
constructor chains to `this(...)`. csharpc lowers each initializer as its own CFG prologue in front
of the constructor body, which already contains the `: base(...)` call. The compiler-generated
default constructor runs the initializers and then calls the base class's parameterless constructor
(`EmitImplicitConstructor`). The call to `object..ctor()` is a no-op.

Not supported yet: structs, records, and deriving from library classes. Generic classes and
interfaces are covered below.

## Generics (monomorphization)

Generic code is compiled per instantiation, like Rust and C++ templates (and like NativeAOT for
value-type instantiations): `Stack<int>`, `Stack<string>` and `Max<double>` each get their own
LLVM functions, instance layout and method table. There is no shared generic code and no runtime
generic dictionary. See `src/Csharpc/CodeGen/Generics.cs`.

* **Substitution.** Roslyn hands us the CFG of the generic *definition*, whose types mention type
  parameters. `TypeSubstitution.For(method)` builds the map T → argument from a constructed method
  (its own type arguments plus those of every containing type). `FunctionCodeGen` runs every type
  and symbol it reads through `S(...)` / `TypeOf(op)`: operation types, local and parameter types,
  and target methods, fields and properties. Members of constructed types are re-resolved through
  the constructed type's `GetMembers()`, so `Box<T>.Value` inside the body becomes `Box<int>.Value`.
* **Keys.** Functions, layouts, method tables and static fields in generic contexts are keyed by
  the *constructed* symbol. Non-generic code keeps keying by `OriginalDefinition`.
* **Worklist.** `GetFunction` on a constructed method declares it and queues the body
  (`_pendingInstantiations`). Layouts and method tables for constructed classes are created on
  first use, and building a method table pulls in the class's virtual methods. Generation therefore
  starts at `Main` and only instantiates what is reachable.
* **Lambdas and local functions** in generic code are emitted once per enclosing substitution.
  They are keyed by (symbol, substitution) in `GetNestedFunction`, and closure environment layouts
  are built per substitution (`EnvScope.TypeFor`). A generic local function adds its own type
  arguments and is instantiated on first use.
* **Mangling.** Type arguments are appended Itanium-style: `I<types>E` after a generic type or
  method name, e.g. ``_CN7Stack`1IiE4PushEi`` for `Stack<int>.Push(int)`. The runtime type name
  follows the CLR format: ``Box`1[System.Int32]`` (that's what `object.ToString()` prints in .NET).
* **Constrained calls.** After substitution `T` is concrete:
  * `x.CompareTo(y)` with `T : IComparable<T>` is devirtualized: a direct call for user types, and
    a few instructions for primitives (`int.CompareTo` and friends are intrinsics).
  * `x.ToString()` / `Equals` / `GetHashCode` on a value-type `T` format the primitive directly or
    box it.
  * Generic math (`T : INumber<T>`): operators and `T.Zero`/`T.One` bind to static abstract
    interface members. Once `T` is a primitive, csharpc emits the built-in instruction.
* `new T()`, `default(T)`, `is T`/`as T`, `T[]`, nested generic types (`Outer<T>.Inner`) and
  generic base classes all work.
* Not yet: static field initializers and static constructors in generic types, variance, and
  generic virtual methods (they would need per-instantiation vtable slots).

## Interfaces

Each interface has an identity "method table": a constant global flagged `CSRT_MT_INTERFACE`.
Every constructed generic interface (`IComparable<Version>`) gets its own. Every class method table
carries an **interface map**, and the method-table header grew to hold it (shared by
`TypeMapper.MethodTableHeader`, `ModuleCodeGen.EmitMethodTable` and `csrt_MethodTable` in
`src/runtime/csrt.h`):

```
{ ptr base, ptr name, i32 size, i32 flags, ptr interfaces, i32 interface_count, i32 reserved, [N x ptr] vtable }
interfaces: [count x { ptr interface_MT, ptr slot_table }]   ; flattened over bases and interface inheritance
slot_table: [members x ptr]                                   ; implementations, in interface member order
```

The vtable now starts at offset 40.

* **Dispatch** (`Interfaces.cs`): `slots = csrt_interface_slots(obj, @IFoo.IMT)` does a linear scan
  of a short map. The call then loads `slots[i]` and calls it indirectly. We chose this over
  per-interface fixed itable offsets (which need a global coloring pass) and over hashed dispatch
  cells: it's simple, it handles any number of interfaces, and most calls never reach it (see
  devirtualization). A fast path that LLVM can see, caching the last (MT → slots) pair, is a
  possible later improvement.
* **Implementations** come from Roslyn's `FindImplementationForInterfaceMember`. When the
  implementation is virtual, the slot takes the class's *vtable* entry, so overrides in derived
  classes are honoured. Explicit implementations, default interface methods (compiled with an
  interface-typed `this`) and interface inheritance all work.
* **Devirtualization.** If the receiver's static type (after substitution, and after peeling the
  implicit conversion to the interface) is a class or primitive, csharpc resolves the
  implementation at compile time. That covers every constrained generic call.
* **Type tests**: `csrt_isinst_class` checks the interface map when the target method table is
  flagged as an interface, so `is IFoo`, `as IFoo` (`IConversionOperation.IsTryCast`) and casts work.
* **foreach over user collections**: the pattern-based `GetEnumerator` (direct user calls), and
  `IEnumerable<T>`/`IEnumerator<T>`/`IDisposable` implemented by user types (interface dispatch).
  Arrays and strings keep their stack-enumerator fast path.
* Not yet: boxed primitives and structs implementing interfaces (boxed method tables have no
  interface map), arrays used through `IEnumerable<T>`, and library classes implementing
  interfaces.

## Virtual dispatch

A virtual call loads the method table, indexes the vtable at
`sizeof(header) + slot * sizeof(ptr)`, and calls indirectly (`VirtualCall` in
`FunctionCodeGen.Calls.cs`). The header is 40 bytes (see [Interfaces](#interfaces)); the listing below predates the interface-map fields, when it was 24 bytes and `a.Sound()` in slot 3 sat at offset 24 + 3·8 = 48:

```llvm
bb1:
  %0 = load ptr, ptr %a.addr, align 8
  %1 = icmp eq ptr %0, null
  br i1 %1, label %throw_null_reference, label %ok
ok:
  %mt   = load ptr, ptr %0, align 8
  %slot = getelementptr inbounds i8, ptr %mt, i64 48
  %vfn  = load ptr, ptr %slot, align 8
  %2    = call ptr %vfn(ptr %0)
  ret ptr %2
```

Calls are direct (no dispatch) when the method isn't virtual, when it or its containing type is
`sealed`, or for `base.M()` calls. Since all method tables are internal constants, LLVM can also
devirtualize when it can prove the dynamic type.

## Properties

Accessors are ordinary functions (`get_X`/`set_X` mangle under the accessor name). Property reads
and writes call the accessor, with virtual dispatch for virtual properties, and LLVM inlines trivial
ones. Auto-property accessors are synthesized as a backing-field load or store
(`EmitAutoPropertyAccessor`). Assigning a **get-only** auto-property in a constructor writes the
backing field directly. Compound assignment on properties (`counter.Value += 10`) evaluates the
receiver once and then calls get followed by set.

## Null checks and other runtime checks

.NET throws `NullReferenceException` when a member is accessed on `null`. CoreCLR does this by
catching the hardware fault. **csharpc can't**: in LLVM, a load from a null pointer is undefined
behavior, and the optimizer is allowed to delete or reorder it. So csharpc emits **explicit null
checks** before instance field access, instance calls (C# `callvirt` semantics), array access and
`string.Length`. The check is skipped when the receiver is `this`.

All checks branch to a **per-function shared throw block** (`ThrowBlock`/`FailIf`) that calls a
`noreturn cold` csrt helper. This keeps the hot path tight and lets LLVM lay out failure paths out of
line.

| check | helper | .NET exception |
|---|---|---|
| null receiver | `csrt_throw_null_reference` | `NullReferenceException` |
| array index ≥ length (unsigned) | `csrt_throw_index_out_of_range` | `IndexOutOfRangeException` |
| integer divisor == 0 | `csrt_throw_divide_by_zero` | `DivideByZeroException` |
| `MinValue / -1`, `Math.Abs(MinValue)` | `csrt_throw_overflow` | `OverflowException` |
| failed downcast | `csrt_throw_invalid_cast` | `InvalidCastException` |

## Type tests and casts

- `x is T` / `x is T t` / `(T)x` where the conversion is a statically known implicit reference
  conversion compile to `x != null`, or to nothing for casts.
- Otherwise they call `csrt_isinst_class(obj, @T_MT)`, which walks the method-table `base` chain, or
  `csrt_castclass(obj, @T_MT)`, which throws `InvalidCastException`. User classes and `string` are
  supported as targets.

## Patterns

Roslyn's CFG turns `switch` statements and expressions into chains of tests. Each test is an
`IIsPatternOperation` that csharpc lowers to an `i1` (`PatternTest`):

| pattern | lowering |
|---|---|
| constant (`0`, `"abc"`, `null`) | `icmp eq` / `fcmp oeq` / `csrt_string_equals` |
| relational (`< 0`, `>= 1`) | the corresponding comparison |
| `and` / `or` / `not` | `and` / `or` / `xor true` on `i1` |
| discard (`_`) | `true` |
| type (`Circle`) | [type test](#type-tests-and-casts) |
| declaration (`Square sq`, `var v`) | type test, then store into the local |

Property and positional patterns aren't supported yet.

## String concatenation and interpolation

`a + b` with a string operand, `string.Concat(...)` and `$"..."` all become a left-to-right chain of
`csrt_string_concat` calls. Each non-string operand is first converted with `ToStringValue`:

- Boxing conversions to `object` are stripped (`Unbox`), so the value is formatted by its real type
  with no boxing at all.
- Primitives call `csrt_string_from_{i32,i64,u32,u64,f64,f32,bool,char}`.
- References call `csrt_object_to_string`, which returns `""` for null (as `string.Concat` does)
  and otherwise makes a virtual call to vtable slot 0.
- **Constants are formatted at compile time using .NET's own formatting**: `"pi ~ " + 3.14159`
  embeds the literal string `"3.14159"` produced by `double.ToString(null, InvariantCulture)`
  inside the compiler. Runtime and compile-time formatting therefore agree by construction.

Interpolation holes with alignment or format specifiers (`{x,5}`, `{x:F2}`) and enum formatting are
not supported yet.

## Exceptions

In this spike, `throw new SomeLibraryException("message")` lowers to
`csrt_throw(@<type>_MT, message)` followed by `unreachable`. csrt prints
`Unhandled exception. System.InvalidOperationException: message` to stderr and calls `abort()`,
which gives exit status 134, the same as an unhandled exception in a .NET process on Linux. Without
a message argument, csharpc passes `.NET`'s default text
(`Exception of type 'X' was thrown.`). Throwing user-defined exception objects, `try`/`catch`/`finally`
and rethrow are rejected. The design for real unwinding is [ADR 0005](adr/0005-exceptions.md).

## Static initialization

Static fields are internal, zero-initialized globals (`ModuleCodeGen.StaticField`).
`__csharpc_static_init` runs **every** static field initializer and **every** explicit static
constructor, in declaration order, **eagerly before `Main`**.

This deviates from .NET, which runs a type's initializer lazily, on first access (precisely, or
`beforefieldinit`-relaxed when there's no static constructor). Programs whose output depends on
static-constructor *ordering or side effects* can behave differently. Precise lazy initialization
(guard flag plus an init check at access sites, which LLVM can hoist) is on the roadmap.

`const` fields never exist at run time: Roslyn folds them into `ConstantValue`, and csharpc emits
the constant directly.

## Entry point

csharpc emits the C `main` (`EmitNativeMain`):

```llvm
define i32 @main(i32 %0, ptr %1) {
entry:
  call void @csrt_init(i32 %0, ptr %1)          ; record argv, set up a 64 KiB stdout buffer
  call void @__csharpc_static_init()
  %2 = call ptr @csrt_get_args()                ; string[] args (without argv[0]), only if Main takes it
  call void @"_CN7Program7<Main>$EAu"(ptr %2)
  call void @csrt_shutdown()                    ; flush stdout
  ret i32 0                                     ; or Main's int return value
}
```

Top-level statements compile to Roslyn's synthesized `Program.<Main>$(string[] args)`. Async
`Main` isn't supported.

## Local functions

Local functions are discovered through `ControlFlowGraph.LocalFunctions` and lowered from
`GetLocalFunctionControlFlowGraph` as ordinary internal functions, mangled under their containing
method. Static and non-capturing local functions work, including recursion. A local function or
lambda that captures outer locals or parameters is detected (the local isn't declared in the graph
being lowered) and rejected as a closure.

## Symbol mangling

`src/Csharpc/CodeGen/Mangler.cs`, loosely modeled on the Itanium C++ ABI and Rust v0:

```
_CN <len><segment>... E <param-type-codes>        ; 'v' when there are no parameters
```

Segments are namespaces, then containing types, then (for local functions) containing methods, then
the member name. Constructors are `ctor` and static constructors `cctor`. Since everything is
length-prefixed, names like `<Main>$` need no escaping.

| C# | symbol |
|---|---|
| `Program.Fib(int)` (local function in top-level code) | `_CN7Program7<Main>$3FibEi` |
| `Animal.Sound()` | `_CN6Animal5SoundEv` |
| `Geo.Circle..ctor(double)` | `_CN3Geo6Circle4ctorEd` |
| `Pick(Animal)` | `…4PickEN6AnimalE` |
| method table of `Animal` | `_CN6Animal2MTE` |
| static field `Shape.Count` | `_CN5Shape5CountE` |

Parameter type codes: `v` void, `b` bool, `w` char, `a` sbyte, `h` byte, `s` short, `t` ushort,
`i` int, `j` uint, `l` long, `m` ulong, `f` float, `d` double, `u` string, `o` object,
`A<elem>` array, `N<path>E` other named types. A `R` prefix marks `ref`/`out`/`in` parameters.

The scheme has no generic arguments yet. Those will be needed for monomorphized instantiations.

## Library calls (intrinsics)

Roslyn binds user code against the real .NET reference assemblies, so overload resolution is exact.
csharpc then looks up the resolved member's signature in a table (`src/Csharpc/CodeGen/Intrinsics.cs`,
keyed like `System.Console.WriteLine(int)`) and emits an implementation. Members not in the table
fail with `CSLLVM0002`. `csharpc --intrinsics` prints the current list.

| area | members | lowering |
|---|---|---|
| Console | `Write`/`WriteLine` for `string object int long uint ulong bool double float char`, `WriteLine()` | typed `csrt_console_write_*`. Boxed arguments are unboxed statically; other objects go through `ToString` |
| Object | `ToString()`, `object()` ctor | vtable slot 0; no-op |
| ToString | `int long uint ulong short ushort byte sbyte double float bool char string .ToString()` | `csrt_string_from_*` |
| String | `Concat` (2–4 strings, 2–3 objects), `IsNullOrEmpty`, `Equals` (static and instance), `Length`, `this[int]` | csrt calls / inline load |
| Math | `Sqrt Floor Ceiling Truncate Round` (double) | `llvm.sqrt/floor/ceil/trunc/roundeven.f64` (`Round` is banker's rounding, like .NET) |
| Math | `Abs` (double, float, int, long) | `llvm.fabs`; integers check `MinValue` → `OverflowException` |
| Math | `Max`/`Min` (int, long, uint, ulong) | `select` + `icmp` |
| Math | `Max`/`Min` (double) | `llvm.maximum/minimum.f64`: IEEE 754-2019, NaN-propagating, −0 < +0, as in .NET Core 3.0+ |
| Math | `Pow Atan2 Exp Log Log10 Sin Cos Tan Asin Acos Atan Sinh Cosh Tanh Cbrt`, `Clamp(int,int,int)` | libm / `select` |
| Array | `Length`, `LongLength` | load of the length field |
| Environment | `TickCount64`, `NewLine`, `Exit(int)` | csrt / literal `"\n"` |

This table is scaffolding. As the C# corelib grows ([04-runtime.md](04-runtime.md)), entries move
from "lowered by the compiler" to "ordinary C# compiled by csharpc".
