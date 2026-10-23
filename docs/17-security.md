# 17 — Security

csharpc's goal is to produce executables that are at least as safe as .NET's — memory-safe by
construction for safe C#, hardened against exploitation by default, and verifiable — without giving up
speed. This page summarizes what csharpc does, how it compares, and how it is tested.

## Memory safety of generated code

Safe C# compiled by csharpc is memory-safe, like on .NET:

- **Bounds checks** on every array, string, `Span<T>`, multi-dimensional array and `Index`/`Range`
  access (one unsigned compare against the length), with `IndexOutOfRangeException` /
  `ArgumentOutOfRangeException` and .NET's messages.
- **Null checks** are explicit (LLVM treats a null dereference as undefined behaviour, so csharpc
  never relies on a hardware fault) and throw `NullReferenceException`.
- **Overflow-proof allocation sizes**: string lengths (concat, `Join`, `Replace`, `PadLeft`...),
  array sizes and multi-dimensional totals are computed in 64 bits and checked against the CLR limits
  — `OverflowException` above `int.MaxValue` elements, `OutOfMemoryException` above
  `Array.MaxLength` or when allocation fails — so no size computation can wrap into an undersized
  heap block. Allocation failure is a catchable `OutOfMemoryException`, not a crash.
- Integer division by zero and `MinValue / -1` are checked before LLVM's `sdiv` (which would be
  undefined behaviour); float→int conversions saturate as in .NET 9+.

Tests: `tests/samples/170_*`–`174_*` (bounds, null, allocation overflow, spans, malformed UTF-8 input)
are recorded from real .NET like every other test.

## Exploit mitigations (`--hardening`)

| level | what you get |
|---|---|
| `none` | platform defaults (PIE, NX stack) |
| **`default`** | full RELRO + BIND_NOW, non-executable stack, separate code segment, stack-clash probes on generated code; the C runtime built with `-fstack-protector-strong`, `_FORTIFY_SOURCE=3`, `-fstack-clash-protection` and zero-initialized locals |
| `max` | default + control-flow protection (x86-64: Intel CET IBT landing pads and shadow-stack marking; AArch64: BTI landing pads and PAC return-address signing), stack protector and call-used-register zeroing on generated code, Spectre v1 speculative load hardening |

`tools/checksec.sh` on the same hello world:

| binary | PIE | RELRO | NX | stack canary | FORTIFY | CET |
|---|---|---|---|---|---|---|
| csharpc `--hardening none` | yes | partial | yes | no | no | no |
| **csharpc (default)** | yes | **full** | yes | **yes** | **yes** | no |
| csharpc `--hardening max` | yes | full | yes | yes | yes | IBT + SHSTK |
| NativeAOT (.NET 11) | yes | full | yes | yes | no | no |
| .NET JIT apphost (.NET 11) | yes | full | yes | yes | no | no |

**macOS.** Mach-O has no RELRO/BIND_NOW/separate-code switches to turn on: executables are always PIE
with a non-executable stack, and `__DATA_CONST` (the GOT and other relocated constants) is made read-only by
dyld after binding. So `default` on macOS means the hardened C runtime (stack protector,
`_FORTIFY_SOURCE`, zero-initialized locals) plus stack probes on generated code; clang has no
`-fstack-clash-protection` for Darwin (the runtime relies on the guard pages there). `max` adds BTI +
PAC as on Linux AArch64 (PAC-signed return addresses work in arm64 processes; BTI is not enforced
outside arm64e).

**Cost.** `default` is within measurement noise on the benchmark suite (`tools/hardening-bench.py`;
e.g. shapes −4 %, sieve −4 %, binarytrees +2 % CPU time, best-of-13 interleaved runs). CET is not in
`default` because it measured **+8 % to +16 %** on call-heavy benchmarks (shapes, sieve) while
Linux does not enforce user-space IBT at all (only the shadow stack is supported upstream); see
[ADR 0009](adr/0009-hardening-by-default.md). `max` is for services that process hostile input and can
afford it.

## Testing the runtime: sanitizers and fuzzing

- `--sanitize=address,undefined` (or `CSHARPC_SANITIZE=...`) builds the C runtime with ASan/UBSan and
  links the program against them. The conservative GC's stack and heap scanning is exempt from ASan,
  and ASan's fake stacks are disabled because they would hide roots from the collector.
- `tools/fuzz/run.sh` builds a libFuzzer harness (`tools/fuzz/fuzz_runtime.cpp`) over the input
  handling that is still C (UTF-8 decoding, UTF-16 strings from native memory, ordinal string
  primitives, `Enum.Parse` name matching) under ASan + UBSan.
- `tools/fuzz/corelib.sh` is a differential fuzzer for everything that moved to the C# corelib
  (number parsing and formatting, composite formatting, decimal, BigInteger, TimeSpan/DateTime
  formatting, string operations). `tools/fuzz/corelib/CorelibFuzz.cs` generates reproducible cases
  from a seed; the script runs it under real .NET and as a csharpc binary built with
  `--sanitize=address,undefined`, so it catches memory errors *and* any output that differs from
  .NET — exception types and messages included.

Fuzzing has already found and fixed two real bugs:

1. An **out-of-bounds read** in the runtime's UTF-8 decoders on truncated multi-byte sequences —
   reachable from `Console.ReadLine` input. Both decoders were replaced by one validating decoder that
   substitutes U+FFFD exactly like .NET (regression test `174_utf8_input`).
2. An **uninitialized rounding flag** in integer `N`/`F`/`E` formatting that could switch rounding
   modes at random — undefined behaviour with visible wrong output.
3. Integer `"R<n>"` formats threw `FormatException` after formatting moved to C#; .NET treats them
   as `"G<n>"` (differential fuzzer; regression test `175_integer_r_format`).
4. Long-standing gaps in error handling and edge cases that predate the move (regression tests
   `180`–`186`): `decimal.Parse` rejected trailing signs and left the input out of its message;
   `string.Format` errors lacked .NET's offset and reason; DateTime/TimeSpan custom formats accepted
   unterminated quotes, trailing escapes and unknown specifiers; decimal `"R"`, negative `BigInteger`
   `"X"`, double `"r"` and the sign of custom-formatted values that round to zero differed.

## Reproducible builds

The same source and options produce bit-identical executables (checked by compiling twice and from a
different working directory). With `-g` the debug info embeds absolute source paths, so moving the
checkout changes debug builds.

## Supply chain

- The compiler's only package dependency is Roslyn (`Microsoft.CodeAnalysis.CSharp`); the C runtime
  ships as source and is built locally with the system clang.
- CI actions are pinned by commit SHA and the workflow token is read-only; Renovate proposes updates
  weekly (Roslyn and the .NET SDK pin are always reviewed by hand).
