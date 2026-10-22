# ADR 0007 — Bootstrap runtime in C, corelib in C# later

- **Status:** Accepted (2026-10-22)

## Context

Compiled programs need allocation, strings, console output, number formatting, type tests and
exception reporting. Ultimately most of this should be C#: that's the language we compile, and
it's how NativeAOT's `System.Private.CoreLib` and Rust's `core`/`std` work. But compiling a C#
corelib requires generics, structs, interfaces and exceptions, which the spike doesn't have yet.

## Decision

- **csrt** (`src/runtime/csrt.c`, `csrt.h`) is a small C11 runtime with a documented ABI
  ([04-runtime.md](../04-runtime.md)). The driver compiles it with clang on demand and caches it.
- **Library members are intrinsics** (`src/Csharpc/CodeGen/Intrinsics.cs`): Roslyn resolves calls
  against real .NET reference assemblies, and csharpc lowers each supported member to LLVM
  intrinsics, libm or csrt.
- As compiler features land (M1), library functionality moves into a **C# corelib** compiled by
  csharpc together with the user program (M2). csrt shrinks to what must be native: GC, allocator,
  unwinding personality, OS calls.

## Consequences

- The spike runs real programs immediately, with exact .NET formatting semantics where we
  implemented them (shortest round-trip doubles, invariant culture).
- Logic is duplicated: formatting lives in C now and will be rewritten in C#. Acceptable, since the
  differential tests pin the behavior either way.
- C runtime functions are opaque to LLVM's optimizer, so it can't inline them. A C# corelib in the
  same module would be inlinable, which is one more reason to migrate.
- Shipping csrt as source (rather than prebuilt binaries) makes cross-compiling to a new target a
  matter of `clang --target=...`.

## Alternatives considered

- **Write the runtime in C# from day one** (compiled by csharpc): blocked on missing language
  features; would have delayed the first working program by weeks.
- **Use .NET's own NativeAOT runtime / CoreLib:** its ABI is tied to RyuJIT's GC info, EH model and
  object layout conventions, which is exactly what sank LLILC ([01-prior-art.md](../01-prior-art.md)).
- **Write the runtime in Rust or C++:** no advantage over C for a few hundred lines, and an extra
  toolchain.
