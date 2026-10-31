#!/usr/bin/env bash
# M6 step 5 (docs/06-roadmap.md "M6 — Bootstrapping"): csharpc compiled by csharpc.
#
#   1. builds csharpc (Release) and compiles its own csharpc.dll from IL with it (`csharpc csharpc.dll`),
#      Roslyn and the LLVM C API P/Invokes included, recording the compile's time and peak memory;
#   2. compiles hello world and a few samples with the native compiler and with the managed one, and
#      checks that both compilers print the same diagnostics and that their programs print the same output.
#
# The native compiler lands next to csharpc.dll (src/Csharpc/bin/Release/net11.0/csharpc-native), where it
# finds the corelib and runtime sources the managed one ships with.
#
# Usage: tools/bootstrap/selfhost.sh [-O0|-O2] [sample-name-filter ...]
# Environment: CSHARPC_THREADS (compile threads), SELFHOST_SKIP_COMPILE=1 (reuse an existing csharpc-native).
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
OPT="-O0"
if [[ "${1:-}" == -O* ]]; then OPT="$1"; shift; fi
SAMPLES=("$@")
[[ ${#SAMPLES[@]} -eq 0 ]] && SAMPLES=(01_hello 02_recursion 05_strings 06_classes 25_records 40_try_catch 101_dictionary 161_stringbuilder)
BIN="$ROOT/src/Csharpc/bin/Release/net11.0"
NATIVE="$BIN/csharpc-native"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -m:1 -v quiet -nologo > "$WORK/build.log" 2>&1 || { cat "$WORK/build.log"; echo "FAIL: compiler build"; exit 1; }

if [[ -z "${SELFHOST_SKIP_COMPILE:-}" ]]; then
  echo "== csharpc compiling csharpc.dll ($OPT)"
  start=$(date +%s)
  if command -v /usr/bin/time > /dev/null; then
    /usr/bin/time -v "$DOTNET" "$BIN/csharpc.dll" "$BIN/csharpc.dll" "$OPT" -v -o "$NATIVE" > "$WORK/selfhost.log" 2>&1; rc=$?
  else
    "$DOTNET" "$BIN/csharpc.dll" "$BIN/csharpc.dll" "$OPT" -v -o "$NATIVE" > "$WORK/selfhost.log" 2>&1; rc=$?
  fi
  end=$(date +%s)
  grep -E " ms  .*MB alloc|^             |Maximum resident|Elapsed \(wall" "$WORK/selfhost.log" | cut -c1-400
  grep -E "error" "$WORK/selfhost.log" | grep -v "^ " | sort | uniq -c | sort -rn | head -40
  echo "self-compile: exit $rc, $((end - start)) s"
  cp "$WORK/selfhost.log" "${SELFHOST_LOG:-$ROOT/selfhost.log}" 2>/dev/null || true
  [[ $rc -eq 0 && -x "$NATIVE" ]] || { echo "FAIL: csharpc could not compile itself"; exit 1; }
  ls -la "$NATIVE"
fi

pass=0; fail=0
for name in "${SAMPLES[@]}"; do
  src="$ROOT/tests/samples/$name.cs"
  [[ -f "$src" ]] || { echo "FAIL $name: no such sample"; fail=$((fail + 1)); continue; }
  "$DOTNET" "$BIN/csharpc.dll" "$src" "$OPT" -o "$WORK/$name.managed" > "$WORK/$name.managed.log" 2>&1; managed_rc=$?
  "$NATIVE" "$src" "$OPT" -o "$WORK/$name.native" > "$WORK/$name.native.log" 2>&1; native_rc=$?
  if [[ $managed_rc != "$native_rc" ]] || ! diff -q "$WORK/$name.managed.log" "$WORK/$name.native.log" > /dev/null; then
    echo "FAIL $name: the compilers differ (managed exit $managed_rc, native exit $native_rc)"
    diff "$WORK/$name.managed.log" "$WORK/$name.native.log" | head -10 | sed 's/^/     /'
    fail=$((fail + 1)); continue
  fi
  if [[ $managed_rc != 0 ]]; then echo "FAIL $name: both compilers failed"; head -5 "$WORK/$name.managed.log" | sed 's/^/     /'; fail=$((fail + 1)); continue; fi
  input=/dev/null; [[ -f "$ROOT/tests/samples/$name.stdin" ]] && input="$ROOT/tests/samples/$name.stdin"
  "$WORK/$name.managed" < "$input" > "$WORK/$name.managed.out" 2>&1; mrc=$?
  "$WORK/$name.native" < "$input" > "$WORK/$name.native.out" 2>&1; nrc=$?
  if [[ $mrc == "$nrc" ]] && diff -q "$WORK/$name.managed.out" "$WORK/$name.native.out" > /dev/null; then
    echo "PASS $name"; pass=$((pass + 1))
  else
    echo "FAIL $name: the programs differ (exit $mrc vs $nrc)"; diff "$WORK/$name.managed.out" "$WORK/$name.native.out" | head -10 | sed 's/^/     /'
    fail=$((fail + 1))
  fi
done
echo "$pass passed, $fail failed"
[[ $fail -eq 0 ]]
