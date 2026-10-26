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
tests/run.sh --all-opts         # -O0 and -O2
tests/run.sh --shard 0/4        # one deterministic slice of the sample list (what CI runs)
tests/run.sh --repeat 20 544    # flake hunting; the run fails if any repeat fails
tests/run.sh --list             # print the selected names and do not build
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

## CI

The merge bar is the five jobs `test (x86-64)`, `test (arm64)`, `test (macos-arm64)`,
`test (windows-x64)` and `test (wasm32-wasi)` in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml).
A full run (push to `main` or `ci/**`, a pull request, or a manual dispatch of that workflow) splits
the differential suite across shards and runs size, conformance, programs, projects, the Windows
oracle and the IL front-end beside them. Each `test (<platform>)` job is only the aggregate: it
fails if any shard or the suites job failed, or if the shard manifests do not list every sample at
`-O0` and at `-O2` exactly once (`tools/ci-plan.sh coverage`). Shards are a round-robin over the
`LC_ALL=C` sorted sample names (`tests/run.sh --shard INDEX/COUNT`).

Shard counts are chosen from the unsharded step times on run
[40310822368](https://github.com/jonathanperis/csharpc/actions/runs/40310822368) (x86-64 differential
38.5 min, Windows 49.1 min, arm64 35.4 min, macOS 29.5 min, wasm 21.5 min; wall clock 69 min on
Windows). x86-64 and Windows use 4 shards. arm64, macOS and wasm use 2: arm and macOS runners queue,
and wasm shares `ubuntu-24.04` with x86-64, so more shards wait instead of finishing sooner.

A push or pull request that only touches `docs/**`, `*.md`, or `LICENSE` / `COPYING` / `NOTICE` /
`AUTHORS` skips the compiler suite when some ancestor of that commit already has the five `test`
checks green. The same five job names still run and pass, which is what a required check needs;
`paths-ignore` would leave them pending. The skip does not apply when that ancestor's gate is
missing, failed, or cancelled (a docs push that cancels an in-progress full run therefore runs the
suite). `main` ignores a `CI-Only:` trailer and a `ci/targets` file, so those cannot drop the gate.

### Targeted runs

A targeted run is partial. Its jobs are named `partial (<platform>)`, and it does not report
`test (<platform>)` as success, so it is not the merge bar. Release publishes only after a full
gate (successful `diff (` shards for every platform and the five `test` checks); a partial or
docs-only CI success does not publish.

Two ways to start one:

1. **Push a `ci/**` branch** (the agent checkout cannot dispatch workflows: `gh workflow run` is
   HTTP 403). Put the spec in the tip commit's message, or in a `ci/targets` file, or both (trailer
   keys win). The run title shows the commit message, so keep the trailer on the commit you push.

   ```
   CI-Only: filter=544_blocking_collection platforms=macos-arm64 repeat=20
   ```

   `ci/targets` (not read on `main` or on pull requests):

   ```
   filter=544_blocking_collection
   platforms=macos-arm64
   opt=-O0
   repeat=20
   skip_other=true
   ```

2. **Actions → CI (targeted) → Run workflow**, for a token that has `actions: write`. The same
   keys are the inputs. The workflow is not named `CI`, so it cannot turn the merge-bar checks
   green and it does not trigger a release.

`filter` is a sample-name substring. `platforms` is `all` or a comma-separated list of `x86-64`,
`arm64`, `macos-arm64`, `windows-x64`, `wasm32-wasi`. `opt` is `all`, `-O0`, or `-O2`. `repeat`
is 1–50 and requires a filter (a repeat of the whole suite is refused). `skip_other` defaults to
true on a targeted run, which skips size, conformance, programs, projects, the oracle and IL.
The run must narrow the suite (a filter, a platform subset, one opt level, or a repeat); a bare
`CI-Only:` is rejected. Partial runs are one job per platform, not sharded, so a repeat's log stays
together. Delete `ci/targets` before merging the branch.

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
- `tests/size.sh` compiles the small programs in `tests/size/` with the default options and fails when
  one exceeds its budget in `tests/size/budgets.txt` (per platform; size + max(4 KiB, 3 %): file sizes move in whole pages). Being smaller than
  .NET is part of the standing goal ([06-roadmap.md](06-roadmap.md)), so a size regression is a test
  failure. Platforms without budgets print the sizes (`tests/size.sh --print` gives budget lines).
  On Linux it also runs `tools/checksec.sh --check` on the same executables, so a size win can't come
  from dropping hardening: full RELRO, NX stack, separate code (no 4 KiB file page shared between code
  and anything else) and, on AArch64, a segment layout that loads with 64 KiB pages.
