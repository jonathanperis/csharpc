# 04 — Runtime

Every compiled program links against **csrt**, the csharpc native runtime: the C11 sources in
`src/runtime` (`csrt.h` is the ABI). The driver compiles it on demand and caches the object
under `~/.cache/csharpc/` ([02-architecture.md](02-architecture.md#5-link-srccsharpcdrivertoolchaincs)).

csrt is the "native layer". Library logic lives in the C# corelib instead
([ADR 0007](adr/0007-runtime-in-c-first.md), [ADR 0008](adr/0008-runtime-in-csharp.md)); see
[the C / C# boundary](#the-c--c-boundary).

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
| Strings | `concat` (null = empty), `equals` (ordinal), `is_null_or_empty`, `char_at` (bounds-checked), `from_{i32,i64,u32,u64,bool,char}` (doubles: corelib) |
| Console | `csrt_console_write_{string,i32,i64,u32,u64,bool,char}`, `csrt_console_newline`. UTF-16 → UTF-8 with surrogate pairs; lone surrogates become U+FFFD |
| Exceptions | `csrt_throw_{null_reference,index_out_of_range,divide_by_zero,overflow,invalid_cast}()`, `csrt_throw(mt, message)` |
| Environment | `csrt_environment_tick_count64()` (`CLOCK_MONOTONIC` ms), `csrt_environment_exit(code)` |

### Number formatting and parsing (C#)

Number formatting and parsing aren't in csrt anymore. They're corelib C#
(`src/Csharpc/corelib/Number*.cs`, [ADR 0008](adr/0008-runtime-in-csharp.md)) and follow .NET's
own algorithms; the culture-dependent parts (symbols, group sizes, patterns) come from the
`NumberFormatInfo` of the provider or the current culture ([19 — Globalization](19-globalization.md)).
With the invariant culture:

- `double.ToString()` / `"R"` / `"G"`: the shortest digits that round-trip, from the Steele & White
  / Burger & Dybvig free-format algorithm on exact big integers (`BigNum.cs`). Fixed notation is
  used while the decimal exponent is at most 17 for `double` (9 for `float`) and at least −5;
  otherwise scientific notation (`1E+18`, `1E-05`). Special values print as `NaN`, `Infinity`,
  `-Infinity` and `-0`.
- `F`/`E`/`N`/`P`/`C`/`G<n>` on doubles round the **exact** binary value, ties to even
  (`0.125.ToString("F2") == "0.12"`). Custom formats first round doubles to 15 significant digits
  (floats: 7), then round half up, as .NET does. Integers and decimals round half up and never
  print `-0`.
- Parsing is correctly rounded: Clinger's fast path when the digits and the power of ten are exact
  doubles, otherwise an exact big-integer quotient with a sticky bit.

Because the test oracle is real .NET ([05-testing.md](05-testing.md)), any divergence shows up as a
test failure (`tests/samples/162_number_format`, `163_number_parse`).

### Exceptions: unhandled-only

No unwinding exists yet. Every throw is treated as an **unhandled exception**: csrt flushes stdout,
prints `Unhandled exception. <Type>: <Message>` to stderr with .NET's exact message texts
(`Object reference not set to an instance of an object.`, `Index was outside the bounds of the
array.`, `Attempted to divide by zero.`, `Arithmetic operation resulted in an overflow.`,
`Specified cast is not valid.`), and calls `abort()`. A .NET process with an unhandled exception on
Linux also ends with SIGABRT, so both exit with status **134**. The differential tests check this.
The .NET stack trace isn't reproduced. Real exceptions are designed in
[ADR 0005](adr/0005-exceptions.md).

`Exception.ToString()` therefore has no `   at Namespace.Type.Method(Args) in path:line N` lines.
Matching them byte for byte is out of reach. The frames would need .NET's method-signature
formatting of IL names that csharpc never sees: Roslyn's lambda, async and iterator method names,
and "--- End of stack trace from previous location ---" markers. They would also need line tables
in every binary, and they would have to undo LLVM's inlining, since `dotnet run` builds Debug
without it. csharpc doesn't invent frames that would differ anyway. ToString prints exactly what
.NET prints for an exception that was never thrown: the type, the message, and the inner
exceptions. Because a thrown exception's text then differs, the compiler warns (`CSLLVM0006`) at
each place where a program turns an exception into text. Reading
`Exception.StackTrace` is a compile error (`CSLLVM0001`).

## The C / C# boundary

The target architecture is the one Rust and NativeAOT use: library code written in the language
itself, on top of a thin native layer.

```
┌──────────────────────────────────────────────────────────────┐
│ user code (C#)                                               │
├──────────────────────────────────────────────────────────────┤
│ csharpc corelib (C#, src/Csharpc/corelib, compiled with the  │  collections, LINQ, strings, char,
│ program; ADR 0008)                                           │  StringBuilder, number formatting and
│                                                              │  parsing, decimal, BigInteger,
│                                                              │  DateTime/TimeSpan, Math.Round, ...
├──────────────────────────────────────────────────────────────┤
│ csrt (C, src/runtime)                                        │  GC + allocator, unwinding/EH, threads,
│                                                              │  async scheduler core, console + OS calls,
│                                                              │  startup, type-system primitives
└──────────────────────────────────────────────────────────────┘
```

**Stays in C:** things that are native by nature or live below the object model:

- the GC and allocator (`gc.c`);
- exception unwinding and messages (`eh.c`);
- threads and monitors (`threads.c`) and the async scheduler core (`async.c`);
- console I/O and syscalls (`console.c`, `csrt.c`);
- startup (`csrt_init`);
- casts, interface dispatch and boxing method tables (`csrt.c`, `type.c`, `delegates.c`).

Also in C: .NET's ICU shim (`globalization/`, System.Globalization.Native, see
[19 — Globalization](19-globalization.md)), and, next in line to move, `Enum.Parse` (`parse.c`). The `decimal.c`, `bigint.c` and `datetime.c` files that remain only hold
boxed method tables and the clocks.

**Lives in C#:** library logic that is ordinary code:

- number formatting and parsing (`Number.Formatting.cs`, `Number.Parsing.cs`, `Number.cs`, `BigNum.cs`);
- string operations and `char` with Unicode tables generated from .NET (`StringOps.cs`,
  `UnicodeData.g.cs`, `tools/unicode`);
- `StringBuilder` (`StringBuilder.cs`);
- `decimal` (`DecimalOps.cs`), `BigInteger` (`BigIntegerOps.cs`), and `DateTime`/`TimeSpan` (`DateTimeOps.cs`);
- collections and LINQ.

How the two sides call each other:

- **Compiler → C#.** Library members are implemented, in order of preference, by:
  1. a corelib shadow (`[Shadows]`, `[ShadowMembers]`, `[ShadowInstance]`; [12-corelib.md](12-corelib.md));
  2. an `Intrinsics.cs` entry (an LLVM intrinsic or a runtime call);
  3. a `CSLLVM0002` "not supported" error.

  A runtime call whose function has moved to C# is redirected by `CodeGen/CoreLibRuntime.cs`, so
  call sites keep naming the old `csrt_*` symbol.
- **C → C#.** A corelib method marked `[RuntimeExport("csrt_x", "trigger")]` is emitted under the C
  name `csrt_x` when the program references `trigger`. For example, a boxed double's `ToString`
  slot in `csrt_Boxed_Double_MT` calls `csrt_string_from_f64`. csrt declares weak fallbacks for
  these symbols so that programs which never reach that code still link.
- **C# → C.** Corelib code reaches the runtime through ordinary BCL calls that lower to `csrt_*`
  (`Console.Write`, `new string`, ...) and through a few `CoreIntrinsics` (`FastAllocateString`,
  `StringChars`, bit casts) that need no `unsafe` code.

## Platforms

csrt is C11 + POSIX threads. What differs per OS is behind `#ifdef __APPLE__`, `#ifdef _WIN32` and
`#ifdef __wasi__` (Windows: MinGW-w64 with the UCRT; winpthreads provides the pthread API, so the
threading code is shared. WASI: wasi-libc, one thread; `wasi.c` implements the pthread subset csrt
uses):

| Concern | Linux (glibc) | macOS (arm64) | Windows (x64, MinGW-w64) | WASI (wasm32, wasi-libc) |
|---|---|---|---|---|
| GC static roots | writable `PT_LOAD` segments via `dl_iterate_phdr` | writable `LC_SEGMENT_64`s of image 0 (`_dyld_get_image_header`) | writable sections of the executable's PE image (`GetModuleHandle(NULL)`) | the data segment, `__global_base` to `__data_end` |
| GC thread-local roots | the executable's `PT_TLS` block (`dlpi_tls_data`) | the thread's TLV block: a `__thread_vars` descriptor's thunk gives one variable's address, minus its offset = block start; size = `__thread_data` + `__thread_bss` | `TEB.ThreadLocalStoragePointer[_tls_index]`; size from the image's TLS directory | none (`_Thread_local` becomes ordinary globals without the threads proposal) |
| GC heap chunks | `mmap` + trim to alignment | the same | `VirtualAlloc`: reserve twice the size, release, map at the aligned address | `aligned_alloc` (linear memory only grows) |
| thread stack bounds | `__libc_stack_end`, `pthread_getattr_np` | `pthread_get_stackaddr_np` | `GetCurrentThreadStackLimits` | the linear-memory stack, up to `__stack_high`; values in wasm locals are spilled there by the compiler (RootSpilling) |
| stop-the-world | `SIGPWR`/`SIGXCPU`, POSIX semaphore acks | `SIGUSR1`/`SIGXCPU`, Mach semaphore acks (no unnamed POSIX semaphores) | `SuspendThread` + `GetThreadContext` (waits until the thread is stopped; the whole `CONTEXT`, vector registers included, is scanned as roots), `ResumeThread` | not needed (one thread) |
| timed waits | `CLOCK_MONOTONIC` condvar, `pthread_mutex_timedlock` | `pthread_cond_timedwait_relative_np`, trylock polling | `pthread_cond_timedwait_relative_np` (winpthreads' absolute timeouts are wall-clock) | sleep until the deadline; a wait nothing can end is reported as a deadlock |
| unwinding | libgcc_s / `.eh_frame` | libSystem's libunwind, compact unwind + `__eh_frame`; same personality (`csrt_personality`) | LLVM-MinGW's libunwind on SEH tables (`.pdata`/`.xdata`): LLVM registers `csrt_personality` as each function's SEH handler, and on Windows it forwards to `_GCC_specific_handler` with the Itanium personality, as libstdc++'s `__gxx_personality_seh0` does | none: explicit propagation through `csrt_pending_exception` (ExceptionLowering) |
| unhandled exception | `abort()`: SIGABRT, exit status 134 | the same | `TerminateProcess` with .NET's exit code `0xE0434352`, or `0xC0000005` for a `NullReferenceException` from a null dereference (.NET re-raises the access violation) | `abort()` traps; wasmtime exits with status 134 |
| standard streams | as is | as is | binary mode: "\r\n" is written where .NET writes `Environment.NewLine`, a program's own "\n" stays bare | as is |
| command line | `/proc/self/cmdline` | `_NSGetArgv` | the UCRT's wide `argv` (`_configure_wide_argv`: MSVC `wmain` parsing of `GetCommandLineW`, like .NET's host) | `argv` from wasi-libc |
| clocks | `clock_gettime` | the same | `GetSystemTimePreciseAsFileTime`; `Stopwatch` on `QueryPerformanceCounter` (its `Frequency` is `QueryPerformanceFrequency`, as in .NET) | `clock_gettime` (WASI clocks) |
| P/Invoke probing | `foo.so`, `libfoo.so`, `foo`, `libfoo` | the same with `.dylib` | `foo.dll`, `foo` (`LoadLibraryExW`); `SetLastError` captures `GetLastError()` | libc/libm only (wasi-libc, linked statically); others throw `DllNotFoundException` |
| ICU (globalization) | `libicuuc.so.NN` / `libicui18n.so.NN`, newest version first (`dlopen`) | `/usr/lib/libicucore.dylib` | `icu.dll` (Windows 10 1903+); no NLS fallback | none: invariant globalization mode |
| user culture | ICU default locale: `LC_ALL`, `LC_MESSAGES`, `LANG`; `C`/`POSIX` = invariant | system settings (`CFLocale`) | `GetLocaleInfoEx` / `GetUserPreferredUILanguages` | the invariant culture |

An uncaught exception on Windows: `_Unwind_RaiseException` raises an SEH exception that no frame
handles, and MinGW's top-level filter (`_gnu_exception_handler`) resumes it, so it returns
`_URC_END_OF_STACK` as on Unix; csrt then runs the `finally` blocks (forced unwind) and reports it.
The thread-pool trap (`csrt_trap_call`) uses `__builtin_setjmp`/`__builtin_longjmp` there, because
MinGW's `longjmp` unwinds the stack through SEH (`RtlUnwindEx`) and it jumps out of a forced unwind.
`win32.c` supplies C library functions the UCRT lacks (`roundeven`, which LLVM calls for
`Math.Round` without SSE4.1).

errno values, `open` flags and struct layouts that C# code needs are selected in the corelib at
compile time from the target triple, not in C ([16-interop-io.md](16-interop-io.md#systemio)).

### WebAssembly (wasm32-wasip1)

The design is in [ADR 0011](adr/0011-wasm32.md). What a program sees:

- **Exceptions** behave as on the native targets, filters and `finally` included. The compiler
  lowers them to explicit propagation, since WebAssembly has no unwinder.
- **Garbage collection** is the same collector. References that the optimized code keeps in wasm
  locals are spilled into stack slots in linear memory, so the scan finds them. `CSRT_GC_STRESS=N`
  collects on every N-th allocation (any platform) to test this.
- **Threads**: there is one. `Thread.Start` throws `PlatformNotSupportedException` ("Operation is not
  supported on this platform."), as in .NET's single-threaded WebAssembly builds.
  `Environment.ProcessorCount` is 1 and parallel loops run inline. `Task.Run` work, continuations and
  timers run on the main thread whenever it blocks (`Wait`, `Result`, `async Main`, `Thread.Sleep`,
  `Thread.Yield`). A wait that can never end (`Monitor.Wait()`, `Thread.Sleep(Infinite)`, a task
  nothing will complete) prints a deadlock message and exits with status 134 instead of hanging.
  A busy-wait on a flag that only another thread would set still spins forever.
- **System**: files in the directories the WASI runtime preopens (relative paths resolve against
  `$PWD`), environment variables, clocks, `Random` (WASI `random_get`), standard streams.
  `Environment.ProcessId` is 42 and `MachineName` is "localhost". P/Invoke reaches only wasi-libc.
  Exit codes above 125 are rejected by wasmtime.
- **Running**: `wasmtime run --dir=. program.wasm` for a program that only touches files under the
  current directory, or `tests/wasm-run.sh program.wasm` (the whole file system, the environment,
  and an 8 MiB wasm stack like .NET's main thread).

Measurements (local x86-64 machine, wasmtime 36 including its JIT compilation; best of 3, wall ms):

| benchmark | native -O2 | wasm -O2 | native size (KiB, stripped, dynamic glibc) | wasm size (KiB, static wasi-libc) |
|---|---:|---:|---:|---:|
| fib | 92 | 132 | 11 | 35 |
| binarytrees | 336 | 437 | 28 | 49 |
| fannkuchredux | 394 | 487 | 28 | 51 |
| nbody | 904 | 1102 | 36 | 63 |

The module embeds what native executables load from libc at run time (wasi-libc: malloc, stdio,
math). Hello world is about 65 KiB at -O2.

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
