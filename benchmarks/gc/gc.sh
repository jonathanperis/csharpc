#!/usr/bin/env bash
# GC throughput, pause and RSS: csharpc vs the .NET JIT vs NativeAOT.
#
#   benchmarks/gc/gc.sh                 three runs of each engine, median row
#   GC_BENCH_RUNS=1 benchmarks/gc/gc.sh one run (a quick local check)
#   NO_AOT=1 benchmarks/gc/gc.sh        skip NativeAOT
#
# gclatency.cs prints a checksum on stdout (must match gclatency.expected, recorded from
# `dotnet run`) and one `gcbench` metrics line per phase on stderr. Peak RSS is GNU time's
# maximum resident set when /usr/bin/time exists, otherwise blank. The csharpc binary is the
# default serial marker. The ownership parallel marker is not in that binary; it is compiled
# with -DCSRT_GC_PARALLEL_MARK and was slower on a 4-core machine (docs/adr/0004).
#
# The workload always uses 4 threads (GC_BENCH_THREADS) so the checksum does not depend on
# the machine. Wall times are comparable within one machine; hosted runners are 4 vCPUs.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC="$ROOT/benchmarks/gc/gclatency.cs"
EXPECTED="$ROOT/benchmarks/gc/gclatency.expected"
DOTNET="${DOTNET:-dotnet}"
RUNS="${GC_BENCH_RUNS:-3}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 GC_BENCH_THREADS=4

if [[ ! -f "$EXPECTED" ]]; then
  echo "missing $EXPECTED (record it with: dotnet run -c Release $SRC > benchmarks/gc/gclatency.expected)" >&2
  exit 1
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed" >&2; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll" -c Release)
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# One metrics line per phase, plus peak RSS in KiB from GNU time when it is installed.
run_once() {
  local label="$1"; shift
  local rc=0
  if [[ -x /usr/bin/time ]]; then
    /usr/bin/time -v -o "$WORK/time.txt" "$@" > "$WORK/out" 2> "$WORK/err" || rc=$?
  else
    : > "$WORK/time.txt"
    "$@" > "$WORK/out" 2> "$WORK/err" || rc=$?
  fi
  if [[ $rc -ne 0 ]]; then
    echo "$label FAIL" >&2
    sed 's/^/   /' "$WORK/err" | head -20 >&2
    return 1
  fi
  if ! cmp -s "$WORK/out" "$EXPECTED"; then
    echo "$label MISMATCH" >&2
    echo "--- stdout ---" >&2
    cat "$WORK/out" >&2
    echo "--- stderr ---" >&2
    cat "$WORK/err" >&2
    return 1
  fi
  local rss
  rss="$(awk -F': ' '/Maximum resident set size/ { gsub(/[^0-9]/, "", $2); print $2 }' "$WORK/time.txt")"
  awk -v label="$label" -v rss="$rss" '
    /^gcbench / {
      phase = $2; sub(/^phase=/, "", phase)
      wall = ops = pause = cols = p50 = p99 = p999 = max = alloc = heap = "?"
      for (i = 1; i <= NF; i++) {
        split($i, a, "=")
        if (a[1] == "wall_ms") wall = a[2]
        else if (a[1] == "ops_s") ops = a[2]
        else if (a[1] == "pause_ms") pause = a[2]
        else if (a[1] == "collections") cols = a[2]
        else if (a[1] == "p50_us") p50 = a[2]
        else if (a[1] == "p99_us") p99 = a[2]
        else if (a[1] == "p999_us") p999 = a[2]
        else if (a[1] == "max_us") max = a[2]
        else if (a[1] == "alloc_mib") alloc = a[2]
        else if (a[1] == "heap_mib") heap = a[2]
      }
      printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n", label, phase, wall, ops, pause, cols, p50, p99, p999, max, alloc, heap
    }
    END {
      printf "%s\trss\t%s\n", label, rss
    }
  ' "$WORK/err"
  # csharpc's own pause histogram, when the runtime was asked to print it.
  grep '^csrt gc:' "$WORK/err" || true
}

median_of() {
  # median of the numbers on stdin
  sort -n | awk '{ a[NR] = $1 } END { if (NR == 0) { print "-" ; exit } print a[int((NR + 1) / 2)] }'
}

