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

- Canonicalization replaces each type argument that is a reference type (class, interface, string, single-dimension array, `object`)
  with a **positional marker**: `__Canon0`, `__Canon1`, …, where the number is the type parameter's position in the flattened parameter
  list of its class and the classes that contain it, outermost first (`Pair<string, Customer>` → `Pair<__Canon0, __Canon1>`;
  `Pair<int, string>` → `Pair<int, __Canon1>`; `Outer<string>.Inner<Customer>` → `Outer<__Canon0>.Inner<__Canon1>`;
  `Dictionary<string, int>` → `Dictionary<__Canon0, int>`). Value types stay (`List<int>` is its own form). A method's own type arguments
  are kept as they are until step 4 shares generic methods.
- *Why markers and not one `__Canon`* (changed in step 3b-1): a canonical body must tell `List<TKey>` from `List<TValue>` when it asks its
  dictionary for a type handle. With one `__Canon`, `Pair<__Canon, __Canon>` names both `List<__Canon>`, and the two requests would share
  a slot, so `Pair<string, Customer>` would answer `is List<TValue>` with `List<string>`'s table. A marker per position keeps every type a
  body names distinct, and the position is stable (it does not depend on which other arguments happen to be reference types), so
  `C<string, int>` and `C<int, string>` can never collide by accident. Calls out of a canonical body name the callee in terms of the
  caller's markers and are re-canonicalized to the callee's own positions (`Pair<__Canon0, __Canon1>` creating `Pair<__Canon1, __Canon0>`
  reaches the same canonical form as any other `Pair` over two references). NativeAOT keys its lookups on the open signature for the same
  reason.
- Markers are internal, abstract classes with `object`'s representation, never instantiated and never observable: a canonical form has a
  layout (field offsets and vtable slots, the same for every instantiation it stands for: instances of `List<string>` and
  `List<Customer>` have the same fields at the same offsets) but no method table, no type name and no reflection record. Abstract and not
  sealed: a type parameter stands for any class, so no lowering decision may reason from the marker being a leaf type (#362: a sealed
  marker made `T[]` look never covariant and dropped the array store check). The guard is twofold: a type that names a marker has a
  stand-in symbol for its method table, which only the dictionary path may replace, and the end of the module checks that nothing uses
  that symbol and that no global, type-name string or reflection record names a marker; a hit is an internal error, never a program.
- Up to eight flattened parameters share; a class with more stays exact. Instantiations whose arguments are delegates (variance is decided
  by a test function per delegate type) or multi-dimensional arrays stay exact for now.
- Function, layout and mangling keys (`Generics.cs`, `GetFunction`, the worklist) move from the constructed symbol to its canonical form
  when the form contains a marker; the mangled name carries the marker (`N14CsharpcCoreLib8__Canon0E`), as ILC's symbol names carry `__Canon`.

### 2. The generic dictionary

A shared body cannot name `T`; whatever depends on the exact `T` comes from a generic dictionary, one constant per exact instantiation.

- **Where the body finds it.** For instance methods of generic classes: a vtable slot of the generic class's own section (appended after
  the virtuals it introduces, so its index is fixed for the canonical class and inherited by derived classes) points at the dictionary,
  and the body loads it from `this`: method table, slot, entry, three dependent loads of constant data. No header field: non-generic
  classes pay nothing. For static methods, generic methods (whose own
  arguments the receiver cannot carry), and methods of generic structs (no method table on `this`): a hidden trailing parameter, the
  "instantiating argument", passed by every caller; a caller in shared code passes a pointer it loaded from its own dictionary.
  Interface and virtual calls into shared instance methods need nothing extra: the receiver brings its method table.
- **What a slot holds** (slots assigned per canonical method from the operations its body needs; filled per exact instantiation):
  method tables of `T` and of types built from it (`T[]`, `List<T>`, `IEnumerable<T>`) for `new`, `newarr`, `box`, `is`/`as`/casts,
  `typeof(T)` and `catch (T)`; the static base of each exact generic class the body touches, with its class-initializer flag; method
  addresses that depend on the exact instantiation (delegates to generic methods, generic virtual methods' targets, calls into other
  shared generic methods that themselves need a dictionary); interface method tables over `T` for the call sites that test or dispatch
  on them; reflection records for `MethodBase.GetCurrentMethod()` and `typeof(T)`.
