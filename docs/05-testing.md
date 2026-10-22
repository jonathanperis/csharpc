# 05 — Testing

## Differential testing against .NET

csharpc's correctness criterion is simple: **a program compiled by csharpc must behave exactly like
the same program run on .NET**. The test suite enforces this directly. Real .NET is the oracle.

```
                  ┌─▶ dotnet run sample.cs  ──▶ sample.expected (+ sample.exitcode)   [recorded once, checked in]
tests/samples/  ──┤
  sample.cs       └─▶ csharpc sample.cs ──▶ ./sample ──▶ stdout + exit code ──▶ diff
```

- **Recording** uses .NET 10+ *file-based apps*: `dotnet run sample.cs` compiles and runs a single
  file with no project. Its stdout becomes `sample.expected`. A nonzero exit code is written to
  `sample.exitcode` (absent means 0).
- **Checking** builds csharpc (Release), compiles each sample at `-O2` (or `$CSHARPC_OPT`), runs it
  with a timeout (`$CSHARPC_TEST_TIMEOUT`, default 20 s), and requires byte-identical stdout and the
  same exit code. stderr isn't compared, because the .NET stack trace is out of scope.

There are no hand-written expected outputs. That rules out a whole class of mistakes: the expected
`0.30000000000000004`, `-2147483648` or `1E-05` is whatever .NET actually prints.

## Running

```bash
tests/run.sh                    # all samples
tests/run.sh 06                 # samples whose name contains "06"
CSHARPC_OPT=-O0 tests/run.sh    # unoptimized codegen (catches bugs that the optimizer hides)
tests/run.sh --regen            # re-record .expected/.exitcode from real .NET
tests/run.sh --regen 07         # re-record a single sample
```

Environment: `DOTNET` (path to the `dotnet` executable; the .NET 11 SDK must be first on `PATH`),
`CSHARPC_OPT`, `CSHARPC_TEST_TIMEOUT`.

Failure output shows the compile errors (including `CSLLVM0001` "not supported yet" diagnostics) or
the first lines of a unified diff between the expected and actual stdout.

## Adding a sample

1. Write `tests/samples/NN_topic.cs` as an ordinary C# program. Top-level statements are fine.
   Start with a comment explaining what it tests.
2. Make sure it compiles and runs on .NET: `dotnet run tests/samples/NN_topic.cs`.
3. Record it: `tests/run.sh --regen NN_topic`. Commit both the `.cs` and `.expected` files (and
   `.exitcode` if present).
4. Run `tests/run.sh NN_topic`.

Guidelines:

- **Make it deterministic.** No time, randomness, environment, thread scheduling or hash-code-order
  output.
- **Stay culture-neutral.** csharpc formats numbers like the invariant culture. Avoid output whose
  .NET formatting depends on the current culture, or run the recording with
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
- **Avoid compile-time folding hiding runtime paths.** Roslyn folds constant expressions, and so
  does csharpc's `ToStringValue`. To test runtime behavior, route values through variables or
  `args.Length` (as `11_divide_by_zero.cs` does). Also note that C# rejects constant conversions that
  overflow (`(short)40000` is error CS0221 without `unchecked`).
- **Keep warnings off stdout.** `dotnet run` prints build warnings to stdout, where they would land
  in `.expected`. Avoid warning-producing code (or suppress it with `#pragma warning disable`), and
  check the recorded file.
- **Testing a failure path:** let the exception go unhandled. Both runtimes exit with 134, and stdout
  up to that point must match (`09`–`12`).
- **Samples ahead of the compiler are fine.** A sample may use a feature csharpc doesn't support
  yet. It fails with `CSLLVM0001` until the feature lands, which makes the suite a to-do list.

## Exit codes

| situation | .NET | csharpc |
|---|---|---|
| normal completion, `void Main` | 0 | 0 |
| `int Main` / top-level `return n` | n | n |
| `Environment.Exit(n)` | n | n (stdout is flushed first) |
| unhandled exception | 134 (SIGABRT) | 134 (`abort()` in csrt) |
| C# compile error (`dotnet run`) | 1 (build failed) | csharpc exits 1 and no binary is produced |

## Other checks

- Every module is run through the LLVM verifier before optimization. Invalid IR is an internal
  compiler error (exit 70, `<output>.invalid.ll` written for inspection).
- `--dump-cfg` and `--emit llvm-ir` (at `-O0` and `-O2`) are the main debugging tools. When libLLVM
  crashes, use `CSHARPC_TRACE=1` and `gdb` ([07-spike-log.md](07-spike-log.md)).