echo "building engines" >&2
t0=$(date +%s%N)
"${CSHARPC[@]}" -O2 "$SRC" -o "$WORK/gclatency-csharpc" 2> "$WORK/csharpc.log" || { cat "$WORK/csharpc.log" >&2; exit 1; }
compile_ms=$(( ($(date +%s%N) - t0) / 1000000 ))
cp "$WORK/gclatency-csharpc" "$WORK/gclatency-stripped"
strip "$WORK/gclatency-stripped" 2>/dev/null || true
csharpc_bytes=$(stat -c %s "$WORK/gclatency-stripped")

mkdir -p "$WORK/src" && cp "$SRC" "$WORK/src/"
(cd "$WORK/src" && "$DOTNET" publish gclatency.cs -c Release -p:PublishAot=false -p:WarningLevel=0 -o "$WORK/jit") > "$WORK/jit.log" 2>&1 || { cat "$WORK/jit.log" >&2; exit 1; }
if [[ "${NO_AOT:-0}" == 1 ]]; then
  aot_bytes="-"
else
  (cd "$WORK/src" && "$DOTNET" publish gclatency.cs -c Release -p:PublishAot=true -p:WarningLevel=0 -o "$WORK/aot") > "$WORK/aot.log" 2>&1 || { cat "$WORK/aot.log" >&2; exit 1; }
  aot_bytes=$(stat -c %s "$WORK/aot/gclatency")
fi

rows="$WORK/rows.tsv"
: > "$rows"
for ((i = 1; i <= RUNS; i++)); do
  echo "== run $i / $RUNS" >&2
  run_once "csharpc" "$WORK/gclatency-csharpc" >> "$rows" || exit 1
  run_once "jit" "$DOTNET" "$WORK/jit/gclatency.dll" >> "$rows" || exit 1
  if [[ "${NO_AOT:-0}" != 1 ]]; then
    run_once "aot" "$WORK/aot/gclatency" >> "$rows" || exit 1
  fi
done

{
  echo "GC benchmark, median of $RUNS. Threads=4. csharpc compile ${compile_ms} ms, stripped ${csharpc_bytes} bytes; NativeAOT ${aot_bytes} bytes."
  echo "op p50/p99/p999/max are mutator-stall microseconds (a stop-the-world pause lands in max; p99 stays near the operation cost when collections are rare). pause_ms is GC.GetTotalPauseDuration over the phase."
  echo
  echo "| engine | phase | wall ms | ops/s | pause ms | collections | p50 us | p99 us | p999 us | max us | alloc MiB | heap MiB | rss KiB |"
  echo "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
  for engine in csharpc jit aot; do
    [[ "$engine" == aot && "${NO_AOT:-0}" == 1 ]] && continue
    for phase in churn livebuild livemutate; do
      sel=$(awk -F'\t' -v e="$engine" -v p="$phase" '$1 == e && $2 == p { print }' "$rows")
      [[ -z "$sel" ]] && continue
      wall=$(echo "$sel" | awk -F'\t' '{ print $3 }' | median_of)
      ops=$(echo "$sel" | awk -F'\t' '{ print $4 }' | median_of)
      pause=$(echo "$sel" | awk -F'\t' '{ print $5 }' | median_of)
      cols=$(echo "$sel" | awk -F'\t' '{ print $6 }' | median_of)
      p50=$(echo "$sel" | awk -F'\t' '{ print $7 }' | median_of)
      p99=$(echo "$sel" | awk -F'\t' '{ print $8 }' | median_of)
      p999=$(echo "$sel" | awk -F'\t' '{ print $9 }' | median_of)
      max=$(echo "$sel" | awk -F'\t' '{ print $10 }' | median_of)
      alloc=$(echo "$sel" | awk -F'\t' '{ print $11 }' | median_of)
      heap=$(echo "$sel" | awk -F'\t' '{ print $12 }' | median_of)
      rss=$(awk -F'\t' -v e="$engine" '$1 == e && $2 == "rss" { print $3 }' "$rows" | median_of)
      echo "| $engine | $phase | $wall | $ops | $pause | $cols | $p50 | $p99 | $p999 | $max | $alloc | $heap | $rss |"
    done
  done
} | tee "$ROOT/benchmarks/gc/latest.md"
echo "csharpc compile_ms=$compile_ms stripped_bytes=$csharpc_bytes aot_bytes=$aot_bytes"
