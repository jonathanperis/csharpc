#!/usr/bin/env bash
# Benchmark harness: csharpc (-O2, -O3) vs .NET JIT vs NativeAOT on benchmarks/programs/*.cs.
#
#   benchmarks/run.sh               run everything, print a Markdown table (also benchmarks/results-latest.md)
#   benchmarks/run.sh nbody         only programs whose name contains "nbody"
#   benchmarks/run.sh --regen       re-record benchmarks/programs/*.expected with `dotnet run` (the oracle)
#
# Every binary's stdout must match the .expected file recorded from real .NET, otherwise the row
# is marked MISMATCH. Times are wall-clock, best of $RUNS (default 3), in milliseconds.
# Environment: RUNS=3, NO_AOT=1 (skip NativeAOT), DOTNET=path/to/dotnet.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROGRAMS="$ROOT/benchmarks/programs"
DOTNET="${DOTNET:-dotnet}"
RUNS="${RUNS:-3}"
FILTER=""
REGEN=0
for arg in "$@"; do
  case "$arg" in
    --regen) REGEN=1 ;;
    *) FILTER="$arg" ;;
  esac
done
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [[ $REGEN == 1 ]]; then
  for src in "$PROGRAMS"/*.cs; do
    name="$(basename "$src" .cs)"
    [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
    echo "recording $name"
    (cd "$PROGRAMS" && "$DOTNET" run -c Release "$name.cs" -p:WarningLevel=0 2>/dev/null) > "$PROGRAMS/$name.expected"
  done
  exit 0
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed" >&2; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

now_ms() { echo $(( $(date +%s%N) / 1000000 )); }

# best_of <expected-file> <cmd...>: prints "wall/cpu" — best wall time and best CPU time
# (user+sys, which is robust against other load on the machine) in ms, or MISMATCH / FAIL.
best_of() {
  local expected="$1"; shift
  local best="" bestcpu="" t0 t1 dt cpu
  for ((i = 0; i < RUNS; i++)); do
    t0=$(now_ms)
    if ! { TIMEFORMAT="%3U %3S"; time "$@" > "$WORK/out" 2> "$WORK/err"; } 2> "$WORK/time"; then echo "FAIL"; return; fi
    t1=$(now_ms)
    if ! cmp -s "$WORK/out" "$expected"; then echo "MISMATCH"; return; fi
    dt=$((t1 - t0))
    cpu=$(awk '{printf "%d", ($1 + $2) * 1000}' "$WORK/time")
    if [[ -z "$best" || $dt -lt $best ]]; then best=$dt; fi
    if [[ -z "$bestcpu" || $cpu -lt $bestcpu ]]; then bestcpu=$cpu; fi
  done
  echo "$best/$bestcpu"
}

size_kb() { [[ -f "$1" ]] && echo "$(( $(stat -c %s "$1") / 1024 ))" || echo "-"; }

rows=()
for src in "$PROGRAMS"/*.cs; do
  name="$(basename "$src" .cs)"
  [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
  expected="$PROGRAMS/$name.expected"
  echo "== $name" >&2

  # csharpc: compile time is measured on the -O2 build (includes Roslyn, LLVM and the link).
  t0=$(now_ms)
  "${CSHARPC[@]}" -O2 "$src" -o "$WORK/$name-O2" 2> "$WORK/$name.log"
  ok2=$?
  ctime=$(( $(now_ms) - t0 ))
  "${CSHARPC[@]}" -O3 "$src" -o "$WORK/$name-O3" 2>> "$WORK/$name.log"
  ok3=$?
  if [[ $ok2 == 0 ]]; then o2=$(best_of "$expected" "$WORK/$name-O2"); else o2="COMPILE-ERROR"; ctime="-"; fi
  if [[ $ok3 == 0 ]]; then o3=$(best_of "$expected" "$WORK/$name-O3"); else o3="COMPILE-ERROR"; fi
  [[ $ok2 != 0 || $ok3 != 0 ]] && sed 's/^/   /' "$WORK/$name.log" | head -5 >&2
  cp "$WORK/$name-O2" "$WORK/$name-O2-stripped" 2>/dev/null && strip "$WORK/$name-O2-stripped" 2>/dev/null

  # .NET JIT: a Release build of the file-based app, run with `dotnet app.dll` (includes runtime startup).
  mkdir -p "$WORK/$name-src" && cp "$src" "$WORK/$name-src/"
  if (cd "$WORK/$name-src" && "$DOTNET" publish "$name.cs" -c Release -p:PublishAot=false -p:WarningLevel=0 -o "$WORK/$name-jit") > "$WORK/$name.jit.log" 2>&1; then
    jit=$(best_of "$expected" "$DOTNET" "$WORK/$name-jit/$name.dll")
  else
    jit="BUILD-FAIL"
  fi

  # NativeAOT (ILCompiler) — the closest existing analogue to csharpc: ahead-of-time, whole program.
  if [[ "${NO_AOT:-0}" == 1 ]]; then
    aot="skipped"
  elif (cd "$WORK/$name-src" && "$DOTNET" publish "$name.cs" -c Release -p:PublishAot=true -p:WarningLevel=0 -o "$WORK/$name-aot") > "$WORK/$name.aot.log" 2>&1; then
    aot=$(best_of "$expected" "$WORK/$name-aot/$name")
  else
    aot="BUILD-FAIL"
  fi

  rows+=("| $name | $o2 | $o3 | $jit | $aot | $ctime | $(size_kb "$WORK/$name-O2-stripped") | $(size_kb "$WORK/$name-aot/$name") |")
done

{
  echo "Times are wall/CPU (user+sys) in ms, best of $RUNS."
  echo
  echo "| benchmark | csharpc -O2 | csharpc -O3 | .NET JIT | NativeAOT | csharpc compile (ms) | csharpc size (KiB, stripped) | NativeAOT size (KiB) |"
  echo "|---|---:|---:|---:|---:|---:|---:|---:|"
  printf '%s\n' "${rows[@]}"
} | tee "$ROOT/benchmarks/results-latest.md"
