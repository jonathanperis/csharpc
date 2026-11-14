#!/usr/bin/env bash
# Binary-size budgets: "smaller than .NET" is part of the standing goal (docs/06-roadmap.md), so a
# size regression fails like a wrong output. Each tests/size/<name>.cs is compiled with csharpc's
# defaults (-O2, stripped) and its size compared with the budget for the platform in its own file,
# tests/size/budgets/<name>.txt (one file per program, so PRs that move different programs' budgets
# never touch the same file; the reasons for a program's numbers are comments in its file). Platforms
# without a budget only print the sizes. Budget files:
#   # <platform> <program> <max bytes, stripped -O2>: measured size + max(4 KiB, 3 %), rounded up to 1 KiB.
#   # File sizes move in whole pages (segments are page-aligned), so the headroom is at least one page.
#   # Lower a budget when a change makes a program smaller; raising one needs a reason in the commit.
# A new size program adds tests/size/<name>.cs and tests/size/budgets/<name>.txt; a line whose program is
# not the file's name, or a budget file without its program, is an error.
# On Linux the same executables must also keep the default hardening and a segment layout valid for every page size of the architecture (tools/checksec.sh --check): the
# cheap way to shrink an ELF file is to drop separate code or the 64 KiB alignment AArch64 needs.
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

failed=0; checked=0; exes=()
BUDGETS="$ROOT/tests/size/budgets"
for f in "$BUDGETS"/*.txt; do
  n="$(basename "$f" .txt)"
  [[ -f "$ROOT/tests/size/$n.cs" ]] || { echo "FAIL tests/size/budgets/$n.txt: no tests/size/$n.cs"; failed=1; }
  bad=$(awk -v n="$n" '!/^#/ && NF && (NF != 3 || $2 != n)' "$f")
  [[ -z "$bad" ]] || { echo "FAIL tests/size/budgets/$n.txt: lines not of the form \"<platform> $n <bytes>\":"; echo "$bad"; failed=1; }
done
[[ $PRINT == 0 ]] && printf '%-14s %10s %10s\n' program bytes budget
for src in "$ROOT"/tests/size/*.cs; do
  name="$(basename "$src" .cs)"
  exe="$OUT/$name$EXE_SUFFIX"
  if ! ${CSHARPC[@]+"${CSHARPC[@]}"} ${TARGET_FLAGS[@]+"${TARGET_FLAGS[@]}"} "$src" -o "$exe" > "$OUT/$name.log" 2>&1; then
    echo "FAIL $name: compile error"; cat "$OUT/$name.log"; failed=1; continue
  fi
  exes+=("$exe")
  size=$(wc -c < "$exe" | tr -d ' ')
  if [[ $PRINT == 1 ]]; then echo "$PLATFORM $name $size"; continue; fi
  budget=""
  [[ -f "$BUDGETS/$name.txt" ]] && budget=$(awk -v p="$PLATFORM" -v n="$name" '$1 == p && $2 == n { print $3 }' "$BUDGETS/$name.txt")
  printf '%-14s %10s %10s' "$name" "$size" "${budget:--}"
  if [[ -n "$budget" ]]; then
    checked=$((checked + 1))
    if (( size > budget )); then printf '  OVER by %d bytes' $((size - budget)); failed=1; fi
  fi
  echo
done
[[ $PRINT == 1 ]] && exit 0
echo "$checked budgets checked on $PLATFORM"
if [[ $TARGET_OS == linux && ${#exes[@]} -gt 0 ]]; then
  "$ROOT/tools/checksec.sh" --check ${exes[@]+"${exes[@]}"} || failed=1
fi
exit $failed
