# ADR 0005 — Exceptions

- **Status:** Accepted. Phase 1 (abort) is implemented; phase 2 is planned.

## Context

C# exceptions involve `throw`, `try`/`catch` (by type, with `when` filters), `finally`, rethrow,
exceptions crossing any number of frames, and implicit exceptions (`NullReferenceException`,
`IndexOutOfRangeException`, `DivideByZeroException`, `OverflowException`, `InvalidCastException`).
C# filters run **before** unwinding (two-phase semantics), which matches the Itanium C++ ABI's
search phase followed by its cleanup phase.

LLVM supports zero-cost exceptions with `invoke` / `landingpad` / `resume` and a language-specific
**personality routine** that interprets the LSDA (call-site tables in `.gcc_except_table`), driven by
the platform unwinder (`libgcc_s`/`libunwind`, `_Unwind_RaiseException`). Windows has a separate
funclet-based model (`catchswitch`/`catchpad`/`cleanuppad`), and wasm has its own EH proposal.

## Decision

1. **Spike (now):** every throw is an unhandled exception. `csrt_throw*` prints .NET's message and
   calls `abort()` (exit status 134, as .NET on Linux). All functions are `nounwind`.
   `try`/`catch`/`finally`, user exception types and rethrow are rejected with `CSLLVM0001`. Implicit
   exceptions are explicit checks that branch to `noreturn cold` throw helpers.
2. **try/finally without unwinding (M1, first step):** lower finally regions in the CFG by routing
   each exit of a try region through the finally block with a selector variable. This is enough
   for `foreach`, `using` and `lock` while throw still aborts.
3. **Real exceptions (M1):** Itanium-style zero-cost EH.
   - Exception objects are ordinary csharpc objects (`System.Exception` and subclasses compiled
     from C#), wrapped in an `_Unwind_Exception` header allocated by csrt.
   - `throw` → `csrt_throw(obj)` → `_Unwind_RaiseException`.
   - Calls inside protected regions become `invoke`. Landing pads use a `csrt_personality` routine
     (C, in csrt) that matches catch clauses by comparing method tables (`csrt_isinst_class`) and
     calls filter funclets during the search phase.
   - `finally` = cleanup landing pad plus the normal-path routing from step 2. Rethrow =
     `_Unwind_Resume` / re-raise of the current exception.
   - The `nounwind` attribute is dropped from functions that can throw. The explicit-check helpers
     raise real exceptions.
   - Unhandled exceptions keep the current `Unhandled exception. …` + `abort()` behavior.

## Consequences

- Zero cost on the non-throwing path. Throwing is expensive, which matches .NET's model.
- Personality and LSDA handling is subtle, but it's C++'s well-trodden path, and clang's output is a
  reference to diff against.
- Windows needs the funclet model later (`__CxxFrameHandler3`-like or a custom personality);
  wasm32 needs wasm EH or setjmp/longjmp. These are per-target backends behind the same front-end
  lowering of CFG regions.

## Alternatives considered

- **setjmp/longjmp** (portable, simple): a cost on every try entry, problems with LLVM optimizations
  around `returns_twice`, and no two-phase search for filters without extra machinery. Kept as the
  fallback for targets without unwinding (wasm32 without EH, some embedded).
- **Error-code returns** (every call checks a flag, Go/Rust `Result` style): simple and portable, but
  a branch after every call, and every function signature changes. Rejected for the main targets.
- **Reuse the C++ personality (`__gxx_personality_v0`) with C++ type_info:** possible (IL2CPP uses C++
  exceptions), but it ties us to libstdc++/libc++ and C++ RTTI layouts, which don't map onto CLR
  type tests or filters. A small custom personality is cleaner.
