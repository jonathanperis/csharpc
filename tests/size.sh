#!/usr/bin/env bash
# Binary-size budgets: "smaller than .NET" is part of the standing goal (docs/06-roadmap.md), so a
# size regression fails like a wrong output. Each tests/size/<name>.cs is compiled with csharpc's
# defaults (-O2, stripped) and its size compared with the budget in tests/size/budgets.txt for the
# platform ("<platform> <name> <max bytes>"; headroom: one page or 3 %). Platforms without budgets
# only print the sizes.
#
#   tests/size.sh            check the budgets
#   tests/size.sh --print    print "<platform> <name> <bytes>" lines (to set budgets: size + max(4 KiB, 3 %))
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tests/platform.sh"
DOTNET="${DOTNET:-dotnet}"
PRINT=0; [[ "${1:-}" == --print ]] && PRINT=1
PLATFORM="$TARGET_OS-$(uname -m)"
[[ $TARGET_OS == wasi ]] && PLATFORM=wasi-wasm32

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")
OUT="$(mktemp -d)"; trap 'rm -rf "$OUT"' EXIT

failed=0; checked=0
[[ $PRINT == 0 ]] && printf '%-14s %10s %10s\n' program bytes budget
for src in "$ROOT"/tests/size/*.cs; do
  name="$(basename "$src" .cs)"
  exe="$OUT/$name$EXE_SUFFIX"
  if ! "${CSHARPC[@]}" "${TARGET_FLAGS[@]}" "$src" -o "$exe" > "$OUT/$name.log" 2>&1; then
    echo "FAIL $name: compile error"; cat "$OUT/$name.log"; failed=1; continue
  fi
  size=$(wc -c < "$exe" | tr -d ' ')
  if [[ $PRINT == 1 ]]; then echo "$PLATFORM $name $size"; continue; fi
  budget=$(awk -v p="$PLATFORM" -v n="$name" '$1 == p && $2 == n { print $3 }' "$ROOT/tests/size/budgets.txt")
  printf '%-14s %10s %10s' "$name" "$size" "${budget:--}"
  if [[ -n "$budget" ]]; then
    checked=$((checked + 1))
    if (( size > budget )); then printf '  OVER by %d bytes' $((size - budget)); failed=1; fi
  fi
  echo
done
[[ $PRINT == 1 ]] && exit 0
echo "$checked budgets checked on $PLATFORM"
exit $failed
