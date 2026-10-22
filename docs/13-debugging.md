# 12 — Debugging native C# (DWARF debug info)

`csharpc -g` emits DWARF debug info, so the native executables can be debugged at the C# source
level with gdb or lldb: breakpoints by `file.cs:line`, stepping through C# statements, C# names in
backtraces, and locals/parameters/fields in the debugger.

```sh
csharpc -g -O0 app.cs -o app          # best debugging experience
csharpc -g app.cs -o app              # -O2 with debug info (line tables survive inlining)
csharpc -c Debug MyApp.csproj         # project builds: Debug configuration implies -O0 -g
dotnet build -p:UseCsharpc=true       # (Debug is the default configuration)
csharpc -c Debug -g0 ...              # Debug without debug info
```

```text
(gdb) source tools/gdb/csharpc.py
(gdb) break app.cs:10
(gdb) run
Breakpoint 1, Program.<Main>$.Fib(int) (n=6) at app.cs:10
10	    if (n < 2) return n;
(gdb) bt
#0  Program.<Main>$.Fib(int) (n=6) at app.cs:10
#1  0x... in Program.<Main>$(string [] *) (args=string [], Length = 0) at app.cs:4
#2  0x... in main ()
(gdb) up
(gdb) info locals
shape = 0x7c4a61381010
total = 0
i = 1
label = "fib total"
(gdb) print *shape
$1 = {__mt = 0x... <_CN6Circle2MTE>, Radius = 2.5}
```

For lldb: `command script import tools/lldb/csharpc.py` (string summaries).

## What is emitted

Implementation: `src/Csharpc/CodeGen/DebugInfo.cs` (a wrapper over LLVM's `DIBuilder` C API,
bindings in `src/Csharpc/Llvm/NativeDebugInfo.cs`), plus small hooks in `FunctionCodeGen`.

| | |
|---|---|
| Compile unit | one per program; producer `csharpc`; language `DW_LANG_C_plus_plus` — DWARF has no code for C#, and C++ gives debuggers the right expression syntax for `a->b`, `*p`, member access. DWARF version 4 (gdb ignores the `.debug_names` index LLVM generates for DWARF 5). |
| Functions | every function lowered from C# gets a `DISubprogram` named like the C# member: `Program.Fib`, `Circle.Area`, `Program.<Main>$.Square` for local functions, `<lambda>` for lambdas. No `DW_AT_linkage_name`: debuggers would try to demangle our `_CN…` symbols (they aren't Itanium) into garbage. The native `main` shim gets an artificial subprogram so code inlined into it (at -O2, usually `Main` itself) keeps its line info. Other synthesized code (method tables, thunks, static-init) has no debug info. |
| Line table | as each Roslyn `IOperation` is lowered (`FunctionCodeGen.WithLocation`), the builder's current debug location is set to the operation's syntax start (line + column). Every instruction in a described function therefore has a location — the LLVM verifier requires that for inlinable calls. At -O2 LLVM keeps `inlinedAt` chains, so `bt` shows inlined C# frames. |
| Variables | locals and parameters: `dbg.declare` on their entry-block allocas (mem2reg/SROA turn them into `dbg.value`s at -O2). `this`: `dbg.value` on the incoming argument (artificial, object pointer). Compiler temporaries (unnamed or `<…>` names) are skipped. |
| Types | primitives as DWARF base types (`char` is UTF-16, `bool` 8 bits); `string` → pointer to `System.String { __mt, Length, chars[] }`; `T[]` → pointer to `T[] { __mt, Length, data[] }`; classes → pointer to a struct with `__mt` and all instance fields at their real offsets (from `ClassLayout`; self-referential classes go through a replaceable placeholder); structs → by-value struct; enums → their underlying type; other reference types → pointer to `System.Object { __mt }`. Auto-property backing fields are shown under the property's name. Not described yet: `Nullable<T>`, tuples, spans, decimal (such variables are omitted). |

`-g` changes **only** metadata: the `.text` of every `bench/programs` binary is byte-identical with
and without `-g` at -O2 (checked with `objcopy --only-section=.text` + `cmp`). Binary size grows by
0–4 KiB for those programs.

### Coroutines

LLVM 18's coroutine frame builder (`CoroSplit` → `buildFrameDebugInfo`) dereferences the coroutine's
promise alloca when the function has debug info, so with `-g` async methods and iterators pass a
1-byte dummy promise to `llvm.coro.id` (clang always has a real promise). This only changes the
frame layout of `-g` builds.

## Testing

- `tests/debuginfo.sh` — compiles `tests/debuginfo/app.cs` with `-g` at -O0 and -O2 and drives gdb in
  batch mode: breakpoint by line, arguments, locals, string pretty-printing, fields through `this`,
  C# names in backtraces, inlined frames at -O2.
- `CSHARPC_FLAGS=-g tests/run.sh --all-opts` — the whole differential suite with debug info (every
  sample must still pass the LLVM verifier and produce identical output).

## Limitations / future work

- No lexical-block scopes (all locals are function-scoped in the debugger).
- No `DW_TAG_enumeration_type` (enum values print as numbers), no DWARF for `Nullable<T>`, tuples,
  spans, decimal, delegates' targets, closure environments (captured variables aren't visible from
  lambdas), or static fields as global variables.
- Field initializers from another file of a partial class report lines in the constructor's file.
- Expression evaluation uses C++ syntax (`print shape->Radius`, `print *this`); calling C# methods
  from the debugger works only for non-inlined functions and needs the mangled name.
