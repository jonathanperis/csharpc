#!/usr/bin/env bash
# Where does the native compiler (tools/bootstrap/selfhost.sh's csharpc-native) differ from the managed one?
# For each sample, compiles it with both to LLVM IR and prints how the IR and the diagnostics differ, then
# the native compiler's own trace (CSHARPC_TRACE=1: internal errors with stack traces, failed generators).
# The same input must give the same IR, so the first differing lines are the construct the native compiler
# handled differently.
#
# Usage: tools/bootstrap/native-diff.sh [-O0|-O2] sample-name ...
# Environment: NATIVE_DIFF_DIR (where the IR files and logs are kept; default: a temporary directory).
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
OPT="-O0"
if [[ "${1:-}" == -O* ]]; then OPT="$1"; shift; fi
BIN="$ROOT/src/Csharpc/bin/Release/net11.0"
NATIVE="$BIN/csharpc-native"
[[ -x "$NATIVE" ]] || { echo "no $NATIVE: run tools/bootstrap/selfhost.sh first"; exit 2; }
OUT="${NATIVE_DIFF_DIR:-$(mktemp -d)}"
mkdir -p "$OUT"

for name in "$@"; do
  src="$ROOT/tests/samples/$name.cs"
  [[ -f "$src" ]] || { echo "== $name: no such sample"; continue; }
  "$DOTNET" "$BIN/csharpc.dll" "$src" "$OPT" --emit llvm-ir -o "$OUT/$name.managed.ll" > "$OUT/$name.managed.log" 2>&1; managed_rc=$?
  "$NATIVE" "$src" "$OPT" --emit llvm-ir -o "$OUT/$name.native.ll" > "$OUT/$name.native.log" 2>&1; native_rc=$?
  echo "== $name ($OPT): managed exit $managed_rc, native exit $native_rc"
  if [[ -f "$OUT/$name.managed.ll" && -f "$OUT/$name.native.ll" ]]; then
    diff "$OUT/$name.managed.ll" "$OUT/$name.native.ll" > "$OUT/$name.ll.diff"
    echo "   IR: $(wc -l < "$OUT/$name.managed.ll") lines managed, $(wc -l < "$OUT/$name.native.ll") native, $(grep -c '^[<>]' "$OUT/$name.ll.diff") differing"
    head -60 "$OUT/$name.ll.diff" | cut -c1-220 | sed 's/^/   /'
  fi
  if ! diff -q "$OUT/$name.managed.log" "$OUT/$name.native.log" > /dev/null; then
    echo "   diagnostics differ:"
    diff "$OUT/$name.managed.log" "$OUT/$name.native.log" | head -20 | cut -c1-400 | sed 's/^/   /'
  fi
  CSHARPC_TRACE=1 "$NATIVE" "$src" "$OPT" --emit llvm-ir -o "$OUT/$name.trace.ll" > /dev/null 2> "$OUT/$name.native.trace"
  # the trace lists every symbol lowered: keep the generators' exceptions (with stack traces) and internal errors only
  grep -A25 -E "^\[csharpc\] generator|^[A-Za-z.]*(Exception|Error):" "$OUT/$name.native.trace" > "$OUT/$name.native.failures" || true
  rm -f "$OUT/$name.native.trace"
  if [[ -s "$OUT/$name.native.failures" ]]; then
    echo "   native failures (CSHARPC_TRACE=1):"
    head -50 "$OUT/$name.native.failures" | cut -c1-300 | sed 's/^/   /'
  fi
done
echo "files: $OUT"
