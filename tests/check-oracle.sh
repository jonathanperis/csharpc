#!/usr/bin/env bash
# Checks the recorded expectations against real .NET on *this* machine.
#
#   tests/check-oracle.sh [-j N] [filter...]
#
# The expectations were recorded with .NET on Linux. On another OS the test scripts compare against
# what .NET prints there, derived from the recording (on Windows: every "\n" becomes "\r\n", because
# Console.WriteLine ends lines with Environment.NewLine). This script runs each sample, conformance
# program and real-world program with `dotnet run`, applies the same derivation, and reports every
# test where real .NET disagrees: those belong in tests/platform-skips.txt (with the reason), or the
# derivation is wrong. Tests already listed there are not run. Like the recordings, .NET runs with
# the invariant culture as its current culture (tests/oracle-hook). Slow (one `dotnet run` per test); meant for CI and for porting
# to a new OS. Exits non-zero if any unlisted test differs.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
JOBS=4
filters=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    -j) shift; JOBS="$1" ;;
    *) filters+=("$1") ;;
  esac
  shift
done
source "$ROOT/tests/platform.sh"

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

# One line per test: <name> <dir> <source file> <expected> <exitcode file> <stdin file>
list="$OUT/list"
: > "$list"
add() { # name dir file expected exitcode stdin
  local name="$1"
  if [[ ${#filters[@]} -gt 0 ]]; then
    local f hit=0
    for f in "${filters[@]}"; do [[ "$name" == *"$f"* ]] && hit=1; done
    [[ $hit == 1 ]] || return 0
  fi
  [[ -f "$4" ]] || return 0
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$@" >> "$list"
}
for src in "$ROOT"/tests/samples/*.cs; do
  name="$(basename "$src" .cs)"
  [[ -f "$ROOT/tests/samples/$name.diagnostics" ]] && continue
  add "$name" "$ROOT/tests/samples" "$name.cs" "$ROOT/tests/samples/$name.expected" "$ROOT/tests/samples/$name.exitcode" "$ROOT/tests/samples/$name.stdin"
done
while IFS= read -r src; do
  rel="${src#"$ROOT/tests/conformance/"}"; rel="${rel%.cs}"
  add "$rel" "$(dirname "$src")" "$(basename "$src")" "${src%.cs}.expected" "${src%.cs}.exitcode" /nonexistent
done < <(find "$ROOT/tests/conformance" -name '*.cs' | sort)
for dir in "$ROOT"/tests/programs/*/; do
  name="$(basename "$dir")"
  add "$name" "$dir" Program.cs "$dir/expected.txt" "$dir/exitcode" "$dir/input.txt"
done

check_one() {
  IFS=$'\t' read -r name dir file expected exitcode stdin <<< "$1"
  local safe="${name//\//_}"
  check_test "$name" "$dir" "$file" "$expected" "$exitcode" "$stdin" > "$OUT/$safe.report" 2>&1
}
check_test() {
  local name="$1" dir="$2" file="$3" expected="$4" exitcode="$5" stdin="$6"
  local safe="${name//\//_}"
  local reason; reason="$(platform_skip "$name")"
  [[ -n "$reason" ]] && { echo "LISTED $name ($reason)"; return; }
  local in=/dev/null; [[ -f "$stdin" ]] && in="$stdin"
  local want_code=0; [[ -f "$exitcode" ]] && want_code="$(platform_exit_code "$(cat "$exitcode")")"
  # A NAME.env test runs under its environment instead of the culture hook (tests/platform.sh).
  local test_env hook="$ORACLE_HOOK"
  read -ra test_env <<< "$(platform_env "$dir" "${file%.cs}")"
  [[ ${#test_env[@]} -gt 0 ]] && hook=""
  (cd "$dir" && env "${test_env[@]}" DOTNET_STARTUP_HOOKS="$hook" "$DOTNET" run "$file" -p:WarningLevel=0 -p:AllowUnsafeBlocks=true -p:StartupHookSupport=true 2>/dev/null < "$in") > "$OUT/$safe.raw"
  local code=$?
  grep -U -a -v -E '^[^ ]+\([0-9]+,[0-9]+\): warning ' < "$OUT/$safe.raw" > "$OUT/$safe.out"
  platform_expected < "$expected" > "$OUT/$safe.want"
  if cmp -s "$OUT/$safe.want" "$OUT/$safe.out" && [[ "$code" == "$want_code" ]]; then
    echo "SAME $name"
  else
    echo "DIFF $name (exit $code, derived $want_code)"
    diff -a <(cat -A "$OUT/$safe.want") <(cat -A "$OUT/$safe.out") | head -12 | sed 's/^/     /'
  fi
}
platform_oracle_hook || exit 1
export -f check_one check_test platform_expected platform_exit_code platform_skip platform_env
export OUT DOTNET HOST_OS TARGET_OS ROOT
cut -f1 "$list" | wc -l | xargs echo "tests:"
tr '\n' '\0' < "$list" | xargs -0 -P "$JOBS" -I{} bash -c 'check_one "$1"' _ {}
while IFS=$'\t' read -r name _; do cat "$OUT/${name//\//_}.report"; done < "$list" > "$OUT/report"
grep -v -E '^(SAME|LISTED)' "$OUT/report" || true
echo
echo "$(grep -c '^SAME' "$OUT/report") same, $(grep -c '^DIFF' "$OUT/report") different, $(grep -c '^LISTED' "$OUT/report") listed in tests/platform-skips.txt"
! grep -q '^DIFF' "$OUT/report"
