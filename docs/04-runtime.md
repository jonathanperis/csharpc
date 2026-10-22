# 04 — Runtime

Every compiled program links against **csrt**, the csharpc bootstrap runtime: `src/runtime/csrt.h` and
`src/runtime/csrt.c`, a few hundred lines of C11. The driver compiles it on demand and caches the object
under `~/.cache/csharpc/` ([02-architecture.md](02-architecture.md#5-link-srccsharpcdrivertoolchaincs)).

csrt is deliberately tiny. It's the "native layer" that will remain once most of the library is
written in C# ([ADR 0007](adr/0007-runtime-in-c-first.md)).

## ABI contract

The compiler and csrt must agree on these layouts (see `csrt.h`, `TypeMapper.cs`, `ModuleCodeGen.cs`):

```c
struct csrt_MethodTable {           // 24 bytes on 64-bit targets
    const csrt_MethodTable* base;   // null for System.Object
    const char* name;               // "Ns.Outer+Inner", UTF-8
    uint32_t instance_size;         // bytes, including the header
    uint32_t flags;                 // bit 0: abstract
    // void* vtable[] follows; slots 0..2 = ToString, Equals, GetHashCode
};

object : { const csrt_MethodTable* mt; fields... }
string : { const csrt_MethodTable* mt; int32_t length; uint16_t chars[length]; }   // UTF-16
T[]    : { const csrt_MethodTable* mt; int64_t length; T data[length]; }           // data @ 16
```

The built-in types `csrt_Object_MT`, `csrt_String_MT` and `csrt_Array_MT` are
`csrt_BuiltinMethodTable`s (header plus three vtable slots), so any object, including a string held
in an `object` variable, can be dispatched through slot 0 by `csrt_object_to_string`.

Compiler-generated method tables for user classes are emitted by csharpc as internal constants with
the same header. Their `base` points at the base class's table or at `csrt_Object_MT`.

## What csrt provides today

| Area | Functions |
|---|---|
| Lifecycle | `csrt_init(argc, argv)` (64 KiB fully-buffered stdout), `csrt_shutdown()` (flush), `csrt_get_args()` (`string[]` without argv[0]) |
| Allocation | `csrt_alloc(mt, size)` (zeroed), `csrt_new_array(element_size, length)` (`OverflowException` if `length < 0`). **Memory is never freed** ([ADR 0004](adr/0004-memory-management.md)) |
| Type system | `csrt_isinst_class`, `csrt_castclass` (walk the `base` chain), `csrt_object_to_string` (null → `""`, else vtable slot 0), default `ToString` (type name), `Equals` (reference equality), `GetHashCode` (address hash) |
| Strings | `concat` (null = empty), `equals` (ordinal), `is_null_or_empty`, `char_at` (bounds-checked), `from_{i32,i64,u32,u64,f64,f32,bool,char}` |
| Console | `csrt_console_write_{string,i32,i64,u32,u64,f64,f32,bool,char}`, `csrt_console_newline`. UTF-16 → UTF-8 with surrogate pairs; lone surrogates become U+FFFD |
| Exceptions | `csrt_throw_{null_reference,index_out_of_range,divide_by_zero,overflow,invalid_cast}()`, `csrt_throw(mt, message)` |
| Environment | `csrt_environment_tick_count64()` (`CLOCK_MONOTONIC` ms), `csrt_environment_exit(code)` |

### Number formatting

`format_real` reproduces .NET Core 3.0+ `double.ToString()` / `float.ToString()` in the **invariant
culture**:

- The output is the shortest digit string that round-trips (try 1…17 significant digits for
  `double`, 1…9 for `float`, keep the first that parses back to the same value).
- Scientific notation is used when the decimal exponent is ≥ 15 or < −4, written as `E+XX`/`E-XX`
  with at least two exponent digits (`1E+15`, `1E-05`).
- Special values: `NaN`, `-0`, and infinities. The target is the invariant culture's `Infinity`
  / `-Infinity`, which is what .NET 11 printed in our test environment. Under ICU cultures such as
  en-US, .NET prints `∞`. The first csrt version printed `∞`, and that is being switched; see the
  spike log.

Because the test oracle is real .NET ([05-testing.md](05-testing.md)), any divergence shows up as a
test failure. This is how the `Infinity` vs `∞` question was settled
([07-spike-log.md](07-spike-log.md)).

### Exceptions: unhandled-only

No unwinding exists yet. Every throw is treated as an **unhandled exception**: csrt flushes stdout,
prints `Unhandled exception. <Type>: <Message>` to stderr with .NET's exact message texts
(`Object reference not set to an instance of an object.`, `Index was outside the bounds of the
array.`, `Attempted to divide by zero.`, `Arithmetic operation resulted in an overflow.`,
`Specified cast is not valid.`), and calls `abort()`. A .NET process with an unhandled exception on
Linux also ends with SIGABRT, so both exit with status **134**. The differential tests check this.
The .NET stack trace isn't reproduced. Real exceptions are designed in
[ADR 0005](adr/0005-exceptions.md).

## The long-term plan: a corelib in C#

csrt's C functions (and the compiler's `Intrinsics.cs` table) are scaffolding. The target
architecture is what Rust and NativeAOT do:

```
┌──────────────────────────────────────────────────────────────┐
│ user code (C#)                                               │
├──────────────────────────────────────────────────────────────┤
│ csharpc corelib (C#, compiled by csharpc)                    │  System.Object, String, Array, Span<T>,
│                                                              │  Math, Console, List<T>, Dictionary<K,V>,
│                                                              │  StringBuilder, number formatting/parsing,
│                                                              │  exceptions, LINQ subset, ...
├──────────────────────────────────────────────────────────────┤
│ csrt (C, small)                                              │  GC, allocation, unwinding personality,
│                                                              │  thread/TLS, OS calls (write, mmap, clock)
└──────────────────────────────────────────────────────────────┘
```

- Rust's `core`/`alloc`/`std` are ordinary Rust compiled by rustc. NativeAOT's
  `System.Private.CoreLib` is ordinary C# compiled by the same AOT compiler on top of a thin native
  layer. csharpc follows both.
- The corelib compiles together with the user program in the same closed-world module
  ([ADR 0006](adr/0006-closed-world-compilation.md)), so LLVM inlines and specializes across
  user/library boundaries and drops whatever is unused.
- **Binding strategy during the transition.** Roslyn still binds user code against the real .NET
  reference assemblies, so API shapes and overload resolution stay exact. For each resolved library
  member, csharpc uses, in order: (1) a corelib implementation compiled from C# source, (2) an
  `Intrinsics.cs` entry, or (3) a `CSLLVM0002` error. Eventually, binding against the corelib's own
  reference assembly (as bflat's zerolib does) would make the supported surface explicit.
- Formatting and parsing move to C# first, since those are where .NET's behavior is subtle and
  where exact compatibility matters.
- This requires generics, structs, interfaces and exceptions in the compiler (milestone M1), which
  is why those come first in [06-roadmap.md](06-roadmap.md).

## GC plan

The full design is in [ADR 0004](adr/0004-memory-management.md). In short:

1. **Now:** bump/`calloc` allocation; memory is never freed. This is fine for short-running programs
   and benchmarks with bounded allocation.
2. **Next:** a conservative mark-sweep collector inside csrt. Roots come from scanning the native
   stack, registers (via `setjmp`) and the data segment. Heap objects are recognized through
   allocator metadata, and object interiors are scanned conservatively at first, then precisely
   using per-type pointer maps emitted in method tables. It doesn't move objects.
3. **Later:** a precise collector with LLVM statepoints / stack maps (as gollvm does), which also
   allows moving and compacting. On wasm32, a shadow stack (as NativeAOT-LLVM does).
