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

- **Rejection samples** pin down programs csharpc must refuse *cleanly*: valid C# that used to
  crash the compiler and now gets an explicit diagnostic (`283_unsupported_field_in_base`,
  `284_recursive_generic_expansion`). A `sample.diagnostics` file holds csharpc's expected stderr
  (paths relative to `tests/samples/`); the compiler must exit with 1 and print exactly that. It is
  csharpc's own specification, written from the compiler's output, not a .NET recording. `--regen`
  still records `sample.expected` from .NET, which shows that the program is valid C#.

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
`CSHARPC_OPT`, `CSHARPC_TEST_TIMEOUT`, `CSHARPC_TEST_TARGET` (cross-target runs, see below).

### Platforms

Expectations are recorded from .NET on Linux and checked on every CI platform (Linux x86-64, Linux
ARM64, macOS ARM64, Windows x64). A test whose behaviour differs under real .NET on another OS too (for example a
`DllImport("libm.so.6")`, a Linux soname that .NET can't load on macOS) is listed with its reason
in `tests/platform-skips.txt` and reported as `SKIP ... (platform-dependent ...)` there; never list
a test because csharpc differs from .NET. `run.sh` resolves `$TMPDIR` to a path without symlinks
(macOS's `/var/folders` is reached through `/var -> /private/var`, which `getcwd` would expose).
On macOS the scripts need bash 4+ and GNU coreutils (`brew install bash coreutils`, with
`$(brew --prefix coreutils)/libexec/gnubin` on `PATH`); `tests/debuginfo.sh` drives gdb and is
Linux-only.

**Windows.** The scripts run in Git Bash (`tests/platform.sh` holds the per-OS helpers; checkouts
must keep LF line endings, `core.autocrlf=false`). .NET on Windows ends `Console.WriteLine` with
`Environment.NewLine`, `"\r\n"`, so the expected stdout is the recording with every `"\n"` turned
into `"\r\n"`, compared byte for byte (csharpc writes the standard streams in binary mode). An
unhandled exception ends .NET with exit code `0xE0434352`, which Git Bash reports as 127 (the
conformance runner, in Python, maps it back to the recorded 134). `tests/check-oracle.sh` runs every
test under real `dotnet run` on the machine and checks that derivation; CI runs it on Windows. Tests
for which it doesn't hold are listed in `tests/platform-skips.txt` with the reason; most are marked
`live:`, which records their expected output and exit status from real .NET on the test machine at
test time instead of skipping them (programs that write their own bare `"\n"`, `AppendLine` lengths,
Windows paths, `TZ`, which .NET ignores on Windows, `DllImport("libc")`, culture data from another
ICU version).

**Culture.** The recordings come from Linux in the C locale, where the current culture is the
invariant one (with ICU's root collation). csharpc follows the user's culture like .NET
([19 — Globalization](19-globalization.md)), so the scripts pin it on both sides: on Linux
`tests/platform.sh` exports `LC_ALL=C.UTF-8` (and unsets `LC_MESSAGES`/`LANGUAGE`); on macOS and
Windows, where the culture comes from the system settings, real .NET runs with `tests/oracle-hook`
(a startup hook that makes the invariant culture current) and csharpc programs are compiled together
with `tests/culture-pin/CulturePin.cs` (a `[ModuleInitializer]` doing the same).
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is not equivalent: it also makes comparisons ordinal.
A sample with a `NAME.env` file (`VAR=value` lines, e.g. `LC_ALL=pt_BR.UTF-8`) is recorded and run
with that environment on both sides instead, without the pin.

**WebAssembly.** `CSHARPC_TEST_TARGET=wasm32-wasip1` makes `tests/run.sh`, `tests/conformance.sh`
and `tests/programs.sh` compile every test with `--target wasm32-wasip1` and run it with
`tests/wasm-run.sh`: wasmtime with the environment inherited, the host file system preopened at `/`
(csrt follows `$PWD` as the current directory) and an 8 MiB wasm stack. It needs the WASI SDK
(`WASI_SDK_PATH`, LLVM 18: wasi-sdk 24) and wasmtime on `PATH` (`WASMTIME` overrides). The expected
output is the Linux recording, unchanged: there is no .NET for plain WASI to record with, and a WASI
build is meant to behave like a native one. An unhandled exception traps, and wasmtime exits with
134, the recorded status. Tests whose recorded behaviour WebAssembly can't have are listed under
`wasi` in `tests/platform-skips.txt`, each with its reason: they need threads, assume LP64 C types
in P/Invoke signatures, or print errno numbers. CI runs the three suites this way in the
`test (wasm32-wasi)` job. It also runs `310_gc_roots_in_locals` with `CSRT_GC_STRESS=1` (a
collection at every allocation), which checks that the compiler's GC root spilling is complete
([ADR 0011](adr/0011-wasm32.md)).

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
- **Mind the culture.** Tests run with the invariant culture current (see above). To test a
  culture, use it explicitly (`CultureInfo.GetCultureInfo("de-DE")`, or set `CurrentCulture`), or
  give the sample a `NAME.env`; culture data can differ between ICU versions, so such samples are
  `live:` on macOS and Windows.
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
