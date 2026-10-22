# ADR 0006 — Closed-world, whole-program compilation

- **Status:** Accepted (2026-10-22)

## Context

CoreCLR is an *open* world: assemblies load at run time, any virtual method may be overridden by
code that doesn't exist yet, and reflection can reach anything. NativeAOT compiles a *closed* world:
all code is known at build time, which enables whole-program analysis, devirtualization and
trimming, at the cost of `Assembly.Load`, `Reflection.Emit` and unrestricted reflection.

## Decision

csharpc compiles the whole program, user code plus (later) the C# corelib, into **one LLVM module**:

- Every function, method table, string literal and static field is `internal`/`private`. Only
  `main` (and, later, explicitly exported FFI entry points) is external
  (`ModuleCodeGen.GetFunction`, `EmitMethodTable`).
- Method tables and vtables are constants.
- There's no runtime code loading, no JIT and no `Reflection.Emit`.

## Consequences

- LLVM may change calling conventions (`fastcc`), inline anything, specialize, and delete unused
  functions and data. That gives NativeAOT-style trimming as a side effect of `globaldce`.
- Constant method tables let LLVM devirtualize whenever it can see an object's allocation site. The
  "sealed type / sealed method → direct call" rule is applied by csharpc itself.
- Compile time grows with program size because the module is a single unit. Future mitigations:
  per-type codegen units plus ThinLTO, as rustc does with codegen units.
- Separate compilation of libraries, or C# plugins loaded at run time, would need an "open" mode
  with external linkage and a stable ABI. Not planned before M3.
- Reflection, if ever supported, must be metadata generated at compile time for what's reachable
  (NativeAOT's model).

## Alternatives considered

- **Per-file or per-type objects with external linkage** (classic C model): enables incremental
  builds but blocks cross-module optimization unless LTO is used. Revisit for build speed (M3),
  with LTO.
