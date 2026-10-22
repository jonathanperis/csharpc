# ADR 0003 — Roslyn's ControlFlowGraph is the mid-level IR

- **Status:** Accepted (2026-10-22)

## Context

LLVM front-ends usually lower through a CFG-shaped IR before LLVM: rustc's MIR, Swift's SIL, the
clang CIR effort. Lowering a typed tree directly to LLVM is possible (classic clang), but structured
control flow, short-circuit evaluation and evaluation-order details then all live in the code
generator.

Roslyn provides `ControlFlowGraph.Create(...)` (namespace `Microsoft.CodeAnalysis.FlowAnalysis`), a
public, documented lowering of a method body to basic blocks with conditional and fall-through
successors, branch semantics (`Regular`, `Return`, `Throw`, `Rethrow`,
`StructuredExceptionHandling`, …), regions (locals' lifetimes, try/catch/finally/filter) and
**flow captures** for intermediate values that cross blocks. It was built for analyzers, so it's
semantically faithful, including C#'s left-to-right evaluation order.

## Decision

csharpc lowers from Roslyn's `ControlFlowGraph`, not from the `IOperation` tree. Each reachable CFG
block becomes an LLVM block. Flow captures become allocas, and lvalue captures (targets of
assignments) hold addresses. `IOperation` remains the expression language inside blocks. Field and
property initializers get their own CFGs and are chained into constructors as prologues.
Local-function CFGs come from `GetLocalFunctionControlFlowGraph`.

## Consequences

- Loops, `&&`/`||`, `?:`, `??`, `?.`, `switch` (statement and expression), patterns in branch
  positions, `goto`, object initializers and evaluation-order spills are handled by Roslyn.
  csharpc's control-flow code is about 150 lines.
- `--dump-cfg` gives a readable "MIR dump" for free.
- **Subtleties we must respect:** captures can denote *locations*. The Collatz bug
  ([02-architecture.md](../02-architecture.md#flow-captures-and-lvalue-captures)) came from treating
  every capture as a value.
- **The CFG isn't optimized for codegen.** `foreach` over arrays uses the enumerator pattern inside
  a try/finally region instead of an index loop. csharpc has to recognize such patterns or make the
  generic path cheap: stack-allocated enumerators that SROA dissolves.
- **Not lowered by the CFG:** lambdas and closures (`IFlowAnonymousFunctionOperation`), `await`,
  `yield`, interpolated-string handlers, tuples/deconstruction. csharpc owns those transforms.
- Exception regions are explicit in the CFG. That's good for implementing try/finally and
  try/catch, but csharpc must lower region exits itself (finally routing).

## Alternatives considered

- **Lower `IOperation` trees directly** (clang-style structured codegen). This duplicates what the
  CFG gives us, and evaluation-order bugs become ours. Rejected.
- **Define our own MIR after the CFG** (rustc-style), with explicit temporaries, drop/cleanup edges
  and borrow-like analyses. Possibly later, if we need our own optimizations (escape analysis for
  stack allocation, bounds-check elimination) before LLVM. Not needed yet; LLVM does most of it.
