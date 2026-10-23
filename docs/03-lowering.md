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
- [Structs](#structs)
- [Boxing](#boxing)
- [Generics (monomorphization)](#generics-monomorphization)
- [Interfaces](#interfaces)
- [Virtual dispatch](#virtual-dispatch)
- [Properties](#properties)
- [Null checks and other runtime checks](#null-checks-and-other-runtime-checks)
- [Type tests and casts](#type-tests-and-casts)
- [Patterns](#patterns)
- [Closures and delegates](#closures-and-delegates)
- [Ranges](#ranges)
- [Spans](#spans)
- [Unsafe code](#unsafe-code)
- [String concatenation and interpolation](#string-concatenation-and-interpolation)
- [Exceptions](#exceptions)
- [Static initialization](#static-initialization)
- [Entry point](#entry-point)
- [Local functions](#local-functions)
- [Iterators and async](#iterators-and-async)
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
| classes, `string`, arrays, delegates, interfaces, pointers | `ptr` | `ptr` | — |
| user structs, record structs | named struct `%struct.S` | same | — |
| `T?` (`Nullable<T>`) | `{ i8 hasValue, T }` | same | — |
| `ValueTuple` (up to 7 elements) | `{ T1, ..., Tn }` (flattened) | same | — |
| `decimal` | `{ i32 flags, i32 hi32, i64 lo64 }` (the CLR layout) | same | — |
| `TimeSpan`, `DateTime` | `i64` (ticks / `_dateData`) | same | — |
| `Int128` / `UInt128` | `i128` | same | signed / unsigned |
| `Index` / `Range` | `i32` / `{ i32, i32 }` | same | — |
| `Span<T>` / `ReadOnlySpan<T>` | `{ ptr, i32 length }` | same | — |
| `void` | `void` | — | — |

Signedness isn't part of LLVM integer types. It's carried by the C# type and chosen per instruction:
`sdiv`/`udiv`, `ashr`/`lshr`, `sext`/`zext`, `slt`/`ult`, `sitofp`/`uitofp`. LLVM 15+ uses opaque
pointers, so every managed reference is plain `ptr`.

`LoadValue`/`ToStorage` (`FunctionCodeGen.cs`) convert between the two forms: `bool` loads as
`load i8` + `trunc` to `i1`, and stores as `zext` to `i8`.

Value types are first-class LLVM aggregates (see [Structs](#structs)). Generic type parameters never
reach `TypeMapper`: generic code is compiled per instantiation, after substitution
([Generics](#generics-monomorphization)). Library structs csharpc doesn't model (`DateTimeOffset`,
...) are `CSLLVM0001`, reported at the field, local or expression that uses them.

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

**`checked` arithmetic** uses `llvm.{s,u}{add,sub,mul}.with.overflow.*` and branches to
`csrt_throw_overflow` when the overflow bit is set. Checked conversions compare against the target
range before narrowing.

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
`csrt_castclass` (see [type tests](#type-tests-and-casts)). Value type ↔ `object`/interface
conversions box and unbox (see [Boxing](#boxing)). User-defined conversion operators compile to
direct calls, and are lifted to nullable operands as the C# spec describes.

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
does the bounds check. Most other `string` members are C# in the corelib
([12-corelib.md](12-corelib.md)). `==`/`!=` on two `string` operands call `csrt_string_equals` (null-safe,
ordinal); other reference types compare pointers.

## Arrays

Layout:

```
T[]: { ptr mt, i64 length, [0 x T] data }        ; data at offset 16 on 64-bit targets
```

Every array gets a method table per element type (`ArrayMethodTable`), named like the CLR
(`System.Int32[]`), so `GetType()`, `ToString()` and type tests see the element type. Its interface
map lists exactly the interfaces a CLR array of that element type implements (`IList<T>`,
`IReadOnlyList<T>`, non-generic `IEnumerable`/`IList`, ...), with slots implemented in the corelib
(`ArrayInterfaces.cs`).

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
(`long[][]`) work naturally, since the elements are `ptr`. Rectangular arrays (`T[,]`) are
`{ ptr mt, i64 total, [rank x i64] lengths, [0 x T] data }`, row-major, so `Length` stays at the
same offset (`FunctionCodeGen.MultiDim.cs`). `a[^1]` and `a[1..^1]` are covered in
[Ranges](#ranges).

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
accesses (pointers, refs) alias everything, so neither is affected. Within managed data, each class
field has its own tag, and array elements and string chars have one each: these never share memory
(instance layouts are sequential). Struct fields and span elements keep the generic tag, since a
struct can live in an array or a field and a span can view either. `string[int]` is lowered inline
for the same reason: the C helper's clang-tagged length load aliased every store.

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
{ ptr base_mt, ptr name, i32 instance_size, i32 flags, ptr interfaces, i32 interface_count, i32 reserved, [N x ptr] vtable }
```

The 40-byte header is `csrt_MethodTable` from `src/runtime/csrt.h` (mirrored by
`TypeMapper.MethodTableHeader`): `base` (the base class's method table, or `csrt_Object_MT`), `name`
(CLR-style full name, e.g. `Ns.Outer+Inner` or ``Box`1[System.Int32]``, as a C string),
`instance_size` (from the target data layout), `flags` (bit 0 = abstract), and the
[interface map](#interfaces). The vtable follows immediately. Output for
`class Animal { public virtual string Sound() … }` and `class Dog : Animal { public override string
Sound() … }`, from before the interface-map fields existed (the vtable then started at offset 24):

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
- Overriding library members other than the three `object` virtuals is rejected, except in
  classes deriving from corelib shadows (`Comparer<T>`, `EqualityComparer<T>`, ...) and from
  `System.Exception`, whose layout and method tables are shared with `src/runtime/eh.c`.

**Object creation:** `new C(args)` calls `csrt_alloc(@C_MT, instance_size)` (zeroed, header set)
and then calls the constructor directly with `this`. Constructors return `void`.

**Constructors.** C# runs field initializers *before* the base-constructor call, except when the
constructor chains to `this(...)`. csharpc lowers each initializer as its own CFG prologue in front
of the constructor body, which already contains the `: base(...)` call. The compiler-generated
default constructor runs the initializers and then calls the base class's parameterless constructor
(`EmitImplicitConstructor`). The call to `object..ctor()` is a no-op.

**Building a layout** registers it before its fields are mapped, because members may refer back
to the type. If mapping fails (a field of an unsupported library type), `BuildOrForget` drops the
half-built layout and caches the failure instead: a derived class or a boxing site must never see an
opaque LLVM struct without a method table. Every later use reports the same diagnostic.

**Records** are classes (or structs) whose synthesized members (`ToString`/`PrintMembers`, equality,
`EqualityContract`, the copy constructor and `<Clone>$` for `with`, `Deconstruct`) get bodies from
`FunctionCodeGen.Records.cs`, following the C# spec. **Anonymous types** are classes with a
synthesized `ToString`/`Equals`/`GetHashCode`. `GetHashCode` is Roslyn's algorithm (a seed from the
FNV-1a hashes of the field names, then `hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(field)`),
so hash codes match .NET.

Deriving from library classes other than exceptions and corelib shadows is `CSLLVM0001`.

## Structs

User structs, record structs, `Nullable<T>`, `ValueTuple`, spans and the corelib's structs
(`KeyValuePair<K,V>`, `List<T>.Enumerator`, ...) are LLVM first-class aggregates
(`FunctionCodeGen.Structs.cs`). A struct is a named LLVM struct of the fields' storage types, with no
header (`GetStructLayout`, laid out per instantiation for generic structs). Loads, stores,
arguments and returns copy the aggregate, which gives C#'s value semantics for free; SROA
scalarizes it again at `-O1+`.

- **Member access needs an address.** Lvalues (locals, parameters, fields, array elements, `this`)
  use their storage directly, so `s.X = 1` mutates in place. Rvalues are spilled to a temporary.
- **Instance methods and constructors** take `this` as a pointer to the storage. Readonly fields,
  `in` parameters and `ref readonly` locals get C#'s defensive copy before a non-readonly member is
  called.
- **`new S(...)`** zero-fills a temporary and calls the constructor on it. `new S()` without an
  explicit parameterless constructor is `default(S)`, a `zeroinitializer`.
- **`ValueType.Equals`/`GetHashCode`** are generated field by field with .NET's semantics
  (`ModuleCodeGen.StructEquality.cs`), unless the struct overrides them.
- `Nullable<T>` is `{ i8 hasValue, T value }`. Lifted operators, `??`, `GetValueOrDefault` and
  boxing (`null` for no value) follow the spec.

## Boxing

A boxed value is an ordinary object: `{ ptr mt, payload }` with the payload at offset 8 (`Box` in
`FunctionCodeGen.Calls.cs`). Primitives use method tables defined in csrt (`csrt_Boxed_Int32_MT`,
...). Structs get a method table from `StructMethodTable`, built on first use. Its object vtable
slots point at unboxing thunks into the struct's overrides (or at the generated field-by-field
`Equals`/`GetHashCode`), and its interface map points at unboxing thunks into the struct's
interface implementations. A boxed `List<int>.Enumerator` is therefore an `IEnumerator<int>`, as
in .NET.

Unboxing calls `csrt_unbox(obj, mt)`, which throws `NullReferenceException` or
`InvalidCastException` like the CLR and returns the payload address. Calls through `object` that
csharpc can resolve statically (formatting a boxed `int`) skip the box altogether.

`typeof(T)` and `GetType()` return one canonical `Type` object per method table
(`src/runtime/type.c`), so `typeof(G<int>) == boxed.GetType()` is a pointer comparison. Types that
are never instantiated (library types, interfaces) get a name-only method table.

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
  A type nested in a generic type lists the whole chain's type arguments on the innermost name:
  ``List`1+Enumerator[System.Int32]``.
* **Constrained calls.** After substitution `T` is concrete:
  * `x.CompareTo(y)` with `T : IComparable<T>` is devirtualized: a direct call for user types, and
    a few instructions for primitives (`int.CompareTo` and friends are intrinsics).
  * `x.ToString()` / `Equals` / `GetHashCode` on a value-type `T` format the primitive directly or
    box it.
  * Generic math (`T : INumber<T>`): operators and `T.Zero`/`T.One` bind to static abstract
    interface members. Once `T` is a primitive, csharpc emits the built-in instruction.
* `new T()`, `default(T)`, `is T`/`as T`, `T[]`, nested generic types (`Outer<T>.Inner`) and
  generic base classes all work.
* **Generic virtual methods** have no vtable slots. A call site switches over the closed world of
  overriding classes and calls the matching instantiation directly (`GenericVirtualCall`).
* **Variance** (`IEnumerable<string>` as `IEnumerable<object>`): every variant interface the program
  uses is registered at startup with its type arguments' method tables, and the runtime's interface
  lookup falls back to a variance-compatible entry.
* **Recursive expansion.** `C<T>` calling into `C<C<T>>` is legal C#. .NET instantiates lazily, as
  deep as execution goes, but ahead-of-time expansion would never end. Instantiations nested more
  than 16 levels deep are `CSLLVM0001` (`CheckGenericDepth`).
* Not yet: static field initializers and static constructors in generic types.

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
* **Builtin types.** Boxed structs carry interface maps ([Boxing](#boxing)). Arrays carry exact
  per-element-type maps ([Arrays](#arrays)). `string` and boxed primitives (`IComparable<int>`,
  `IEquatable<string>`, ...) are registered with the runtime at startup for the interfaces the
  program uses (`csrt_register_builtin_interface`). Corelib classes (`List<T>`, `Dictionary<K,V>`,
  ...) are compiled like user classes and implement their interfaces the same way.

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
| `MinValue / -1`, `Math.Abs(MinValue)`, `checked` overflow | `csrt_throw_overflow` | `OverflowException` |
| failed downcast | `csrt_castclass` | `InvalidCastException` |

## Type tests and casts

- `x is T` / `x is T t` / `(T)x` where the conversion is a statically known implicit reference
  conversion compile to `x != null`, or to nothing for casts.
- Otherwise they call `csrt_isinst_class(obj, @T_MT)`, or `csrt_castclass(obj, @T_MT)`, which throws
  `InvalidCastException` with .NET's message. For a class target the runtime walks the `base`
  chain. For an interface target it searches the interface map and the builtin registrations,
  then variance-compatible entries. Value-type targets (`o is int i`, `o is S s`) test against the
  boxed method table, then unbox.

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
| property / positional (`{ Length: > 3 }`, `(0, var y)`) | sub-patterns in order, stopping at the first failure; positional patterns use `Deconstruct` or tuple elements |
| list (`[1, .., var last]`) | length test, then element sub-patterns (arrays, strings, spans) |

Constant patterns use `Equals` semantics, so `x is double.NaN` matches NaN even though
`x == double.NaN` is false (`FunctionCodeGen.Patterns.cs`).

## Closures and delegates

`FunctionCodeGen.Closures.cs`, `ClosureAnalysis.cs`:

- **Captured variables** live in heap environments, `{ ptr header, ptr parent, vars... }`, one per
  CFG region that declares captured variables. A fresh environment is allocated every time control
  enters the region, which gives C#'s per-iteration semantics for loop variables for free.
- **Lambdas and local functions** are ordinary internal functions that take the environment of
  their creation scope as a hidden first parameter. Non-capturing ones take none.
- **Delegates** are `{ ptr mt, ptr target, ptr fn, ptr invocationList }`. Invoking one calls
  `fn(target, args...)`. The target is the environment for lambdas and the receiver for instance
  method groups. Static methods are adapted with a thunk that ignores it.

### Delegates

Multicast delegates and events (`FunctionCodeGen.Events.cs`): `Delegate.Combine`/`Remove`
(`src/runtime/delegates.c`) build a new delegate whose invocation list is a flat array of
single-cast delegates. Invoking it calls each in order and returns the last result. Field-like
events are their backing delegate field, and `+=`/`-=` combine and remove in place. Events with
custom accessors call the user's `add`/`remove` methods.

## Ranges

`System.Index` uses .NET's own encoding: one `i32`, `n` from the start or `~n` from the end. `Range`
is `{ Index start, Index end }` (`FunctionCodeGen.Ranges.cs`). `a[^1]`, `s[1..^1]` and `span[r]`
resolve against the length, with .NET's `ArgumentOutOfRangeException` checks. Array slices are
copies (`RuntimeHelpers.GetSubArray`), string slices are substrings, and span slices are views.

## Spans

`Span<T>` and `ReadOnlySpan<T>` are the aggregate `{ ptr reference, i32 length }`, special-cased like
`Nullable<T>`: no generic code is compiled for them (`FunctionCodeGen.Spans.cs`). The indexer
returns `ref T`, so `span[i] = x` writes through. Span enumerators are `{ ptr, i32 length, i32 index }`,
and `foreach` over a span becomes an index loop. Collection expressions that target a span are
backed by a fresh GC array, which is safe for any span lifetime.

## Unsafe code

Pointers are plain LLVM `ptr`s (`FunctionCodeGen.Unsafe.cs`). Address-of, indirection, `p->f`,
pointer arithmetic (scaled by the element size), `sizeof`, function pointers and `stackalloc` (a
zeroed dynamic `alloca`, or `{ ptr, n }` for a span target) are lowered directly. The GC doesn't
move objects, so `fixed` just takes the address of the first element (null for a null or empty
array or string, as in C#). Roslyn's IOperation API doesn't model most of these, so they are
recognized by syntax shape and child types.

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

Holes with a format string (`{x:F2}`) call the type's `ToString(format)` with .NET's invariant-culture
formatting (`FormatWith`), and alignment (`{x,5}`, `{x,-5}`) pads the result. Enums format by name
(including `[Flags]` combinations), and structs through their `ToString` override or the CLR type
name.

## Exceptions

Zero-cost, table-driven exception handling on the Itanium ABI ([ADR 0005](adr/0005-exceptions.md),
`FunctionCodeGen.Exceptions.cs`, `src/runtime/eh.c`):

- `throw e` calls `csrt_throw_object(e)` and ends the block with `unreachable`. `throw;` calls
  `csrt_rethrow`. Exception objects are ordinary objects whose first fields are shared with
  `csrt_Exception` in `csrt.h` (`{ mt, message, inner, paramName, ... }`). csrt defines the method
  tables of the BCL exception hierarchy, and user exception classes derive from them.
- Calls inside a `try` region are emitted as `invoke`s whose unwind edge goes to that region's
  landing pad. Each landing pad carries a catch clause per handler of the region and of every
  enclosing `try` in the function (type-info = the catch type's method table, null = catch-all),
  plus `cleanup`. LLVM turns that into the standard LSDA in `.gcc_except_table`.
- `csrt_personality` matches clauses in the search phase. In the landing pad, the compiled code
  re-checks the handlers in order (type test, then the `when` filter) and enters the first match.
  Otherwise it runs the `finally` and moves on to the enclosing `try`'s dispatch, or resumes
  unwinding out of the function.
- An exception that no frame handles still runs the `finally` blocks on the way out (a forced
  unwind). Then csrt prints `Unhandled exception. <ToString()>` to stderr and calls `abort()`:
  exit status 134, the same as a .NET process on Linux.

## Static initialization

Static fields are internal, zero-initialized globals (`ModuleCodeGen.StaticField`). A type's
static initialization (its static field initializers in declaration order, then its static
constructor) runs with the timing CoreCLR has (`ModuleCodeGen.StaticInit.cs`):

| type | initialized |
|---|---|
| explicit static constructor | exactly once, on entry to the first of its static methods, accessors or instance constructors (for a struct also its instance methods) that runs |
| static field initializers only (`beforefieldinit`) | on the first static field access (read, write or `ref`); method calls and `new` don't trigger it |
| only side-effect-free constant initializers (literals, `default`, `typeof`, arrays of those) | at startup, in `__csharpc_static_init` (unobservable, so no checks) |
| never used | never |

A lazily initialized type has a `csrt_ClassInit` global `{ state, owner, exception }`. Access
sites and entry points emit an acquire load of `state` and call the type's cold
`__csharpc_cctor_check.<Type>` while it isn't "done". That function calls
`csrt_class_init_begin` (src/runtime/threads.c), runs the initializer through
`csrt_call_catching_void`, then calls `csrt_class_init_end`. Other threads block until the
initializer finishes. The initializing thread may re-enter, so an initialization cycle sees
default values, as in .NET. An escaping exception is wrapped in a `TypeInitializationException`
("The type initializer for 'T' threw an exception.", with the original as `InnerException`).
The wrapper is cached and rethrown on every later access. Not supported yet: static initializers
and static constructors of generic types (`CSLLVM0001`), and the CLR's deadlock resolution when
two threads initialize types that depend on each other.

Referenced corelib types still initialize eagerly at startup. Their initializers have no
observable side effects.

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

Top-level statements compile to Roslyn's synthesized `Program.<Main>$(string[] args)`. Before
static initialization, `main` registers the builtin interface implementations and the variant
interfaces the program uses (see [Interfaces](#interfaces)). `string[] args` is only built when
`Main` reads it. For `async Main`, `main` drives the scheduler with `csrt_task_wait` until the
returned task completes, and rethrows its exception if it faulted. A `void Main` exits with
`Environment.ExitCode`.

## Local functions

Local functions are discovered through `ControlFlowGraph.LocalFunctions` and lowered from
`GetLocalFunctionControlFlowGraph` as ordinary internal functions, mangled under their containing
method. Capturing local functions and lambdas take an environment (see
[Closures and delegates](#closures-and-delegates)). In generic code they are emitted once per
enclosing substitution.

## Iterators and async

Iterators (`yield return`) and `async` methods are lowered to LLVM coroutines. Roslyn's CFG keeps
`await` and `yield` as operations, and LLVM's `CoroSplit` pass builds the state machine. Details,
including the task scheduler in `src/runtime`, are in [10-async.md](10-async.md).

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
Generic instantiations append their type arguments as `I<types>E` (see
[Generics](#generics-monomorphization)).

## Library calls (intrinsics)

Roslyn binds user code against the real .NET reference assemblies, so overload resolution is exact.
csharpc then supplies an implementation for each resolved library member, in this order:

1. **Intrinsics** (`src/Csharpc/CodeGen/Intrinsics.cs`, keyed like `System.Console.WriteLine(int)`,
   plus `BclValueTypes.cs` for `decimal`, `DateTime`, `TimeSpan`, `BigInteger`): members that map to
   an LLVM intrinsic, libm, or a csrt call. Intrinsics outrank corelib shadows.
2. **Corelib shadows** (`src/Csharpc/corelib`, [12-corelib.md](12-corelib.md)): BCL types and
   members written in C# and compiled like user code, e.g. `List<T>`, `Dictionary<K,V>`, LINQ,
   most `string` members, number formatting and parsing.
3. Anything else is `CSLLVM0002` ("has no implementation in the csharpc runtime yet").

`csharpc --intrinsics` prints the current intrinsic list. Representative entries:

| area | members | lowering |
|---|---|---|
| Console | `Write`/`WriteLine` overloads, `ReadLine()` | typed `csrt_console_*` calls. Boxed arguments are unboxed statically; other objects go through `ToString` |
| Math | `Sqrt Floor Ceiling Truncate Round Abs` (floating point) | `llvm.sqrt/floor/ceil/trunc/roundeven/fabs.*` (`Round` is banker's rounding, like .NET) |
| Math | `Max`/`Min` (floating point) | `llvm.maximum/minimum.*`: IEEE 754-2019, NaN-propagating, −0 < +0, as in .NET Core 3.0+ |
| Math | `Pow Atan2 Exp Log Sin Cos Tan Cbrt`, ... | libm |
| Math | integer `Abs`, `Max`, `Min`, `Clamp` | `select` + `icmp`; `Abs(MinValue)` → `OverflowException` |
| primitives | `Parse`, `CompareTo`, ... on `int`, `double`, ... | csrt parsing, or inline compares |
| tasks | `Task`, `ValueTask`, `TaskCompletionSource`, `CancellationToken` | the csrt task scheduler ([10-async.md](10-async.md)) |
| Array | `Length`, `LongLength`, `Rank`, `GetLength`, `GetUpperBound` | loads from the array header |

The direction of travel is from intrinsics to C#: as the corelib grows, entries move from "lowered
by the compiler" to "ordinary C# compiled by csharpc". C stays for the GC, unwinding, threading and
OS boundaries ([04-runtime.md](04-runtime.md)).
