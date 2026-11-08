#!/usr/bin/env bash
# A/B harness for the "framework IL first" policy (docs/adr/0023-framework-il-first.md).
#
#   tools/il-first/ab.sh [options] <assembly[,assembly...]> <sample>...
#
# For one shared-framework library (an assembly name such as System.Text.Json) and a list of samples from
# tests/samples (exact names, or substrings: "json" selects every sample whose name contains it), every sample is
# compiled twice at -O2,
#   shadow  the default: the corelib's hand-written port of the library
#   IL      CSHARPC_IL_FIRST=<assembly>: the shadows are dropped, the library is compiled from the shared framework's IL
# both programs are run, and their stdout and exit code are compared with the recorded .expected / .exitcode (real
# .NET is the oracle). Per sample and route the table reports: pass/fail, stripped binary size, csharpc compile
# time (wall and CPU, children included), and the program's run time (median wall of --runs runs).
# A sample argument may also be the path of a .cs program outside tests/samples (tools/il-first/bench/*.cs, throughput
# programs that print a checksum): real .NET (`dotnet run`) records its stdout once as the oracle.
# Output: a Markdown table, a summary over the samples both routes pass, and the blockers of the IL route (the
# distinct compiler diagnostics with the number of samples that hit each).
#
# Options:
#   --runs N        run-time repetitions (default 5; the median is reported)
#   --opt -O2       optimization level (default -O2)
#   --out FILE      also write the report to FILE
#   --keep          keep the work directory (executables, logs) and print its path
#   --no-warm       don't compile a hello world per route first (the first IL compile otherwise pays for building
#                   the cached corelib and framework index, which would land in the first sample's compile time)
#
# Environment: DOTNET, CSHARPC_FLAGS (extra compiler flags), CSHARPC_TEST_TIMEOUT (seconds per run, default 90).
# Samples with a .diagnostics file (programs csharpc must reject) are skipped. Linux/macOS hosts; run niced on a
# shared machine, e.g.  nice -n 19 ionice -c3 tools/il-first/ab.sh System.Text.Json json
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLES="$ROOT/tests/samples"
DOTNET="${DOTNET:-dotnet}"
RUNS=5; OPT=-O2; OUTFILE=""; KEEP=0; WARM=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs) shift; RUNS="$1" ;;
    --opt) shift; OPT="$1" ;;
    --out) shift; OUTFILE="$1" ;;
    --keep) KEEP=1 ;;
    --no-warm) WARM=0 ;;
    -h|--help) sed -n '2,/^set -uo/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit 0 ;;
    --*) echo "unknown option $1" >&2; exit 2 ;;
    *) break ;;
  esac
  shift
