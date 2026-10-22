#!/usr/bin/env bash
# Real-world program suite: larger idiomatic C# programs, differential-tested against .NET.
#
#   tests/programs.sh              build each tests/programs/<name>/Program.cs with csharpc at -O0
#                                  and -O2, run, diff against the recorded .NET output
#   tests/programs.sh --regen      record expected output with real .NET (`dotnet run Program.cs`)
#   tests/programs.sh --bench      also time csharpc -O2 vs .NET JIT (Release) per program
#   tests/programs.sh interp       only programs whose name contains "interp"
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROGRAMS="$ROOT/tests/programs"
DOTNET="${DOTNET:-dotnet}"
REGEN=0; BENCH=0; FILTER=""
for arg in "$@"; do
  case "$arg" in
    --regen) REGEN=1 ;;
    --bench) BENCH=1 ;;
    *) FILTER="$arg" ;;
  esac
done

names=()
for dir in "$PROGRAMS"/*/; do
  name="$(basename "$dir")"
  [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
  [[ -f "$dir/Program.cs" ]] && names+=("$name")
done

if [[ $REGEN == 1 ]]; then
  for name in "${names[@]}"; do
    dir="$PROGRAMS/$name"
    stdin_file=/dev/null; [[ -f "$dir/input.txt" ]] && stdin_file="$dir/input.txt"
    (cd "$dir" && "$DOTNET" run Program.cs -p:WarningLevel=0 2>/dev/null < "$stdin_file") \
      | grep -Ev '^\S+\([0-9]+,[0-9]+\): warning ' > "$dir/expected.txt"
    code=${PIPESTATUS[0]}
    if [[ $code != 0 ]]; then echo "$code" > "$dir/exitcode"; else rm -f "$dir/exitcode"; fi
    printf 'recorded %-24s exit=%s lines=%s\n' "$name" "$code" "$(wc -l < "$dir/expected.txt")"
  done
  exit 0
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

pass=0; fail=0; failed=()
printf '%-24s %-5s %-6s %9s %9s\n' program opt result compile_ms size_kb
for name in "${names[@]}"; do
  dir="$PROGRAMS/$name"
  want_code=0; [[ -f "$dir/exitcode" ]] && want_code="$(cat "$dir/exitcode")"
  stdin_file=/dev/null; [[ -f "$dir/input.txt" ]] && stdin_file="$dir/input.txt"
  for opt in -O0 -O2; do
    exe="$OUT/$name$opt"
    start=$(date +%s%N)
    if ! "${CSHARPC[@]}" "$opt" "$dir/Program.cs" -o "$exe" 2> "$OUT/$name.compile"; then
      printf '%-24s %-5s %-6s\n' "$name" "$opt" FAIL; sed 's/^/     /' "$OUT/$name.compile" | head -6
      fail=$((fail + 1)); failed+=("$name[$opt]"); continue
    fi
    ms=$(( ($(date +%s%N) - start) / 1000000 ))
    size=$(( $(stat -c %s "$exe") / 1024 ))
    bash -c 'ulimit -c 0; timeout 120 "$1" > "$2" 2> "$3" < "$4"' _ "$exe" "$OUT/$name.out" "$OUT/$name.err" "$stdin_file" 2>/dev/null
    code=$?
    if diff -u "$dir/expected.txt" "$OUT/$name.out" > "$OUT/$name.diff" && [[ $code == "$want_code" ]]; then
      printf '%-24s %-5s %-6s %9s %9s\n' "$name" "$opt" PASS "$ms" "$size"; pass=$((pass + 1))
    else
      printf '%-24s %-5s %-6s (exit %s, expected %s)\n' "$name" "$opt" FAIL "$code" "$want_code"
      sed 's/^/     /' "$OUT/$name.diff" | head -12; head -3 "$OUT/$name.err" | sed 's/^/     /'
      fail=$((fail + 1)); failed+=("$name[$opt]")
    fi
  done
  if [[ $BENCH == 1 && -x "$OUT/$name-O2" ]]; then
    TIMEFORMAT=%R
    t_native=$( { time "$OUT/$name-O2" > /dev/null 2>&1 < "$stdin_file"; } 2>&1 | tail -1 )
    (cd "$dir" && "$DOTNET" build Program.cs -c Release -p:WarningLevel=0 -o "$OUT/jit-$name" > /dev/null 2>&1)
    dll="$OUT/jit-$name/Program.dll"
    if [[ -f "$dll" ]]; then
      t_jit=$( { time "$DOTNET" "$dll" > /dev/null 2>&1 < "$stdin_file"; } 2>&1 | tail -1 )
      printf '    bench %-20s csharpc=%ss  jit=%ss\n' "$name" "$t_native" "$t_jit"
    fi
  fi
done
echo
echo "$pass passed, $fail failed${failed:+: ${failed[*]}}"
[[ $fail == 0 ]]
