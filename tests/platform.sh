# Per-OS helpers for the test scripts (sourced after ROOT is set). The expectations are recorded with
# .NET on Linux; these functions derive what .NET does on the host OS from them.
case "$(uname -s)" in
  Darwin) HOST_OS=macos ;;
  MINGW*|MSYS*|CYGWIN*) HOST_OS=windows ;;
  *) HOST_OS=linux ;;
esac
EXE_SUFFIX=""; [[ $HOST_OS == windows ]] && EXE_SUFFIX=".exe"

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
# culture (csharpc's). A Windows user has a real culture (en-US on CI), so real .NET runs there with
# tests/oracle-hook, a startup hook that makes the invariant culture current (ICU collation stays;
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT would make comparisons ordinal). Sets ORACLE_HOOK.
ORACLE_HOOK=""
platform_oracle_hook() {
  [[ $HOST_OS == windows && -z "$ORACLE_HOOK" ]] || return 0
  local dir="${TMPDIR:-/tmp}/csharpc-oracle-hook"
  [[ -f "$dir/OracleHook.dll" ]] ||
    "${DOTNET:-dotnet}" build "$ROOT/tests/oracle-hook/OracleHook.csproj" -c Release -o "$dir" -v quiet -nologo > /dev/null ||
    { echo "building tests/oracle-hook failed" >&2; return 1; }
  ORACLE_HOOK="$(cygpath -w "$dir/OracleHook.dll")"
  export ORACLE_HOOK
}

# platform_live_expected <dir> <file.cs> <stdin> <out>: records stdout to <out>, prints the exit status.
platform_live_expected() {
  platform_oracle_hook
  (cd "$1" && DOTNET_STARTUP_HOOKS="$ORACLE_HOOK" "${DOTNET:-dotnet}" run "$2" -p:WarningLevel=0 -p:AllowUnsafeBlocks=true -p:StartupHookSupport=true \
    2>/dev/null < "$3") > "$4.raw"
  local code=$?
  grep -U -a -v -E '^[^ ]+\([0-9]+,[0-9]+\): warning ' < "$4.raw" > "$4"
  rm -f "$4.raw"
  echo "$code"
}

# Prints the reason if test $1 (sample name, program name or conformance path) is listed in
# tests/platform-skips.txt for this OS.
platform_skip() {
  awk -v os="$HOST_OS" -v t="$1" '$1 == os && $2 == t { $1 = $2 = ""; sub(/^ +/, ""); print; exit }' "$ROOT/tests/platform-skips.txt"
}
