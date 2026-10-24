# ADR 0012 — Stack traces: per-call-site landing pads, names and lines from Roslyn's own PDB

- **Status:** Accepted (2026-10-23), implemented.

## Context

`Exception.StackTrace` and `Exception.ToString()` print the frames an exception passed through:

```text
System.InvalidOperationException: Not sufficient funds for this withdrawal
   at Classes.BankAccount.MakeWithdrawal(Decimal amount, DateTime date, String note) in /src/BankAccount.cs:line 61
   at Program.<Main>$(String[] args) in /src/Program.cs:line 27
```

Real programs print them: every remaining wrong-output difference in the first real-world
compatibility runs ([18-compatibility.md](../18-compatibility.md)) was a missing stack trace. Until
now csharpc printed the type, the message and the inner exceptions only, and warned (`CSLLVM0006`).

What .NET prints, measured against .NET 11 RC2 (the oracle):

- **The frame list** runs from the throw point to the catching frame. `throw;` keeps the frames
  (the rethrowing frame is not listed again), `throw e;` starts over even for an exception thrown
  before, `ExceptionDispatchInfo.Throw()` appends `--- End of stack trace from previous location ---`
  and continues. With .NET 11's runtime-async, awaits add no marker: an exception that went through
  `Inner` ← `Middle` ← `Outer` lists those frames in order.
- **Each frame** is `Namespace.Type.Method[T](Int32 x, String& s)`: nested types joined with `.`,
  generic types as ``List`1``, generic methods with their parameter names, parameter types as
  `System.Type.Name` (`Int32`, `Decimal`, ``Nullable`1``, `Int32[,]`, `Int32&` for ref/out/in, `Int32*`).
  Constructors are `..ctor`, accessors `get_X`, operators `op_Addition`, explicit implementations
  `System.IDisposable.Dispose`, top-level statements `Program.<Main>$(String[] args)`, iterators
  `Program.Numbers(Int32 count)+MoveNext()`, async methods their own name.
- **Compiler-generated methods** carry Roslyn's metadata names: `Program.<>c.<Main>b__7_0()` for a
  lambda without captures, `Program.<>c__DisplayClass7_0.<Main>b__8()` in a closure class,
  `Program.<Main>g__Local|7_9()` for a local function, `<<Main>$>g__L2|0_27(<>c__DisplayClass0_0&)`
  for one capturing through a struct. The ordinals come from Roslyn's closure conversion (lambdas of
  a scope first, then those of nested scopes, closure classes per scope); they are easy to get subtly
  wrong by imitation.
- **The location** ` in <path>:line N` comes from the PDB: the document path as the build saw it
  (`#line` paths resolved against the file, PathMap applied) and the start line of the sequence
  point that contains the IL offset — the statement start, but per declarator, per `for` clause, per
  switch-expression arm, the closing brace for a `using` block's implicit Dispose, and so on.
  Library frames (the BCL ships without PDBs) have no location.
- **Quirks of CoreCLR's funclets:** an exception that leaves a `finally` block (or a using's
  Dispose) run on the normal path lists the method twice — once for the finally funclet, once for
  the method's own frame, at its first sequence point in Debug code and at the try block's first
  statement in optimized code. During unwinding (the finally runs because of another exception) it
  appears once. Catch blocks never duplicate.
- **Inlining.** `dotnet run` builds Debug, and the JIT's first tier never inlines, so every user
  method has a frame. csharpc compiles every configuration at -O2, where LLVM inlines aggressively;
  an inlined frame that vanished from the trace would be a silent difference.