- **Constant, not lazy at run time.** ILC fills dictionaries lazily through helper stubs because it cannot always know every
  instantiation. csharpc compiles a closed world (ADR 0006), and its worklist already reaches every exact instantiation; each dictionary
  is constant data. No lookup helpers, no locks, no run-time type loading.
- **Slots are assigned during lowering, deterministically** (step 3b-1). Predicting every slot up front would mean a second lowering:
  intrinsics (`Array.Copy`, the span helpers) ask for handles of types that appear only in signatures. So a canonical body gets a slot the
  first time its lowering asks for a handle, and every exact instantiation of its class, already reached or reached later, gets that
  entry filled in the same worklist (filling an entry can reach new method tables and functions). Lowering is single-threaded and the
  worklist order is fixed, so the slot numbers are too: json and regexoptions compiled twice with the flag on give byte-identical IR. The
  up-front analysis stays as the eligibility check: a body may ask only for the handle of a type it names (its instructions' operands,
  the types of the members, locals and parameters it uses, and what those are built from), and any other request is an internal error.
  So is a request after the dictionaries are emitted.
- **Layout.** A flat array of pointers per exact instantiation: the class part (shared by all instance methods of the class) then the
  method parts. Nested dictionaries (a generic method in a generic class) chain through one slot. The compiler records the slot map per
  canonical method so the size of every dictionary is known and reported.

### 3. Method tables, the GC, the unwinder and stack traces

- **Method tables** stay one per exact instantiation (type identity, `GetType()`, `is List<string>`, variance registration and reflection
  are unchanged), plus the dictionary slot in the generic class's vtable section; their vtables and interface maps point at the shared
  code. The header does not change.