done
[[ $# -ge 2 ]] || { echo "usage: ab.sh [options] <assembly[,assembly...]> <sample>..." >&2; exit 2; }
[[ "$RUNS" =~ ^[1-9][0-9]*$ ]] || { echo "bad --runs '$RUNS'" >&2; exit 2; }
ASSEMBLY="$1"; shift

source "$ROOT/tests/platform.sh"

selected=()
declare -A PATHS   # name -> path, for programs outside tests/samples (tools/il-first/bench/*.cs)
for pattern in "$@"; do
  if [[ "$pattern" == *.cs && -f "$pattern" && ! -f "$SAMPLES/$pattern" ]]; then
    name="$(basename "$pattern" .cs)"; PATHS[$name]="$(cd "$(dirname "$pattern")" && pwd)/$(basename "$pattern")"; selected+=("$name"); continue
  fi
  if [[ -f "$SAMPLES/$pattern.cs" ]]; then selected+=("$pattern"); continue; fi
  found=0
  for f in "$SAMPLES"/*"$pattern"*.cs; do
    [[ -f "$f" ]] || continue
    selected+=("$(basename "$f" .cs)"); found=1
  done
  [[ $found == 1 ]] || { echo "no sample matches '$pattern'" >&2; exit 2; }
done
# de-duplicate, keep order
mapfile -t selected < <(printf '%s\n' "${selected[@]}" | awk '!seen[$0]++')

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed" >&2; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")

WORK="$(mktemp -d)"
cleanup() { if [[ $KEEP == 1 ]]; then echo "work directory kept: $WORK" >&2; else rm -rf "$WORK"; fi; }
trap cleanup EXIT

# `time` of the bash keyword with children included: "wall user+sys" in seconds.
TIMEFORMAT='%R %U %S'
file_size() { stat -c %s "$1" 2>/dev/null || stat -f %z "$1"; }
median() { sort -n | awk '{a[NR]=$1} END {if (NR==0) print "-"; else print (NR%2 ? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2)}'; }
ms() { awk -v s="$1" 'BEGIN {printf "%d", s*1000 + 0.5}'; }
route_env() { [[ "$1" == il ]] && echo "CSHARPC_IL_FIRST=$ASSEMBLY" || echo "CSHARPC_IL_FIRST="; }

# results[route:name:field]
declare -A R
compile() { # route name src exe
  local route="$1" name="$2" src="$3" exe="$4" t
  # shellcheck disable=SC2046
  t="$( { time env $(route_env "$route") "${CSHARPC[@]}" "${TARGET_FLAGS[@]}" "$OPT" ${CSHARPC_FLAGS:-} "$src" "${CULTURE_PIN[@]}" -o "$exe" > /dev/null 2> "$WORK/$route.$name.compile"; } 2>&1 )"
  local rc=$?
  read -r wall user sys <<< "$(tail -1 <<< "$t")"
  R[$route:$name:wall]="$wall"; R[$route:$name:cpu]="$(awk -v u="$user" -v s="$sys" 'BEGIN {printf "%.2f", u+s}')"
  return $rc
}

if [[ $WARM == 1 ]]; then
  printf 'class P { static void Main() { System.Console.WriteLine("hi"); } }\n' > "$WORK/hello.cs"
  for route in shadow il; do
    # (a program that doesn't use the library still builds the route's corelib assembly and framework index)
    env $(route_env $route) "${CSHARPC[@]}" "${TARGET_FLAGS[@]}" "$OPT" ${CSHARPC_FLAGS:-} "$WORK/hello.cs" -o "$WORK/hello.$route" > /dev/null 2>&1 || true
  done
  rm -f "$WORK"/hello.*
fi

for name in "${selected[@]}"; do
  src="$SAMPLES/$name.cs"
  stdin_file=/dev/null; test_env=(); want_code=0
  if [[ -n "${PATHS[$name]:-}" ]]; then
    # A program outside tests/samples: real .NET's stdout is the oracle, recorded here once.
    src="${PATHS[$name]}"
    (cd "$(dirname "$src")" && "$DOTNET" run "$(basename "$src")" -p:WarningLevel=0 -p:NuGetAudit=false 2> /dev/null) > "$WORK/$name.want"
  else
    if [[ -f "$SAMPLES/$name.diagnostics" ]]; then R[skip:$name]="rejected-program sample"; continue; fi
    [[ -f "$SAMPLES/$name.expected" ]] || { R[skip:$name]="no .expected"; continue; }
    reason="$(platform_skip "$name")"
    if [[ -n "$reason" ]] && ! platform_is_live "$reason"; then R[skip:$name]="platform skip: $reason"; continue; fi
    [[ -f "$SAMPLES/$name.stdin" ]] && stdin_file="$SAMPLES/$name.stdin"
    read -ra test_env <<< "$(platform_env "$SAMPLES" "$name")"
    [[ -f "$SAMPLES/$name.exitcode" ]] && want_code="$(cat "$SAMPLES/$name.exitcode")"
    platform_expected < "$SAMPLES/$name.expected" > "$WORK/$name.want"
  fi
  for route in shadow il; do
    exe="$WORK/$route.$name"
    if ! compile $route "$name" "$src" "$exe"; then
      R[$route:$name:result]="FAIL compile"
      continue
    fi
    cp "$exe" "$exe.stripped" && strip "$exe.stripped" 2>/dev/null
    R[$route:$name:size]="$(file_size "$exe.stripped")"
    times=()
    result=PASS
    for ((i = 0; i < RUNS; i++)); do
      t="$( { time env "${test_env[@]}" bash -c 'ulimit -c 0 2>/dev/null; t="$1" out="$2" in="$3"; shift 3; timeout "$t" "$@" > "$out" 2>/dev/null < "$in"' _ \
        "${CSHARPC_TEST_TIMEOUT:-90}" "$WORK/$route.$name.out" "$stdin_file" "$exe" 2>/dev/null; } 2>&1 )"
      got_code=$?
      if (( i == 0 )); then
        if [[ "$got_code" != "$want_code" ]] || ! cmp -s "$WORK/$name.want" "$WORK/$route.$name.out"; then result="FAIL output"; fi
      fi
      read -r wall _ <<< "$(tail -1 <<< "$t")"
      times+=("$wall")
    done
    R[$route:$name:result]="$result"
    R[$route:$name:run]="$(printf '%s\n' "${times[@]}" | median)"
    rm -f "$exe"
  done
done

fmt_kb() { [[ -n "${1:-}" ]] && awk -v b="$1" 'BEGIN {printf "%.0f", b/1024}' || echo "-"; }
fmt_ms() { [[ -n "${1:-}" && "$1" != "-" ]] && ms "$1" || echo "-"; }
cell() { # route name  -> "result | KB | wall/cpu s | run ms"
  local k="$1:$2"
  echo "${R[$k:result]:-n/a} | $(fmt_kb "${R[$k:size]:-}") | ${R[$k:wall]:--} / ${R[$k:cpu]:--} | $(fmt_ms "${R[$k:run]:-}")"
}

report() {
  echo "### A/B: $ASSEMBLY, shadow vs framework IL ($OPT, run time = median of $RUNS)"
  echo
  echo "| Sample | Shadow | KB | compile wall / cpu s | run ms | IL | KB | compile wall / cpu s | run ms |"
  echo "|---|---|---:|---:|---:|---|---:|---:|---:|"
  local both=0 s_kb=0 i_kb=0 s_wall=0 i_wall=0 s_cpu=0 i_cpu=0 s_run=0 i_run=0 s_pass=0 i_pass=0 counted=0 skipped=()
  for name in "${selected[@]}"; do
    if [[ -n "${R[skip:$name]:-}" ]]; then skipped+=("$name (${R[skip:$name]})"); continue; fi
    echo "| $name | $(cell shadow "$name") | $(cell il "$name") |"
    counted=$((counted + 1))
    [[ "${R[shadow:$name:result]:-}" == PASS ]] && s_pass=$((s_pass + 1))
    [[ "${R[il:$name:result]:-}" == PASS ]] && i_pass=$((i_pass + 1))
    if [[ "${R[shadow:$name:result]:-}" == PASS && "${R[il:$name:result]:-}" == PASS ]]; then
      both=$((both + 1))
      s_kb=$((s_kb + ${R[shadow:$name:size]})); i_kb=$((i_kb + ${R[il:$name:size]}))
      s_wall=$(awk -v a="$s_wall" -v b="${R[shadow:$name:wall]}" 'BEGIN {print a+b}'); i_wall=$(awk -v a="$i_wall" -v b="${R[il:$name:wall]}" 'BEGIN {print a+b}')
      s_cpu=$(awk -v a="$s_cpu" -v b="${R[shadow:$name:cpu]}" 'BEGIN {print a+b}'); i_cpu=$(awk -v a="$i_cpu" -v b="${R[il:$name:cpu]}" 'BEGIN {print a+b}')
      s_run=$(awk -v a="$s_run" -v b="${R[shadow:$name:run]}" 'BEGIN {print a+b}'); i_run=$(awk -v a="$i_run" -v b="${R[il:$name:run]}" 'BEGIN {print a+b}')
    fi
  done
  echo
  echo "Samples: $counted measured, shadow passes $s_pass, IL passes $i_pass, both pass $both."
  if (( both > 0 )); then
    echo
    echo "Over the $both samples both routes pass (sums; ratio IL / shadow):"
    echo
    echo "| Metric | Shadow | IL | IL / shadow |"
    echo "|---|---:|---:|---:|"
    awk -v a="$s_kb" -v b="$i_kb" 'BEGIN {printf "| stripped binary, KB | %.0f | %.0f | %.2f |\n", a/1024, b/1024, b/a}'
    awk -v a="$s_wall" -v b="$i_wall" 'BEGIN {printf "| compile wall, s | %.1f | %.1f | %.2f |\n", a, b, b/a}'
    awk -v a="$s_cpu" -v b="$i_cpu" 'BEGIN {printf "| compile cpu, s | %.1f | %.1f | %.2f |\n", a, b, b/a}'
    awk -v a="$s_run" -v b="$i_run" 'BEGIN {printf "| run time, ms | %.0f | %.0f | %.2f |\n", a*1000, b*1000, (a>0 ? b/a : 0)}'
  fi
  if ((${#skipped[@]} > 0)); then echo; printf 'Skipped: %s\n' "$(printf '%s; ' "${skipped[@]}")"; fi

  # The blockers of the IL route: every distinct diagnostic of the failed compiles, normalized, with the samples hitting it.
  local blockers="$WORK/blockers.txt"; : > "$blockers"
  for name in "${selected[@]}"; do
    [[ "${R[il:$name:result]:-}" == "FAIL compile" ]] || continue
    # "path(1,1): error CSLLVM0002: 'X' has no implementation ... [in 'Y' (A.dll) IL_000C: call Z]" -> "CSLLVM0002: 'X' has no implementation ..."
    grep -E 'error|warning CSLLVM' "$WORK/il.$name.compile" | grep -v 'construct(s) not supported' \
      | sed -E 's/^.*(error|warning) (CS[A-Z]*[0-9]+): /\2: /; s/ \[in .*$//; s/^csharpc: error : //' | sort -u | sed "s|\$| ::: $name|" >> "$blockers"
  done
  if [[ -s "$blockers" ]]; then
    echo
    echo "### Blockers of the IL route"
    echo
    echo "| Samples | Diagnostic |"
    echo "|---:|---|"
    awk -F' ::: ' '{n[$1]++; s[$1]=s[$1] (s[$1] ? ", " : "") $2} END {for (d in n) printf "%d\t%s\t%s\n", n[d], d, s[d]}' "$blockers" \
      | sort -t$'\t' -k1,1nr -k2,2 | awk -F'\t' '{printf "| %d | `%s` (%s) |\n", $1, $2, $3}'
  fi
  # Output differences (not compile errors) of either route: the first lines of the diff.
  for route in shadow il; do
    for name in "${selected[@]}"; do
      if [[ "${R[$route:$name:result]:-}" == "FAIL output" ]]; then
        echo; echo "**$route route, $name: output differs from the recording**"; echo; echo '```'
        diff "$WORK/$name.want" "$WORK/$route.$name.out" | head -8; echo '```'
      fi
    done
  done
}

if [[ -n "$OUTFILE" ]]; then report | tee "$OUTFILE"; else report; fi
