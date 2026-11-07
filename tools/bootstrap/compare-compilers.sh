#!/usr/bin/env bash
# Is a change to the compiler's lowering invisible in its output, and what does it do to the time?
# Compiles the same inputs with a baseline build of csharpc (the bin directory of an earlier `dotnet build -c Release`)
# and with this checkout's, once each, and compares the results byte for byte; with -v the lowering steps of both
# are listed side by side (csharpc -v prints how long each module-level step took).
#
# Usage: tools/bootstrap/compare-compilers.sh BASELINE_BIN_DIR [-O0|-O2] [--emit KIND] INPUT...
#        (INPUT: a .cs file, a project, or an assembly; --emit llvm-ir compares the IR, the default compares the executables)
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
BASE="$1"; shift
OPT="-O0"; EMIT=()
while [[ "${1:-}" == -O* || "${1:-}" == --emit ]]; do
  if [[ "$1" == --emit ]]; then EMIT=(--emit "$2"); shift 2; else OPT="$1"; shift; fi
done
NEW="$ROOT/src/Csharpc/bin/Release/net11.0"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
status=0

for input in "$@"; do
  name="$(basename "$input")"
  for which in base new; do
    dir="$NEW"; [[ $which == base ]] && dir="$BASE"
    "$DOTNET" "$dir/csharpc.dll" "$input" "$OPT" "${EMIT[@]}" -v -o "$WORK/$which.out" > "$WORK/$which.log" 2>&1 || { echo "FAIL $name: the $which compiler failed"; tail -5 "$WORK/$which.log"; status=1; continue 2; }
  done
  if cmp -s "$WORK/base.out" "$WORK/new.out"; then echo "SAME $name ($OPT${EMIT[*]:+ ${EMIT[*]}}): $(stat -c %s "$WORK/new.out") bytes"; else echo "DIFFERENT $name ($OPT)"; status=1; fi
  for which in base new; do
    echo "  $which: $(grep -E ' lower CFG' "$WORK/$which.log" | cut -c1-70)"
    grep -E "steps \(ms" "$WORK/$which.log" | sed 's/^ */    /' | cut -c1-1500
  done
done
exit $status
