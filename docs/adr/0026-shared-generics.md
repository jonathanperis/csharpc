# ADR 0026 — Shared generic code for reference-type instantiations

- **Status:** Proposed (2026-11-10), design note before any code (ADR 0025, Phase 2, first item). Issue: see the PR that adds this file.
- **Context:** [03-lowering.md "Generics (monomorphization)"](../03-lowering.md#generics-monomorphization),
  [ADR 0025](0025-optimizer-before-flips.md), [ADR 0004](0004-memory-management.md) (conservative GC),
  [ADR 0018](0018-reflection-metadata.md) (reflection metadata), [ADR 0016](0016-nativeaot-parity-and-end-state.md) (NativeAOT is the yardstick).

## Problem

csharpc compiles every generic instantiation separately: `List<string>`, `List<Customer>` and `List<object>` are three copies of
every reached method, three method tables and three sets of statics. That was cheap while the corelib's own collections were small
shadows. The framework's IL (ADR 0023) is written for NativeAOT's compiler, which shares one body of code across all reference-type
instantiations of a method (its *canonical form*, `List<__Canon>`), so upstream code is generous with generic types and methods. The
flips measured the cost: Linq's speed-optimized iterators instantiated per element type exhausted the 16 GB CI runner on the `-O0`
self-compile (#290), and System.Text.Json's reflection serializer is 18x the shadow's size. Program size, compile time and the
compiler's peak memory all grow with (generic code reached) x (reference types it is used with).

## Decision (proposed)

Compile generic code over reference-type arguments once per *canonical form*, as NativeAOT does, and keep per-instantiation code for
value-type arguments, where sharing would cost boxing or a size-dependent layout.

### 1. Canonical forms

- A type argument that is a reference type (class, interface, delegate, array of references, string) canonicalizes to `__Canon`; a
  value type stays itself. `Dictionary<string, int>` and `Dictionary<Customer, int>` share `Dictionary<__Canon, int>`;
  `Dictionary<int, string>` is `Dictionary<int, __Canon>`. A method's canonical form also canonicalizes its own type arguments.
- `__Canon` has the representation of `object` (a pointer), so the canonical body has one LLVM signature for every instantiation it
  serves. Field layouts of a generic class over `__Canon` are the same as over any reference type, so instances of `List<string>` and
  `List<Customer>` have identical layouts; only their method tables differ.
- What stays per instantiation: anything with a value-type argument (`List<int>`, `Dictionary<Guid, T>`), and generic code that the
  compiler already specializes by type identity at compile time and would lose that on sharing (below, "Exact paths").

### 2. The generic dictionary (run-time type information the shared body needs)

A shared body cannot name `T` at compile time; everything that depends on the exact `T` comes from a *generic dictionary*, a constant
table emitted per exact instantiation:

- **Where it comes from.** For an instance method of a generic class: the receiver's method table (`this->MT`) carries a pointer to
  its class's dictionary (a new method-table header field; `csrt.h`, `TypeMapper.MethodTableHeader`, `ModuleCodeGen.EmitMethodTable`
  change together). For a static method, a generic method, or a method of a generic struct: an extra hidden parameter (the
  dictionary pointer), passed by every caller, as NativeAOT's "instantiating argument".
- **What it holds** (slots assigned by the compiler per canonical method, filled per exact instantiation): method tables of `T` and of
  types built from it (`T[]`, `List<T>`, `IEnumerable<T>`), for `new`, `is`/`as`/casts, `typeof(T)`, array allocation and boxing;
  addresses of exact generic static bases (statics stay per instantiation: `Cache<string>.Value` and `Cache<Customer>.Value` are
  different fields) together with their class-initializer check; method pointers that need the exact instantiation (a generic
  virtual method's target, a delegate to a generic method, `default(T)` is just null for `__Canon`); interface map entries for
  interfaces over `T` where the call site cannot use the receiver's own map.
- **Lazy vs eager.** NativeAOT fills dictionaries lazily through helper stubs. csharpc compiles a closed world (ADR 0006), so every exact
  instantiation that is reached is known at compile time and its dictionary is a constant; no lazy lookup, no lock, no runtime type
  loader.

### 3. What changes elsewhere

- **Method tables.** One per exact instantiation, as today (type identity, `GetType()`, `is List<string>` are unchanged), but their
  vtables and interface maps point at the *shared* code. The new header field points at the dictionary. Interface maps keep their
  per-instantiation identities (`IEnumerable<string>` and `IEnumerable<Customer>` are different interface method tables).
- **Interface dispatch.** The call-site cache (`csrt_interface_slots_cached`) and the interface stubs are unchanged: they key on the
  receiver's method table and find the shared implementation in its map. Inside a shared body, a call on `IComparer<T>` names the
  interface by its dictionary slot instead of by a constant.
- **Virtual and generic virtual methods.** Virtual slots point at shared code. The generic-virtual-method dispatcher
  (`GenericVirtualCall`) gains the dictionary of the method's type arguments as its key when they are reference types.
- **GC view of frames.** None: the collector is conservative (ADR 0004) and scans stacks and registers without stack maps, so a shared
  frame that holds `T` values as pointers needs nothing new. Dictionaries are constant data in the image (no GC object). On wasm32,
  where roots are spilled to the shadow stack (ADR 0011), a `__Canon` value is a pointer and is spilled like any reference.
- **Reflection** (ADR 0018). Metadata is per exact type, unchanged; invoke stubs for a method of a shared body pass the right dictionary.
  `MethodBase.GetCurrentMethod()` and stack traces name the exact method where the caller knows it and the canonical method otherwise;
  NativeAOT prints the canonical frame too (`List<__Canon>.Add`). Stack-trace text is part of the exactness rule only for the program's
  own frames; a shared library frame whose name changes is checked against what NativeAOT prints, not the JIT.
- **Exact paths that must stay.** Several compiler optimizations depend on knowing `T` exactly: `typeof(A) == typeof(B)` folding, the
  default-comparer devirtualization and type guards (#223/#269), `box T; isinst` folding (#308), string-specific fast paths
  (`EqualityComparer<string>.Default`, `string.GetHashCode`). Over `__Canon` these become dictionary loads and tests at run time. Rule:
  a canonical method whose body uses an exact-type optimization on `T` that would turn a compile-time constant into a run-time test
  on a hot path is *not shared* for that argument when `T` is `string` (the most common, and the one most fast paths exist for); the
  compiler records the decision per method so it is measurable.
- **Debug info and symbols.** One DWARF subprogram per shared body, named by its canonical form; mangled names get an `I__CanonE` form.
- **Static initialization.** Class initializers stay per exact instantiation; the shared body checks the one in its dictionary.

### 4. Staging (each step its own issue and PR, Bootstrap and the 16-shard native suite on each)

1. **Measure first**: count, per program (benchmark suite, the flipped libraries' samples, the self-compile), the instantiations over
   reference types, the functions they produce and the bytes, and the share NativeAOT's ILC would merge. Add the numbers to this ADR.
2. **Infrastructure without behaviour change**: the `__Canon` symbol, canonicalization of instantiations, the method-table header field
   and dictionaries emitted (unused). Byte-identical output; size grows by the header field.
3. **Share generic classes' instance methods** whose bodies need nothing from the dictionary beyond the receiver's method table
   (the bulk of `List<T>`, `Dictionary<TKey,TValue>` code over reference keys). Measure size, compile time, peak memory and speed.
4. **Share the rest**: static and generic methods with the hidden dictionary parameter, generic virtual methods, delegates, statics.
5. **Exact-path policy**: the not-shared-for-`string` rule above, tuned with measurements.

### 5. How it is measured

Against NativeAOT on the same machine (ADR 0025): binary size, compile CPU and peak memory, and throughput (callgrind instructions and
wall time) of the benchmark suite and of programs over the flipped libraries (List, Dictionary, Linq, StringBuilder, JSON), before and
after each step. The self-compile's peak memory must fall well inside the CI runner. A step that makes any hot path slower than both
the previous step and NativeAOT goes back to the owner before it merges.

## Alternatives considered

- **Keep full monomorphization and trim harder** (whole-program devirtualization, dead-method elimination): needed anyway (ADR 0025,
  next items), but it does not remove the multiplication by reference types, which is the measured problem.
- **Share by LLVM function merging** (`mergefunc`) after the fact: catches identical bodies only, after paying for generating and
  optimizing each copy; compile time and peak memory, the CI problem, do not improve.
- **Share everything, value types included** (the JIT's approach for some cases): value-type layouts differ, so bodies would need
  boxing or size-generic code; NativeAOT does not do it either.

## Consequences

- The code generator's core changes: instantiation keys, method-table layout, calling convention of shared static and generic methods.
  It is staged so each step is measurable and revertible.
- Programs and the self-compile get smaller and compile faster in proportion to how much generic code they use over reference types;
  hot paths over `T = string` keep their exact code by the policy in 3.
- Debugging and stack traces show canonical names for shared library frames, as NativeAOT's do.
