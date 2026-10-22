# ADR 0002 — Bind the LLVM C API with `[LibraryImport]`

- **Status:** Accepted (2026-10-22)

## Context

csharpc must build LLVM IR, verify it, run optimization pipelines and emit objects. The options are
the C++ API (not callable from C#), the C API (`llvm-c/*.h`, a stable C ABI), textual IR piped to
`opt`/`llc`, or an existing binding (LLVMSharp).

## Decision

Hand-written P/Invoke over the **LLVM C API** using the `[LibraryImport]` source generator
(`src/Csharpc/Llvm/Native.cs`):

- Handles are blittable `readonly record struct`s (`LLVMValueRef(nint)`), so `LLVMValueRef[]`
  arguments marshal by pinning, with no copies.
- Strings use `StringMarshalling.Utf8`. LLVM-owned returned strings are taken with `TakeMessage`
  (copy, then `LLVMDisposeMessage`).
- A `DllImportResolver` probes `$CSHARPC_LIBLLVM`, then `libLLVM-18`, 19 and 20 sonames.
- Only the functions we use are bound. Thin wrappers (`Llvm.cs`, `IrBuilder.cs`) plus C# 14
  extension members give it an object-model feel without hiding LLVM's concepts.
- Optimization uses the new pass manager through `LLVMRunPasses("default<On>")`.

## Consequences

- No native shim library and no C++ build. Any LLVM ≥ 17 shared library works (we use opaque
  pointers, `LLVMBuildCall2`/`LoadGEP2`, `LLVMArrayType2`/`ConstArray2`, `LLVMRunPasses`).
- In-memory IR building is fast, with no textual IR round-trip.
- The C API lags the C++ API in places (some debug-info, GC and statepoint features). We can add a
  small C++ shim later if needed, as rustc does with `RustWrapper.cpp`.
- **Release libLLVM has no assertions.** Misuse crashes instead of asserting (see the spike log).
  The verifier catches IR-level errors, but builder misuse needs gdb.
- Enum values (`LLVMOpcode`, predicates, type kinds) are copied by hand and must match the headers
  exactly. One early bug came from a missing enum member.

## Alternatives considered

- **LLVMSharp / LLVMSharp.Interop:** a complete generated binding, but tied to specific
  `libLLVM` NuGet runtime packages and versions. The hand-written surface is small, dependency-free
  and works with the system LLVM. We could switch later.
- **Emit textual `.ll` and shell out to `opt`/`llc`:** easy to debug, but it means string
  formatting, escaping, extra processes and no in-process verification. `--emit llvm-ir` keeps the
  debuggability anyway.
- **`DllImport` instead of `LibraryImport`:** runtime-generated marshalling stubs. `LibraryImport`
  generates them at compile time, which is trimming- and AOT-friendly (useful if csharpc itself is
  NativeAOT-compiled).
