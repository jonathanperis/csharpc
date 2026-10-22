#!/usr/bin/env bash
# Differential fuzzing of the C# corelib: tools/fuzz/corelib/CorelibFuzz.cs runs under real .NET
# (the oracle) and as a csharpc binary built with ASan + UBSan; any output difference or sanitizer
# report is a bug.
#   tools/fuzz/corelib.sh [rounds] [cases-per-round] [first-seed]     (default 20 x 20000, seed 1)
# Reproduce a reported round with the same seed: tools/fuzz/corelib.sh 1 <cases> <seed>.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
ROUNDS="${1:-20}"
CASES="${2:-20000}"
SEED="${3:-1}"
DOTNET="${DOTNET:-dotnet}"
BUILD="$HERE/build/corelib"
mkdir -p "$BUILD" "$HERE/artifacts"
SRC="$HERE/corelib/CorelibFuzz.cs"

CSHARPC="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
[ -f "$CSHARPC" ] || "$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo
"$DOTNET" "$CSHARPC" "$SRC" -O2 --sanitize=address,undefined -o "$BUILD/fuzz"

# Number formatting must not depend on the machine's culture on the .NET side either.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ASAN_OPTIONS="detect_leaks=0:abort_on_error=1" UBSAN_OPTIONS="halt_on_error=1:print_stacktrace=1"

failed=0
for ((round = 0; round < ROUNDS; round++)); do
  seed=$((SEED + round))
  "$DOTNET" run "$SRC" -- "$seed" "$CASES" > "$BUILD/expected.txt"
  status=0
  "$BUILD/fuzz" "$seed" "$CASES" > "$BUILD/actual.txt" 2> "$BUILD/stderr.txt" || status=$?
  if [ "$status" -ne 0 ] || ! cmp -s "$BUILD/expected.txt" "$BUILD/actual.txt"; then
    failed=$((failed + 1))
    out="$HERE/artifacts/corelib-seed$seed"
    cp "$BUILD/expected.txt" "$out.expected"; cp "$BUILD/actual.txt" "$out.actual"; cp "$BUILD/stderr.txt" "$out.stderr"
    echo "seed $seed: exit $status, $(diff "$out.expected" "$out.actual" | grep -c '^<' || true) differing lines -> $out.*"
    diff "$out.expected" "$out.actual" | head -6 || true
  else
    echo "seed $seed: $CASES cases identical"
  fi
done
echo "$((ROUNDS - failed))/$ROUNDS rounds identical"
[ "$failed" -eq 0 ]
