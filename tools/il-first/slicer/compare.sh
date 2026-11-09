#!/usr/bin/env bash
# One program compiled twice, with the corelib's shadow and with a slice of the framework's IL (issue #191 part B):
#
#   tools/il-first/slicer/compare.sh <program.cs> <slice.dll> [runs]
#
# Prints binary size (stripped), compile CPU time, run CPU time (median) and whether the outputs are byte-identical.
# Machine-load noise is large on a shared VPS: sizes and CPU seconds are meaningful, wall clock is not.
set -uo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
export PATH="$HOME/.dotnet11:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
src="$(realpath "$1")"; slice="$(realpath "$2")"; runs="${3:-5}"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
csc="$root/src/Csharpc/bin/Release/net11.0/csharpc.dll"
[ -f "$csc" ] || dotnet build "$root/src/Csharpc" -c Release -m:1 > /dev/null
TIMEFORMAT='%U %S'
one() { # label, extra args...
  local label="$1"; shift
  local t; t=$( { time dotnet "$csc" -O2 "$src" -o "$work/$label" "$@" > "$work/$label.log" 2>&1; } 2>&1 )
  [ -f "$work/$label" ] || { echo "$label: compile failed"; head -3 "$work/$label.log"; return 1; }
  local cpu; cpu=$(echo "$t" | awk '{printf "%.1f", $1 + $2}')
  cp "$work/$label" "$work/$label.s" && strip "$work/$label.s"
  local cpus=()
  for _ in $(seq 1 "$runs"); do
    t=$( { time "$work/$label" > "$work/$label.out" 2>&1; } 2>&1 )
    cpus+=("$(echo "$t" | awk '{printf "%d", ($1 + $2) * 1000}')")
  done
  local med; med=$(printf '%s\n' "${cpus[@]}" | sort -n | sed -n "$(( (runs + 1) / 2 ))p")
  echo "$label: $(stat -c %s "$work/$label.s") bytes stripped, compile $cpu cpu-s, run $med cpu-ms (median of $runs), output $(md5sum < "$work/$label.out" | cut -c1-8)"
}
one shadow
CSHARPC_UPSTREAM_MAP="${slice%.dll}.map" one slice -r "$slice"