The trace must be exact for user code on all five CI targets — Linux x86-64/arm64, macOS arm64,
Windows x64 (SEH unwinding through libunwind) and wasm32-wasi, which has no unwinder at all
([ADR 0011](0011-wasm32.md)) — and must cost nothing in programs that never look at a trace
(the roadmap's standing goal: faster and smaller than .NET in every area).

## Decision

### Recording: a landing pad per call site, read by the personality's search phase

In a program that records stack traces, every call that may throw in a function of the program's
own code becomes an `invoke` with a landing pad of its own (`FunctionCodeGen.StackTraces.cs`), whose
first catch clause is the site's descriptor — a constant `{ magic, method, file, line }` the
compiler emits per site:

```llvm
site.12:  %lp = landingpad { ptr, i32 } catch ptr @csharpc.trace.site.12 <the enclosing try's clauses>
          br label %continue                     ; resume / csrt_begin_catch + dispatch / coroutine fault
```

`csrt_personality` tells a descriptor from a method table by its magic number (not a canonical
address, so no method table starts with it). During the **search phase** it appends every descriptor
it meets to the exception (a new `trace` field) and goes on looking for a handler. The search phase
walks exactly the frames from the throw point to the catching frame — .NET's frame list, by
construction — and frames without descriptors (the C runtime, the corelib) are skipped, as .NET
skips frames of code it doesn't list. Record-only pads have no cleanup, so the cleanup phase passes
through those frames without stopping: recording costs a few instructions per frame in a walk the
unwinder does anyway.

- **Inlining keeps frames.** When LLVM inlines a function, the inlined landing pads get the caller's
  clauses appended, so an inlined frame's descriptor is followed by its caller's: recorded under
  its own name and line, in the right order. The invokes themselves are all that restricts the
  optimizer (tail-call and accumulator-recursion elimination don't apply to invokes, which is also
  what keeps recursive frames). LLVM deletes landing pads that only `resume` — catch clauses and
  all — so record-only pads contain a volatile store that never executes.
- **No frame twice.** csharpc's own propagation re-raises (finally blocks, handlers whose type or
  filter doesn't match, `throw;`), and a new search would walk frames already recorded. The trace
  keeps the highest frame (CFA) recorded, and the search records only frames above it. An await, an
  iterator's MoveNext and `ExceptionDispatchInfo.Throw()` continue elsewhere on the stack and reset
  it (`csrt_trace_new_stack`).
- **Rethrow semantics:** `throw e` (`csrt_throw_object`) clears the frames; csharpc's internal
  propagation and awaits (`csrt_rethrow`) keep them; `throw;` gets no descriptor (its frame is in the
  trace already); `ExceptionDispatchInfo.Capture` copies the frames and `Throw()` restores them plus
  the marker.
- **Finally funclets.** A call that can make an exception leave a finally block gets a cleanup pad
  that, if a per-finally flag says the block was entered on the normal path, inserts the parent
  frame (method start in Debug, try's first statement in Release) after its own entry
  (`csrt_trace_finally`); the flag is set in the normal-path trampolines (`ThroughFinally`) and
  cleared on the exceptional entry (`Dispatch`).
- **wasm32** has no unwinder and no personality: `ExceptionLowering` turns every invoke into a call
  plus a branch to its landing-pad block on a pending exception, so there the pad code itself records
  (`csrt_trace_frame`). Same IR, same frames.
- **Windows** reaches the same personality through libunwind's SEH adapter.

Formatting happens only when `StackTrace` or `ToString()` is called (`csrt_exception_get_stack_trace`
in `eh.c`): `"   at " + method + " in " + file + ":line " + N`, joined with `Environment.NewLine`.

### Names and lines: from the program as .NET builds it

`CodeGen/StackTraces.cs` emits the user's sources once with Roslyn — without csharpc's corelib or
its rewrites, with the configuration's optimization setting, the SDK's source resolver and PathMap —
and reads the portable PDB back with System.Reflection.Metadata:

- **Lines.** For each call site, the smallest sequence point containing the operation being lowered
  (mapped back through csharpc's method-group rewrite) gives the document and line. This is the
  rule the CLR applies to IL offsets, so multi-line statements, `for` headers, switch arms and
  `#line` come out right without re-deriving Roslyn's sequence-point rules. The implicit Dispose of
  `using` is placed at its scope's closing brace, where Roslyn puts that sequence point.
- **Names of compiler-generated methods** are the metadata names of the PE's methods: a lambda or
  local function is the generated method whose first sequence point is the outermost one inside its
  syntax (a state machine's MoveNext maps to its kickoff method). Declaring types, generic
  parameters and parameter types are decoded from metadata into `Type.Name` form.
- **Ordinary methods** are formatted from their Roslyn symbols with the same rules.

Each site's descriptor indexes the method and file string tables the compiler emits; `main`
registers them (`csrt_trace_init`) before user code runs.

### Pay for play

A program records traces only when it can observe one (`StackTraceInfo.FindObservation`): it reads
`Exception.StackTrace`, or turns an exception into text — `ToString()`, interpolation, string
concatenation, or passing it as `object` (`Console.WriteLine(e)`). Everything else compiles exactly
as before: no pads, no table, no PDB emit. `--stack-traces` turns recording on regardless (for
unhandled-exception reports on stderr).

### What doesn't match, and says so

- **Frames inside .NET's library** (`System.ThrowHelper...`, `System.Linq.Enumerable...`) depend on
  how .NET implements each member, and csharpc's corelib implements them differently. For common
  throwing members the corelib reproduces .NET 11's frames: the throw site names them,
  `throw CoreIntrinsics.LibraryFrames(new KeyNotFoundException(...), "System.Collections.Generic.Dictionary`2.get_Item(TKey key)")`,
  which presets them as the start of the trace (`csrt_trace_preset`; nothing at all in programs that
  don't record traces), and a `when` filter adds them to an exception already in flight
  (`AppendLibraryFrames`, for File's text readers, whose frames are per public member and per OS).
  Covered today: `Dictionary`/`SortedDictionary` indexers, `List<T>` indexer and `RemoveAt`, `First`
  and `Last`, `Stack.Pop`, `Queue.Dequeue`, `Array.Copy`, and on Unix `File.OpenText`/`ReadAllText`/
  `ReadAllLines` (with `FileNotFoundException`'s `File name: '...'` line). Elsewhere an exception thrown
  by or passing through BCL code shows the program's frames only. Warning `CSLLVM0006` is reported
  wherever a program turns an exception into text or reads `StackTrace`, so the difference is never
  silent.
- Static field initializers and static constructors run from csharpc's own class-init function:
  their `..cctor` frame is missing (.NET also shows a BCL frame there).
- Frames of methods compiled from IL (ADR 0010) and of record members the compiler synthesizes are
  not recorded.
- If the program cannot be emitted by Roslyn as-is, lines fall back to statement starts and lambda
  frames are named `<lambda>()`, with a `CSLLVM0006` warning.
- Optimized .NET code (`-c Release`) can lose frames when the JIT's later tiers inline hot methods;
  csharpc lists every frame, which is what Tier-0 code (and every short program) shows.

## Consequences

Measured on x86-64 Linux (shared 4-vCPU VPS), -O2, benchmark programs compiled with and without
`--stack-traces` (none of them observes a trace, so by default they compile exactly as before):

| | without | with `--stack-traces` |
|---|---:|---:|
| binary size, programs that already throw (throw microbenchmark, `listsort`, `strbuild`) | 41.2 / 85.6 / 53.3 KiB | 45.4 / 89.7 / 57.6 KiB (+4 KiB) |
| binary size, programs without exceptions (`binarytrees`, `nbody`, `fib`) | 28.1 / 36.5 / 11.1 KiB | 36.6 / 45.1 / 36.1 KiB (the unwinder and formatter come in) |
| CPU time, 11 benchmarks | — | unchanged (±3 %, noise) except `fib`: 42 → 83 ms |
| throw + catch through 10 frames | 1.3 µs (1 frame: LLVM turned the recursion into a loop) | 4.7–5.3 µs (.NET 11 JIT: 4.9 µs) |
| throw + catch through 1 frame | 1.4 µs | 2.4 µs (.NET 11 JIT: 2.4 µs) |
| compile time | — | +0.5–0.9 s for the Roslyn emit of the user's sources |

- `fib` is the cost of keeping frames: its recursive calls stay calls (an invoke is never turned into
  a loop by accumulator-recursion elimination), as in .NET, whose JIT runs the same program in
  117 ms. Only programs that observe stack traces pay it.
- Programs that observe stack traces print the same text as .NET for their own frames. The five
  compatibility programs whose only difference was the missing trace pass; the two that remain print
  BCL-internal frames (`ArgumentOutOfRangeException.ThrowNegative[T]`, `Array.CopyImpl`,
  `SafeFileHandle.Open`, ...).
- `dotnet run app.cs` (the oracle for the differential tests) sometimes runs a cached build whose PDB
  is stale; .NET then prints frames without lines. Recording a stack-trace sample therefore needs a
  fresh build (delete the app's `~/.local/share/dotnet/runfile/<name>-*` directory first).

## Alternatives considered

- **Return-address backtraces + symbolization** (what C++ and NativeAOT do): capture
  `_Unwind_Backtrace` at the throw, map return addresses to methods and lines at format time. Zero
  code-size cost in user functions, but: line tables need the final addresses, so DWARF
  (`.debug_line`/`.debug_info` with inlined-subroutine records) must be parsed at run time, or a
  table rebuilt after linking and patched into the binary — on ELF, Mach-O (whose DWARF stays in the
  object files unless `dsymutil` runs) and PE alike; inlined frames need DWARF's inline records;
  wasm32 has no unwinder to capture anything. Several hundred lines of per-format code for each
  platform, versus nothing platform-specific in the pad approach.
- **Disabling inlining in programs that observe traces**: simple, but slows exactly the programs
  csharpc competes on and still needs the line tables above.
- **A shadow stack** (push method/line on entry and before each call): works everywhere, but costs
  every call on the normal path.
- **Re-deriving Roslyn's lambda ordinals and sequence points from syntax**: no extra emit, but the
  closure-conversion ordering (scope tree order, which lowered temporaries open new scopes) is
  exactly the kind of imitation that is silently wrong in corner cases. Reading Roslyn's own output
  is exact by construction.
