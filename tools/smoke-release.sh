#!/usr/bin/env bash
# Smoke-test a packed release bundle on a machine without the .NET SDK.
# Extracts the archive, compiles 01_hello and 104_linq, compares stdout to .expected.
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

run_sample() {
  local sample=$1
  local src="$ROOT/tests/samples/${sample}.cs"
  local expected="$ROOT/tests/samples/${sample}.expected"
  local out="$WORKDIR/${sample}"
  echo "smoke-release: compiling $sample"
  "$CSHARPC" "$src" -o "$out" -O0
  # Windows: csharpc may write .exe
  local exe=$out
  [[ -f ${out}.exe ]] && exe=${out}.exe
  diff -u "$expected" <("$exe")
  echo "smoke-release: $sample OK"
}

run_sample 01_hello
run_sample 104_linq
echo "smoke-release: all OK"
