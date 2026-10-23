#!/usr/bin/env bash
# Executed instructions (valgrind callgrind) of every benchmark compiled by csharpc at -O2.
# Unlike wall-clock times this is deterministic, so CI runs are comparable across machines and
# runs; it doesn't cover the .NET JIT or NativeAOT (a JIT's count is mostly the JIT itself).
#   benchmarks/instructions.sh [filter]     prints a Markdown table
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROGRAMS="$ROOT/benchmarks/programs"
DOTNET="${DOTNET:-dotnet}"
FILTER="${1:-}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
command -v valgrind > /dev/null || { echo "valgrind is required" >&2; exit 1; }
"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed" >&2; exit 1; }
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
echo "| benchmark | csharpc -O2 instructions |"
echo "|---|---:|"
for src in "$PROGRAMS"/*.cs; do
  name="$(basename "$src" .cs)"
  [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
  if ! "$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll" -c Release -O2 "$src" -o "$WORK/$name" 2> "$WORK/$name.log"; then
    echo "| $name | compile error |"; continue
  fi
  stdin="$PROGRAMS/$name.stdin"; [[ -f "$stdin" ]] || stdin=/dev/null
  count=$(cd "$WORK" && valgrind --tool=callgrind --callgrind-out-file=/dev/null "./$name" < "$stdin" 2>&1 >/dev/null \
    | sed -n 's/.*Collected : \([0-9]*\).*/\1/p')
  printf '| %s | %s |\n' "$name" "${count:-?}"
done
