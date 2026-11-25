# Per-OS helpers for the test scripts (sourced after ROOT is set). The expectations are recorded with
# .NET on Linux; these functions derive what .NET does on the host OS from them.
case "$(uname -s)" in
  Darwin) HOST_OS=macos ;;
  MINGW*|MSYS*|CYGWIN*) HOST_OS=windows ;;
  *) HOST_OS=linux ;;
esac
EXE_SUFFIX=""; [[ $HOST_OS == windows ]] && EXE_SUFFIX=".exe"

# Culture. The recordings come from Linux with the C locale, where .NET's (and csharpc's) current culture
# is the invariant culture, with ICU's root collation. Both sides follow the user's culture, so:
#  * Linux: the C locale for every run (real .NET and csharpc read LC_ALL / LC_MESSAGES / LANG through
#    ICU's default locale).
#  * macOS and Windows: the user culture comes from the system settings (NSLocale, the Windows user
#    locale), so real .NET runs with tests/oracle-hook (a startup hook making the invariant culture
#    current, ORACLE_HOOK) and csharpc programs are compiled with tests/culture-pin/CulturePin.cs (a
#    [ModuleInitializer] doing the same, CULTURE_PIN).
# Tests with a NAME.env file (VAR=value lines, e.g. LANG=pt_BR.UTF-8) run both sides under that
# environment instead, without the pin; platform_env prints its assignments.
if [[ $HOST_OS == linux ]]; then
  export LC_ALL=C.UTF-8
  unset LC_MESSAGES LANGUAGE
fi
CULTURE_PIN=()
[[ $HOST_OS != linux ]] && CULTURE_PIN=("$ROOT/tests/culture-pin/CulturePin.cs")
# platform_env <dir> <name>: the VAR=value words of <dir>/<name>.env (empty if there is none).
platform_env() { [[ -f "$1/$2.env" ]] && grep -Ev '^\s*(#|$)' "$1/$2.env" | tr '\n' ' '; return 0; }

# Cross-target runs: CSHARPC_TEST_TARGET=wasm32-wasip1 compiles every test for WebAssembly (WASI) and
# runs it with tests/wasm-run.sh (wasmtime). The expectations are still the ones recorded with .NET on
# Linux (there is no .NET for plain WASI to record with); tests/platform-skips.txt lists, under "wasi",
# what WebAssembly can't do (threads, processes, ...). TARGET_OS names the platform the tests run on.
TARGET_OS=$HOST_OS
TARGET_FLAGS=()
RUN_PREFIX=()
if [[ "${CSHARPC_TEST_TARGET:-}" == wasm32* ]]; then
  TARGET_OS=wasi
  EXE_SUFFIX=".wasm"
  TARGET_FLAGS=(--target "$CSHARPC_TEST_TARGET")
  RUN_PREFIX=("$ROOT/tests/wasm-run.sh")
fi

# Expected stdout (stdin -> stdout). On Windows, Console.WriteLine ends lines with "\r\n"
# (Environment.NewLine). Tests whose output differs there for another reason are listed in
# tests/platform-skips.txt; tests/check-oracle.sh checks this derivation against real .NET.
platform_expected() {
  if [[ $HOST_OS == windows ]]; then perl -pe 's/\n/\r\n/g'; else cat; fi
}

# Expected exit status as the shell sees it. An unhandled exception ends a .NET process with SIGABRT
# on Unix (status 134) and with exit code 0xE0434352 on Windows, which Git Bash reports as 127 (it
# does that for exception-like NTSTATUS exit codes; other codes are reported modulo 256, as on Unix).
platform_exit_code() {
  if [[ $HOST_OS == windows && "$1" == 134 ]]; then echo 127; else echo "$1"; fi
}

# The compiler's diagnostics (stdin -> stdout) spelled like the recorded .diagnostics files: on
# Windows the compiler, a .NET program, ends lines with "\r\n" and prints "D:\a\x\y.cs(1,2): ...",
# which becomes "/d/a/x/y.cs(1,2): ..." (the shell's spelling of the same path).
platform_compiler_output() {
  if [[ $HOST_OS == windows ]]; then
    tr -d '\r' | sed -E -e 's#^([A-Za-z]):[\\/]#/\L\1/#' -e ':a' -e 's#^([^(]*)\\#\1/#' -e 'ta'
  else
    cat
  fi
}

