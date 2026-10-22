#!/usr/bin/env bash
# Differential test runner.
#
#   tests/run.sh            compile every tests/samples/*.cs with csharpc, run it, and compare
#                           stdout + exit code against the recorded .NET results
#   tests/run.sh --regen    (re)record the expected results by running each sample on real .NET
#                           (`dotnet run file.cs`), which is the oracle for C# semantics
#   tests/run.sh 06         only samples whose name contains "06"
#
# Environment: CSHARPC_OPT=-O0 to test unoptimized code, DOTNET=path/to/dotnet.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SAMPLES="$ROOT/tests/samples"
DOTNET="${DOTNET:-dotnet}"
OPT="${CSHARPC_OPT:--O2}"
REGEN=0
FILTER=""
for arg in "$@"; do
  case "$arg" in
    --regen) REGEN=1 ;;
    *) FILTER="$arg" ;;
  esac
done

if [[ $REGEN == 1 ]]; then
  for src in "$SAMPLES"/*.cs; do
    name="$(basename "$src" .cs)"
    [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
    printf 'recording %-32s' "$name"
    # File-based apps (dotnet run app.cs) — the real .NET compiler and runtime.
    (cd "$SAMPLES" && "$DOTNET" run "$name.cs" > "$name.expected" 2>/dev/null)
    code=$?
    if [[ $code != 0 ]]; then echo "$code" > "$SAMPLES/$name.exitcode"; else rm -f "$SAMPLES/$name.exitcode"; fi
    echo "exit=$code"
  done
  exit 0
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
pass=0; fail=0; failed=()
for src in "$SAMPLES"/*.cs; do
  name="$(basename "$src" .cs)"
  [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
  expected="$SAMPLES/$name.expected"
  [[ -f "$expected" ]] || { echo "SKIP $name (no .expected — run with --regen)"; continue; }
  want_code=0
  [[ -f "$SAMPLES/$name.exitcode" ]] && want_code="$(cat "$SAMPLES/$name.exitcode")"

  if ! "${CSHARPC[@]}" "$OPT" "$src" -o "$OUT/$name" 2> "$OUT/$name.compile"; then
    echo "FAIL $name (compile error)"; sed 's/^/     /' "$OUT/$name.compile" | head -8
    fail=$((fail + 1)); failed+=("$name"); continue
  fi
  timeout "${CSHARPC_TEST_TIMEOUT:-20}" "$OUT/$name" > "$OUT/$name.stdout" 2> "$OUT/$name.stderr"
  got_code=$?
  if diff -u "$expected" "$OUT/$name.stdout" > "$OUT/$name.diff" && [[ "$got_code" == "$want_code" ]]; then
    echo "PASS $name"
    pass=$((pass + 1))
  else
    echo "FAIL $name (exit $got_code, expected $want_code)"
    sed 's/^/     /' "$OUT/$name.diff" | head -20
    fail=$((fail + 1)); failed+=("$name")
  fi
done
echo
echo "$pass passed, $fail failed${failed:+: ${failed[*]}}"
[[ $fail == 0 ]]