- **The GC needs no generic type information from frames.** The collector is conservative and non-moving (ADR 0004;
  `src/runtime/gc.c`, header comment: "Roots are scanned conservatively: the main thread's stack (with callee-saved registers spilled by
  setjmp) and the writable segments", and `scan_range`, which reads every word of a range): it scans stacks, registers and TLS for
  anything that looks like a pointer and never asks a frame what its slots hold; no frame maps exist to extend. A shared frame holding `T` values
  as pointers is scanned like any other frame, and dictionaries are constant data in the image, not heap objects. On wasm32 the roots are
  spilled to the shadow stack (ADR 0011); a value typed by a marker is a pointer and is spilled like any reference. (ILC needs per-frame generic
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

- A call through an interface on a receiver finds the shared implementation in the receiver's interface map. The interface itself,
  when it depends on `T` (`IComparer<T>.Compare`), is named by a dictionary slot instead of a constant (step 3b-2). The exact-code
  call-site cache (`csrt_interface_slots_cached`, receiver method table | map index) cannot be used as is: one shared site sees one
  interface per exact instantiation, and a receiver class can implement several of them (`IComparer<string>` and `IComparer<Animal>`),
  so a hit on the method table alone would call the other interface's implementation, a silent wrong result. Shared sites call
  `csrt_interface_slots_cached_shared`, which also compares the cached map entry's interface; exact sites keep the unvalidated hit.
- Each exact instantiation of the canonical owner notes the dispatch on its own interface (reachability, interface-map slots), and when
  that interface is one `string` or arrays implement, their builtin interface maps are requested too: exact code calls
  `string.CompareTo` directly where shared code dispatches through `IComparable<string>` (#411).
- `foreach` over an `IEnumerable<T>` interface is these dispatches (GetEnumerator, MoveNext, Current, Dispose) and shares; the analysis
  names `IEnumerator<T>` for its dictionary slot.
- Casts and type tests on `T` load the target from the dictionary: `csrt_isinst_class(obj, dict[k])`. The compile-time folds that need an
  exact `T` (`typeof(A) == typeof(B)`, `box T; isinst I`, the default-comparer guards of #223/#269, `IsValueType`/`IsEnum`) stay exact
  for value types (which are not shared) and become dictionary loads plus a compare for the markers.
- Generic virtual methods keep their per-instantiation dispatcher (`GenericVirtualCall`), keyed by the canonical form of the method's
  type arguments plus the dictionary for the exact ones.

### 6. Both front-ends feed the same lowering

The Roslyn front-end and the IL front-end both lower through `FunctionCodeGen` with the substitution `S(...)` (03-lowering.md). Sharing
is a property of the substitution and of the key the worklist uses, not of either front-end: the canonical substitution maps `T` to
a marker, and every place that turns an exact type into code (method-table references, `typeof`, statics, casts, `new`) goes through one
helper that emits a constant for an exact type and a dictionary load for one that names a marker. The IL front-end's own folds
(`IlTypeEqualityConstant`, the `box`/`isinst` folds) consult the same helper. `--via-il` and the Bootstrap (csharpc compiled from IL) test
the IL path on every step.

### 7. What stays per instantiation

- Value-type arguments, always (layouts differ; sharing them would mean boxing or size-generic code; ILC does not share them either).
- **Generic value types over reference types, for now** (open work, step 4). `KeyValuePair<string, int>` as an argument
  (`List<KeyValuePair<string, int>>`) and the methods of generic structs over reference types (`List<string>.Enumerator`) stay exact: a
  struct has no method table on `this` to find a dictionary through, so it needs the hidden instantiating argument. On `json.cs` with the
  flag on (step 3b-1) that is 165 functions of 90 classes with such an argument and 329 functions of generic structs, of 1,482 generic
  functions; at -O2 most of them are inlined, and 8 KB of the classes' code is left in the binary. The self-compile's count is in the
  3b-1 PR.
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
   stays for one release as a way back. Known blockers, each to be closed (not worked around) before then:
   - **Delegates to virtual methods over shared overrides.** `Delegate.Method` answers from the function a delegate holds. A delegate
     bound to a shared method directly (`Func<object, bool> f = pair.Produces`) holds an exact entry of its own, a one-call forwarder per
     instantiation, so it reports `Pair<string, Customer>.Produces` (step 3b-1). This replaced a whole-program "share nothing if
     `Delegate.Method` is referenced" rule, which the self-compile tripped through Roslyn's own reference to it. A delegate bound
     through `ldvirtftn` holds the function found in the vtable, the canonical one, which no record describes: its `Delegate.Method`
     is the explicit NotSupportedException csharpc already gives for a delegate to a virtual method it does not describe. A per-slot
     description is the remaining work (step 3c).
   - **Frames.** `StackFrame` and `StackTrace` objects are not supported by csharpc (their constructors throw NotSupportedException),
     so no frame's method can be read today. Whoever implements them reports a shared frame's exact instantiation, or the definition's,
     which is what .NET's JIT reports for shared code. `MethodBase.GetCurrentMethod()` keeps the method that calls it exact.
   - **The staging limits of step 3b-1**, each a named reason in `CSHARPC_GENERIC_REPORT`: classes with a static constructor (step 3c),
     array copies (`Array.Copy`, `CopyTo`, `SetValue` take the element type from a generic helper), `typeof` over T in source bodies,
     boxing of value types over T, casts to delegate types over T, finalizable classes over T, exception filters, `catch (T)`, statics of
     generic types; after step 3b-2, generic interface methods, interface calls where an array over T may be the receiver, and `foreach`
     other than over an `IEnumerable<T>` interface (struct enumerators over T wait for step 4). The shared-method count on `json.cs` and on the self-compile is
     reported as each one is lifted.
   - **Size and speed**: the size budgets hold with the flag on, and no benchmark is slower with it than without.

### 9. Measurements (before and after every step, against NativeAOT on the same machine, as ADR 0025 requires)

- Binary size (stripped `-O2`) of the size-budget programs, the benchmark suite and programs over the flipped libraries (List, Dictionary,
  Linq, StringBuilder, JSON).
- Compile CPU and wall time, and the **self-compile's peak memory** at `-O0` and `-O2` (it must sit well inside the 16 GB CI runner).
- Throughput: callgrind instructions and wall time of the benchmark suite and the flipped libraries' hot paths.
- A step that makes a hot path slower than both the previous step and NativeAOT goes back to the owner before it merges.

## Baseline (step 1, 2026-11-11)

Measured on main `46d0b194` (.NET 11 GA) on the project's build host (4 cores, 15 GB, shared). csharpc: `-O2`, stripped (default), compile
CPU (user) and peak RSS of the compile, after a warm-up compile that fills the runtime and corelib caches. NativeAOT: `dotnet publish
-c Release -p:PublishAot=true` of the same file as a file-based app, with the app's build cache (ILC's object) removed before the timed
publish; size of the stripped executable. Generic functions: lowered functions that are generic instantiations (`CSHARPC_GENERIC_REPORT`);
canonical forms: the distinct forms with reference-type arguments as `__Canon`; bytes removed: symbol bytes of the copies sharing would drop
(each canonical group keeps its largest copy; copies LLVM inlined are not counted, so this undercounts). `lops2`, `ops2` and `sbops` are the
List, Dictionary/HashSet and StringBuilder throughput programs used for the flips' ADR 0023 rows.

| program | csharpc size | csharpc CPU / peak RSS | NativeAOT size | NativeAOT CPU / peak RSS | generic functions | canonical forms | bytes removed (est.) |
|---|---:|---:|---:|---:|---:|---:|---:|
| hello | 28 544 | 1.3 s / 203 MB | 1 332 984 | 6.6 s / 216 MB | 0 | 0 | 0 |
| exceptions | 36 928 | 1.4 s / 199 MB | 1 332 984 | 6.7 s / 216 MB | 0 | 0 | 0 |
| typeinfo | 43 784 | 1.5 s / 202 MB | 1 332 984 | 7.6 s / 225 MB | 0 | 0 | 0 |
| numbers | 143 688 | 4.4 s / 233 MB | 1 341 208 | 7.9 s / 225 MB | 39 | 29 | 1 159 |
| collections | 167 032 | 3.9 s / 236 MB | 1 354 184 | 10.8 s / 227 MB | 149 | 120 | 1 399 |
| async | 177 472 | 4.3 s / 248 MB | 1 553 128 | 7.1 s / 216 MB | 161 | 102 | 1 418 |
| sbops (StringBuilder) | 213 304 | 5.3 s / 254 MB | 1 341 192 | 13.4 s / 225 MB | 45 | 35 | 1 178 |
| lops2 (List) | 229 128 | 7.8 s / 254 MB | 1 383 224 | 8.1 s / 225 MB | 302 | 244 | 1 399 |
| ops2 (Dictionary/HashSet) | 159 912 | 4.6 s / 237 MB | 1 353 528 | 6.5 s / 217 MB | 95 | 85 | 1 178 |
| linq | 232 792 | 5.5 s / 251 MB | 1 581 128 | 7.4 s / 216 MB | 323 | 258 | 3 674 |
| jsonwriter | 801 272 | 17.2 s / 359 MB | 1 567 208 | 6.6 s / 216 MB | 418 | 313 | 4 351 |
| regex | 818 024 | 17.7 s / 340 MB | 2 301 032 | 19.3 s / 231 MB | 643 | 438 | 7 187 |
| json | 1 572 032 | 32.5 s / 457 MB | 1 989 368 | 8.4 s / 216 MB | 1 520 | 883 | 26 897 |
| regexoptions | 2 045 928 | 40.9 s / 520 MB | 2 765 320 | 11.4 s / 269 MB | 2 937 | 1 717 | 34 545 |

**The self-compile** (csharpc compiling `csharpc.dll`), from CI: Bootstrap run 42943454769 on head `f37498d6` (main plus #319), GitHub's
16 GB Linux runner:

| | wall | peak RSS | lower to LLVM IR | LLVM optimize + codegen |
|---|---:|---:|---|---|
| `-O0` | 6:59 | 14.1 GB | 130 s, 6.1 GB | link step at 13.75 GB |
| `-O2` | 45:44 | 9.7 GB | 247 s, 6.1 GB | 2 464 s, 9.5 GB (8 partitions) |

Its generic instantiations (`CSHARPC_GENERIC_REPORT` on the same tree, from a local run that completed lowering): **305 590 generic
functions, 76 020 canonical forms: sharing removes 229 570 (75.1 %)**. The largest collapses are the array and iterator helpers
instantiated per element type: `Enumerable.Iterator<T>.Select` (4 226 of 4 277 instantiations), each `ArrayEnumerator<T>` member (about
3 740 of 4 350), `ArrayChecks.CopyToUntyped<T>` (3 715 of 4 311), the `SZArrayHelper<T>` array-interface helpers (about 2 250 of 2 840 per
member).

What the baseline says: on the measured programs csharpc is already smaller than NativeAOT, and sharing removes little of their size
(at most about 2 %). Where csharpc loses is compile time on generic-heavy programs (json, jsonwriter and regexoptions take 3 to 4 times
NativeAOT's publish CPU) and the self-compile's memory: the `-O0` peak of 14.1 GB leaves about 2 GB on the CI runner. Three quarters of the
self-compile's generic functions are copies sharing would remove, which is the reason to do step 2 onwards.

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