# Entries whose reason starts with "live:" are not skipped: on that OS the test's output differs
# under real .NET for a reason the derivation above can't express (bare "\n"s the program writes
# itself, Windows paths, ...), so its expected stdout and exit status are recorded live, by running
# it with `dotnet run` on this machine.
platform_is_live() { [[ "$1" == live:* ]]; }

# The recordings come from Linux with the C locale, where .NET's current culture is the invariant
# culture. A macOS or Windows user has a real culture (en-US on CI), so real .NET runs there with
# tests/oracle-hook, a startup hook that makes the invariant culture current (ICU collation stays;
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT would make comparisons ordinal). Sets ORACLE_HOOK.
ORACLE_HOOK=""
platform_oracle_hook() {
  [[ $HOST_OS != linux && -z "$ORACLE_HOOK" ]] || return 0
  local dir="${TMPDIR:-/tmp}/csharpc-oracle-hook"
  [[ -f "$dir/OracleHook.dll" ]] ||
    "${DOTNET:-dotnet}" build "$ROOT/tests/oracle-hook/OracleHook.csproj" -c Release -o "$dir" -v quiet -nologo > /dev/null ||
    { echo "building tests/oracle-hook failed" >&2; return 1; }
  ORACLE_HOOK="$dir/OracleHook.dll"
  [[ $HOST_OS == windows ]] && ORACLE_HOOK="$(cygpath -w "$ORACLE_HOOK")"
  export ORACLE_HOOK
}

# NativeAOT as the oracle. Where NativeAOT and the JIT print different things for the same program (string.IsInterned:
# the JIT interns every literal up front, NativeAOT only what it is asked to), csharpc matches NativeAOT, the PublishAot
# drop-in target. Such a test has a <dir>/<name>.aot file (one line: the difference) and is recorded and checked with its
# PublishAot build instead of `dotnet run`. Startup hooks don't exist under NativeAOT, so on macOS and Windows its culture
# is the user's: an .aot test must not print culture-dependent text (Linux runs in the C locale, as always).
platform_is_aot() { [[ -f "$1/$2.aot" ]]; }

# platform_aot_run <dir> <name> <stdin> <out> [VAR=value ...]: publishes <dir>/<name>.cs with PublishAot (into a
# scratch directory), runs it with <stdin>, writes stdout to <out> and prints the exit status. A failed publish prints
# the build log to stderr and the status 125.
platform_aot_run() {
  local dir="$1" name="$2" in="$3" out="$4" pub
  pub="$(mktemp -d)"
  if ! (cd "$dir" && "${DOTNET:-dotnet}" publish "$name.cs" -o "$pub/bin" -p:PublishAot=true -p:WarningLevel=0 -p:NuGetAudit=false \
          -p:AllowUnsafeBlocks=true -v quiet -nologo > "$pub/log" 2>&1); then
    echo "PublishAot of $name failed:" >&2; cat "$pub/log" >&2; rm -rf "$pub"; echo 125; return
  fi
  (env "${@:5}" "$pub/bin/$name$EXE_SUFFIX" < "$in") > "$out" 2>/dev/null
  local code=$?
  rm -rf "$pub"
  echo "$code"
}

# platform_live_expected <dir> <file.cs> <stdin> <out> [VAR=value ...]: records stdout to <out>, prints the
# exit status. With environment assignments (a NAME.env test) .NET runs under them without the hook.
platform_live_expected() {
  local hook=""
  if [[ $# -le 4 ]]; then platform_oracle_hook; hook="$ORACLE_HOOK"; fi
  (cd "$1" && env "${@:5}" DOTNET_STARTUP_HOOKS="$hook" "${DOTNET:-dotnet}" run "$2" -p:WarningLevel=0 -p:AllowUnsafeBlocks=true -p:StartupHookSupport=true \
    2>/dev/null < "$3") > "$4.raw"
  local code=$?
  grep -U -a -v -E '^[^ ]+\([0-9]+,[0-9]+\): warning ' < "$4.raw" > "$4"
  rm -f "$4.raw"
  echo "$code"
}

# Prints the reason if test $1 (sample name, program name or conformance path) is listed in
# tests/platform-skips.txt for this OS.
platform_skip() {
  awk -v os="${TARGET_OS:-$HOST_OS}" -v t="$1" '$1 == os && $2 == t { $1 = $2 = ""; sub(/^ +/, ""); print; exit }' "$ROOT/tests/platform-skips.txt"
}
