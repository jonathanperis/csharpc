#!/usr/bin/env bash
# Smoke-test a packed release bundle on a machine without the .NET SDK.
# Extracts the archive and compiles a sample from each major area (strings, collections, LINQ,
# generics, exceptions, async, reflection, formatting, regex, JSON) at -O0 and -O2 with the bundled
# binary — the NativeAOT build that ships, which CI's suites don't run — comparing stdout and the
# exit code to what real .NET recorded.
#
#   tools/smoke-release.sh <archive> [repo-root]
set -euo pipefail
ARCHIVE="${1:?usage: smoke-release.sh <archive.tar.gz|.zip> [repo-root]}"
ROOT="${2:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
WORKDIR="${RUNNER_TEMP:-/tmp}/csharpc-smoke-$$"
rm -rf "$WORKDIR"
mkdir -p "$WORKDIR"

case "$ARCHIVE" in
  *.zip)
    # Prefer Expand-Archive on Windows (handles both / and \ zip entries). Info-ZIP unzip
    # warns and exits 1 on backslash separators from Compress-Archive.
    if command -v powershell.exe >/dev/null 2>&1 || command -v pwsh >/dev/null 2>&1; then
      ps="$(command -v pwsh || command -v powershell.exe)"
      win_archive="$ARCHIVE"
      win_workdir="$WORKDIR"
      if command -v cygpath >/dev/null 2>&1; then
        win_archive="$(cygpath -w "$ARCHIVE")"
        win_workdir="$(cygpath -w "$WORKDIR")"
      fi
      "$ps" -NoProfile -Command \
        "Expand-Archive -LiteralPath '$win_archive' -DestinationPath '$win_workdir' -Force"
    else
      unzip -q "$ARCHIVE" -d "$WORKDIR"
    fi
    ;;
  *.tar.gz|*.tgz) tar -xzf "$ARCHIVE" -C "$WORKDIR" ;;
  *) echo "unsupported archive: $ARCHIVE" >&2; exit 2 ;;
esac

BUNDLE="$(find "$WORKDIR" -maxdepth 1 -mindepth 1 -type d | head -1)"
if [[ -z $BUNDLE || ! -d $BUNDLE ]]; then
  echo "smoke-release: no top-level directory in archive" >&2
  exit 1
fi

if [[ -x $BUNDLE/csharpc ]]; then
  CSHARPC="$BUNDLE/csharpc"
elif [[ -f $BUNDLE/csharpc.exe ]]; then
  CSHARPC="$BUNDLE/csharpc.exe"
else
  echo "smoke-release: csharpc binary missing in $BUNDLE" >&2
  ls -la "$BUNDLE" >&2
  exit 1
fi

# Prove we are not accidentally using a SDK-hosted build.
if command -v dotnet >/dev/null 2>&1; then
  echo "smoke-release: note: dotnet is on PATH ($(command -v dotnet)); the smoke still invokes the bundled binary only"
fi

# .expected files are recorded on Linux (LF). On Windows, Console.WriteLine uses
# Environment.NewLine ("\r\n") — same derivation as tests/platform.sh platform_expected.
want_stdout() {
  if [[ ${OSTYPE:-} == msys* || ${OSTYPE:-} == cygwin* || "$(uname -s 2>/dev/null)" == MINGW* ]]; then
    perl -pe 's/\n/\r\n/g' "$1"
  else
    cat "$1"
  fi
}

run_sample() {
  local sample=$1 opt=$2
  local src="$ROOT/tests/samples/${sample}.cs"
  local expected="$ROOT/tests/samples/${sample}.expected"
  local want_exit=0
  [[ -f $ROOT/tests/samples/${sample}.exitcode ]] && want_exit="$(tr -d '[:space:]' < "$ROOT/tests/samples/${sample}.exitcode")"
  # An unhandled exception aborts with 134 on Unix; on Windows it exits 0xE0434352, which Git Bash
  # reports as 127 (tests/platform.sh platform_exit_code).
  if [[ $want_exit == 134 && ( ${OSTYPE:-} == msys* || ${OSTYPE:-} == cygwin* || "$(uname -s 2>/dev/null)" == MINGW* ) ]]; then want_exit=127; fi
  local out="$WORKDIR/${sample}${opt}"
  local want="$WORKDIR/${sample}.want"
  echo "smoke-release: compiling $sample $opt"
  "$CSHARPC" "$src" -o "$out" "$opt"
  # Windows: csharpc may write .exe
  local exe=$out
  [[ -f ${out}.exe ]] && exe=${out}.exe
  want_stdout "$expected" > "$want"
  local code=0
  "$exe" > "$WORKDIR/${sample}${opt}.stdout" 2>/dev/null || code=$?
  diff -u "$want" "$WORKDIR/${sample}${opt}.stdout"
  if [[ $code != "$want_exit" ]]; then
    echo "smoke-release: $sample $opt exited $code, expected $want_exit" >&2
    exit 1
  fi
  echo "smoke-release: $sample $opt OK"
}

for sample in 01_hello 05_strings 101_dictionary 104_linq 106_arrays_as_interfaces 133_enums_generic_math_misc \
              09_unhandled_exception 110_async_exceptions 604_reflection_constructors 142_numeric_format_exact_integers \
              290_regex_basic 311_json_escaping; do
  for opt in -O0 -O2; do run_sample "$sample" "$opt"; done
done
echo "smoke-release: all OK"
