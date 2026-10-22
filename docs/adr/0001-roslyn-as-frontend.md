# ADR 0001 — Roslyn is the C# front-end

- **Status:** Accepted (2026-10-22)

## Context

A C# compiler needs a lexer, parser, binder (name lookup, overload resolution, type inference,
generic constraints, nullable analysis, definite assignment), constant folding and a large catalog
of diagnostics, all tracking an evolving language (C# 14/15 preview). Roslyn implements all of this
and is the reference implementation. It also exposes a stable public semantic API: symbols,
`IOperation` trees, and lowered `ControlFlowGraph`s.

gollvm faced the same situation for Go and chose to reuse gofrontend through an abstract backend
interface instead of writing a new front-end.

## Decision

csharpc uses Roslyn (`Microsoft.CodeAnalysis.CSharp`, currently 5.9.0) for parsing and semantic
analysis and consumes only its **public** API: `CSharpCompilation`, symbols, `IOperation`,
`ControlFlowGraph`. Roslyn diagnostics are reported verbatim. csharpc owns everything after the
CFG.

User code is bound against the real .NET reference assemblies of the host runtime
(`src/Csharpc/Frontend/RoslynFrontend.cs`), so API shapes and overload resolution are exactly what
`csc` does. Implementations for library members come from csharpc ([04-runtime.md](../04-runtime.md)).

## Consequences

- C# language coverage on the *front-end* side is complete from day one. Every compile error is
  identical to `csc`'s.
- Language features that Roslyn lowers in its public CFG (loops, short-circuit operators, `switch`,
  patterns, `?.`, `??`, initializers) cost csharpc nothing.
- Features Roslyn lowers only *internally* (closures/display classes, iterators, async, `foreach`
  over arrays as index loops, string-interpolation handlers, lambdas) must be lowered by csharpc
  ([ADR 0003](0003-roslyn-cfg-as-mir.md)).
- Startup cost: Roslyn's JIT warm-up dominates small compiles (~1.4 s parse+bind). NativeAOT-compiling
  csharpc itself, or a compile server, would fix it.
- csharpc depends on .NET to *run* (until M4 bootstrapping), but not to run the programs it builds.

## Alternatives considered

- **Write our own C# front-end.** Many years of work just to reach parity, and a permanent
  compatibility gap. Rejected.
- **Use Roslyn's internal bound tree / lowering** (via `InternalsVisibleTo` hacks or a fork). That
  would get closure, async and iterator lowering for free, but couples us to unstable internals and
  a fork. Rejected for now; may be revisited for async.
- **Consume IL emitted by Roslyn** (the LLILC/NativeAOT route). Discussed in
  [01-prior-art.md](../01-prior-art.md#why-source-to-llvm-rather-than-il-to-llvm). Rejected as the
  primary path.
