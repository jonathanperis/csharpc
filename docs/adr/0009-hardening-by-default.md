# ADR 0009 — Exploit mitigations on by default, CET opt-in

**Status:** accepted

## Context

csharpc aims to beat .NET on security as well as speed. Executables produced by NativeAOT and the
.NET apphost have full RELRO, NX and stack canaries but no FORTIFY or CET. csharpc controls the whole
link, so it can choose its own defaults — but every mitigation has a cost, and the project's other
goal is to be faster than .NET.

Measured on the benchmark suite (CPU time, interleaved best-of-N runs on a shared machine):

- RELRO/BIND_NOW, NX, separate code, stack-clash probes and the hardened C runtime
  (stack protector, `_FORTIFY_SOURCE=3`, zero-initialized locals): within noise. Stack canaries end up
  only in cold runtime functions (GC collection, large allocations, error paths) even with runtime LTO.
- Intel CET (`-fcf-protection=full` for the runtime, `cf-protection-branch/return` module flags for
  generated code): **+8 % to +16 %** on call-heavy programs (shapes, sieve). IBT alone still cost
  +8–12 %. Linux 6.8 does not enforce user-space IBT; only the shadow stack is supported upstream.
- Speculative load hardening and call-used-register zeroing: +25 % to +200 %.

## Decision

- `--hardening=default` (the default): full RELRO + BIND_NOW, non-executable stack, separate code,
  stack-clash probes on generated code, and the C runtime built with stack protector, FORTIFY 3,
  stack-clash protection and zero-initialized locals.
- `--hardening=max`: adds CET (IBT + SHSTK), stack protector and register zeroing on generated code,
  and Spectre v1 speculative load hardening.
- `--hardening=none`: platform defaults only (for comparisons and debugging).

## Consequences

- Default binaries are hardened beyond NativeAOT's (FORTIFY on top of the same RELRO/NX/canary
  coverage) at no measurable cost.
- CET has to be requested explicitly. Revisit when the Linux kernel enforces user-space IBT and the
  overhead of shadow stacks on csharpc's call patterns is better understood.
- The memory-safety guarantees of generated code (bounds and null checks, overflow-proof allocation
  sizes) are independent of the hardening level.

## Alternatives considered

- **CET by default:** rejected for now — a 10–16 % slowdown on common code patterns for protection the
  kernel does not yet enforce (IBT).
- **Stack protector on all generated code:** generated safe C# has no unchecked buffers, so canaries
  would add cost without adding protection; kept in `max` for `unsafe`/`stackalloc`-heavy code.
