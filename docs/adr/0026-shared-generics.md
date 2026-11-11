# ADR 0026 — Shared generic code for reference-type instantiations

- **Status:** Accepted (2026-11-11): implements ADR 0025 Phase 2, item 1. Design note before any code. Issue
  [#314](https://github.com/jonathanperis/csharpc/issues/314).
- **Context:** [03-lowering.md "Generics (monomorphization)"](../03-lowering.md#generics-monomorphization),
  [ADR 0025](0025-optimizer-before-flips.md) (the compiler catches up before flips resume),
  [ADR 0024](0024-runtime-boundary.md) (boundary A: csharpc owns the code generator and the runtime),
  [ADR 0004](0004-memory-management.md) (conservative GC), [ADR 0012](0012-stack-traces.md) (stack traces),
  [ADR 0018](0018-reflection-metadata.md) (reflection metadata), [ADR 0006](0006-closed-world-compilation.md) (closed world).

## Problem

csharpc compiles every generic instantiation separately: `List<string>`, `List<Customer>` and `List<object>` are three copies of every
method reached, three method tables and three sets of statics. That was affordable while the corelib's collections were small shadows.
The framework's IL (ADR 0023) is written for NativeAOT's compiler (ILC), which compiles one body per *canonical form* and shares it across
every reference-type instantiation, so upstream code is generous with generic types and methods. The flips measured what csharpc pays:
Linq's iterators instantiated per element type exhausted the 16 GB CI runner on the `-O0` self-compile (#290), and the reflection-based
JSON serializer is 18x the shadow's size. Binary size, compile time and the compiler's peak memory grow with (generic code reached) x
(distinct reference types it is used with). ILC's sharing is the main structural reason its outputs and its compile are smaller.

## Decision (proposed)

Compile generic code over reference-type arguments once per canonical form, as ILC does; keep per-instantiation code for value-type
arguments and for hot paths that a measured heuristic (below) marks. Build every dictionary at compile time: the program is a closed world.

### 1. Canonical forms

- Canonicalization replaces each type argument that is a reference type (class, interface, delegate, string, array, `object`) with
  `__Canon`, recursively (`List<List<string>>` → `List<__Canon>`; `Dictionary<string, int>` → `Dictionary<__Canon, int>`). Value types
  stay (`List<int>` is its own form; `KeyValuePair<string, int>` → `KeyValuePair<__Canon, int>`). A method's own type arguments are
  canonicalized the same way, as are those of every containing type.
- `__Canon` is a compiler-internal class symbol with `object`'s representation. A canonical body therefore has one LLVM signature for
  every instantiation it serves, and a generic class's instance layout over `__Canon` is the layout over any reference type: instances of
  `List<string>` and `List<Customer>` have the same fields at the same offsets.
- Function, layout and mangling keys (`Generics.cs`, `GetFunction`, the worklist) move from the constructed symbol to its canonical form
  when the form contains `__Canon`; the mangled name carries `I__CanonE`, as ILC's symbol names do.

### 2. The generic dictionary

A shared body cannot name `T`; whatever depends on the exact `T` comes from a generic dictionary, one constant per exact instantiation.

- **Where the body finds it.** For instance methods of generic classes: a new method-table header field points at the class's
  dictionary, and the body loads it from `this` (one load more than a field access). For static methods, generic methods (whose own
  arguments the receiver cannot carry), and methods of generic structs (no method table on `this`): a hidden trailing parameter, the
  "instantiating argument", passed by every caller; a caller in shared code passes a pointer it loaded from its own dictionary.
  Interface and virtual calls into shared instance methods need nothing extra: the receiver brings its method table.
- **What a slot holds** (slots assigned per canonical method from the operations its body needs; filled per exact instantiation):
  method tables of `T` and of types built from it (`T[]`, `List<T>`, `IEnumerable<T>`) for `new`, `newarr`, `box`, `is`/`as`/casts,
  `typeof(T)` and `catch (T)`; the static base of each exact generic class the body touches, with its class-initializer flag; method
  addresses that depend on the exact instantiation (delegates to generic methods, generic virtual methods' targets, calls into other
  shared generic methods that themselves need a dictionary); interface method tables over `T` for the call sites that test or dispatch
  on them; reflection records for `MethodBase.GetCurrentMethod()` and `typeof(T)`.
- **Eager, not lazy.** ILC fills dictionaries lazily through helper stubs because it cannot always know every instantiation. csharpc
  compiles a closed world (ADR 0006), and its worklist already reaches every exact instantiation; each dictionary is constant data,
  emitted when the instantiation is reached. No lookup helpers, no locks, no run-time type loading; a slot load is one memory read.
- **Layout.** A flat array of pointers per exact instantiation: the class part (shared by all instance methods of the class) then the
  method parts. Nested dictionaries (a generic method in a generic class) chain through one slot. The compiler records the slot map per
  canonical method so the size of every dictionary is known and reported.

### 3. Method tables, the GC, the unwinder and stack traces

- **Method tables** stay one per exact instantiation (type identity, `GetType()`, `is List<string>`, variance registration and reflection
  are unchanged), plus the dictionary field in the header; their vtables and interface maps point at the shared code. `csrt.h`,
  `TypeMapper.MethodTableHeader` and `ModuleCodeGen.EmitMethodTable` change together (the shared-ABI rule).
- **The GC needs no generic type information from frames.** The collector is conservative and non-moving (ADR 0004;
  `src/runtime/gc.c`, header comment: "Roots are scanned conservatively: the main thread's stack (with callee-saved registers spilled by
  setjmp) and the writable segments", and `scan_range`, which reads every word of a range): it scans stacks, registers and TLS for
  anything that looks like a pointer and never asks a frame what its slots hold; no frame maps exist to extend. A shared frame holding `T` values
  as pointers is scanned like any other frame, and dictionaries are constant data in the image, not heap objects. On wasm32 the roots are
  spilled to the shadow stack (ADR 0011); a `__Canon` value is a pointer and is spilled like any reference. (ILC needs per-frame generic
  context reporting because its GC is precise; that work does not exist here.)
- **The unwinder and exception dispatch.** Unwinding is DWARF/SEH through libunwind with csharpc's personality (ADR 0005, 0012); frames of
  shared code unwind like any other. The one change: a `catch (T)` (or a filter on `T`) in shared code cannot name `T`'s method table as a
  constant landing-pad clause, so it becomes a catch-all clause followed by a type test against the dictionary's slot and a rethrow when
  it fails, the shape csharpc already uses for filters.
- **Stack traces** (ADR 0012) take the method index from a per-site descriptor. .NET prints generic frames with their definitions'
  type parameters, not with the exact arguments: checked against .NET 11 (the oracle), `Repo<string>.Save("a")` and
  `Repo<Customer>.Save(c)` both print ``at Repo`1.Save(T item)``, and `Repo<int>.Make<string>("s")` prints ``at Repo`1.Make[U](U u)``.
  The canonical method's index therefore prints the same text: no change in output. Debug info gets one DWARF subprogram per shared body, named by its canonical form, as ILC's are.

### 4. Statics and class initializers

Statics stay per exact instantiation (`Cache<string>.Value` and `Cache<Customer>.Value` are different fields), and so do class
initializers and their once-only flags (03-lowering.md "Static initialization"). A shared body reaches them through the dictionary's
static-base slot and checks the initializer flag it finds next to it. A `[ThreadStatic]` field of a generic class gets a TLS slot per exact
instantiation, also through the dictionary.

### 5. Interface dispatch and casts in shared code

- A call through an interface on a receiver is unchanged: the call-site cache (`csrt_interface_slots_cached`) keys on the receiver's
  method table and finds the shared implementation in its map. The interface itself, when it depends on `T` (`IComparer<T>.Compare`), is
  named by a dictionary slot instead of a constant; the per-site cache stays valid because the site still sees one interface per exact
  instantiation in practice, and a miss is only a slower lookup.
- The interface stubs (`ModuleCodeGen.InterfaceStubs.cs`) compare the receiver's method table with the few classes that implement an
  interface; in shared code they compare against the classes for every instantiation the canonical body serves, still a small set.
- Casts and type tests on `T` load the target from the dictionary: `csrt_isinst_class(obj, dict[k])`. The compile-time folds that need an
  exact `T` (`typeof(A) == typeof(B)`, `box T; isinst I`, the default-comparer guards of #223/#269, `IsValueType`/`IsEnum`) stay exact
  for value types (which are not shared) and become dictionary loads plus a compare for `__Canon`.
- Generic virtual methods keep their per-instantiation dispatcher (`GenericVirtualCall`), keyed by the canonical form of the method's
  type arguments plus the dictionary for the exact ones.

### 6. Both front-ends feed the same lowering

The Roslyn front-end and the IL front-end both lower through `FunctionCodeGen` with the substitution `S(...)` (03-lowering.md). Sharing
is a property of the substitution and of the key the worklist uses, not of either front-end: the canonical substitution maps `T` to
`__Canon`, and every place that turns an exact type into code (method-table references, `typeof`, statics, casts, `new`) goes through one
helper that emits a constant for an exact type and a dictionary load for one that contains `__Canon`. The IL front-end's own folds
(`IlTypeEqualityConstant`, the `box`/`isinst` folds) consult the same helper. `--via-il` and the Bootstrap (csharpc compiled from IL) test
the IL path on every step.

### 7. What stays per instantiation

- Value-type arguments, always (layouts differ; sharing them would mean boxing or size-generic code; ILC does not share them either).
- `Nullable<T>`, spans and other `ref struct`/by-ref-like generics over reference types stay exact where their lowering depends on `T`'s
  representation (spans of references are still spans of pointers, so most can share).
- **Hot paths behind a measured heuristic.** A canonical method whose body uses an exact-type optimization on `T` that would become a
  run-time test (the comparer devirtualizations, `typeof(T) == typeof(string)` branches, string fast paths) is compiled exact for the
  instantiations that are hot. Proposed rule, to be tuned by measurement: methods of `Dictionary`/`HashSet`/`EqualityComparer`/`Comparer`
  and any method marked `[AggressiveInlining]` stay exact for `string`; everything else shares. The compiler records each decision so it is
  visible in a report (`--report-sharing`).

### 8. Migration plan (small PRs, each behind a flag, each with Bootstrap and the native suite on a `ci/m6-*` branch)

1. **Measure first** (no code generator change): count per program the instantiations over reference types, the functions and bytes they
   produce, and how many ILC would merge (the benchmark suite, the flipped libraries' samples, the self-compile). The numbers go into this
   ADR before step 2.
2. **Infrastructure, off by default** (`--shared-generics`, `CSHARPC_SHARED_GENERICS=1`): the `__Canon` symbol, canonicalization of
   keys, the method-table header field and the dictionaries emitted but unused. Output byte-identical with the flag off; the header field
   costs a pointer per method table.
3. **Share the instance methods of generic classes** that need nothing beyond the receiver's method table (the bulk of `List<T>` and
   `Dictionary<TKey,TValue>` code over reference types). Flag on in tests; measure.
4. **Share the rest**: static and generic methods with the hidden argument, generic virtual methods, delegates to generic methods,
   statics and class initializers, `catch (T)`.
5. **The per-instantiation heuristic** of section 7, tuned with measurements.
6. **Flag on by default** once the suite is green on all five platforms with it on and every measurement in section 9 holds; the flag
   stays for one release as a way back.

### 9. Measurements (before and after every step, against NativeAOT on the same machine, as ADR 0025 requires)

- Binary size (stripped `-O2`) of the size-budget programs, the benchmark suite and programs over the flipped libraries (List, Dictionary,
  Linq, StringBuilder, JSON).
- Compile CPU and wall time, and the **self-compile's peak memory** at `-O0` and `-O2` (it must sit well inside the 16 GB CI runner).
- Throughput: callgrind instructions and wall time of the benchmark suite and the flipped libraries' hot paths.
- A step that makes a hot path slower than both the previous step and NativeAOT goes back to the owner before it merges.

## Alternatives considered

- **Keep full monomorphization and trim harder** (whole-program devirtualization, dead-method elimination): needed anyway (ADR 0025's
  next items), but it does not remove the multiplication by reference types, which is the measured problem.
- **Merge identical functions after the fact** (LLVM `mergefunc`, linker ICF): catches only bodies that came out identical, after paying
  for generating and optimizing every copy; the compile-time and memory problem stays.
- **Share value-type instantiations too** (size-generic code): different layouts need boxing or runtime size arithmetic in every access;
  NativeAOT does not do it.
- **Lazy dictionaries as ILC builds them**: unnecessary in a closed world; eager constants are cheaper at run time and simpler.

## Consequences

- The code generator's core changes (worklist keys, method-table header, the calling convention of shared static and generic methods).
  It is staged behind a flag so each step is measurable and revertible.
- Programs and the self-compile get smaller and compile faster in proportion to the generic code they use over reference types; hot paths
  over `string` keep their exact code by the heuristic.
- Frames of shared library code print and debug under their canonical names, as NativeAOT's do; user frames print as today.
