#!/usr/bin/env bash
# M8 harness (docs/06-roadmap.md): the TechEmpower plaintext/JSON app (benchmarks/techempower/app)
# built with NativeAOT, with csharpc and on the JIT, checked with TechEmpower's test requests and
# loaded with wrk the way TechEmpower loads it.
#
#   benchmarks/techempower/run.sh [--out DIR] [--only aot,csharpc,jit] [--no-load]
#
# Writes DIR/results.md (Markdown table: requests/s, latency, peak RSS, binary size, start-up),
# DIR/<build>.verify.txt (normalized responses; csharpc's must equal NativeAOT's), DIR/<build>.wrk.txt,
# and, when csharpc can't build the app yet, DIR/csharpc-blockers.md (its diagnostics ranked by the
# number of methods each item stops). Linux only (taskset, /proc). Heavy: meant for CI
# (.github/workflows/techempower.yml); locally use --no-load.
#
# Environment: DURATION=15 (seconds per load run), WARMUP=5, PORT=8080, CSHARPC_FLAGS="-O2",
# PLAINTEXT_CONNECTIONS="256 1024", JSON_CONNECTIONS="16 64 256", PIPELINE=16, DOTNET=dotnet.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$ROOT/benchmarks/techempower"
APP="$HERE/app/TechEmpower.csproj"
DOTNET="${DOTNET:-dotnet}"
OUT="$PWD/techempower-results"
ONLY="aot,csharpc,jit"
LOAD=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT="$2"; shift ;;
    --only) ONLY="$2"; shift ;;
    --no-load) LOAD=0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
DURATION="${DURATION:-15}"
WARMUP="${WARMUP:-5}"
PORT="${PORT:-8080}"
PIPELINE="${PIPELINE:-16}"
PLAINTEXT_CONNECTIONS="${PLAINTEXT_CONNECTIONS:-256 1024}"
JSON_CONNECTIONS="${JSON_CONNECTIONS:-16 64 256}"
CSHARPC_FLAGS="${CSHARPC_FLAGS:--O2}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p "$OUT"
WORK="$(mktemp -d)"
SERVER_PID=""
cleanup() { [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null; rm -rf "$WORK"; }
trap cleanup EXIT
wants() { [[ ",$ONLY," == *",$1,"* ]]; }

# The server gets half the CPUs, wrk the other half (one machine; TechEmpower uses separate ones).
CPUS=$(nproc)
SERVER_CPUS="0-$(( CPUS / 2 - 1 ))"
CLIENT_CPUS="$(( CPUS / 2 ))-$(( CPUS - 1 ))"
(( CPUS < 2 )) && SERVER_CPUS=0 CLIENT_CPUS=0
CLIENT_THREADS=$(( CPUS - CPUS / 2 ))

declare -A BIN STATUS
# ---- builds ------------------------------------------------------------------------------------
if wants aot; then
  echo "== NativeAOT publish"
  t0=$(date +%s%N)
  if "$DOTNET" publish "$APP" -c Release -o "$WORK/aot" -p:PublishAot=true > "$OUT/aot.build.log" 2>&1; then
    BIN[aot]="$WORK/aot/TechEmpower"; STATUS[aot]="built in $(( ($(date +%s%N) - t0) / 1000000000 )) s"
  else STATUS[aot]="build failed"; tail -30 "$OUT/aot.build.log"; fi
fi
if wants jit; then
  echo "== JIT build (framework-dependent, ReadyToRun off: TechEmpower's default aspnetcore entries)"
  if "$DOTNET" publish "$APP" -c Release -o "$WORK/jit" -p:PublishAot=false > "$OUT/jit.build.log" 2>&1; then
    BIN[jit]="$WORK/jit/TechEmpower"; STATUS[jit]="built"
  else STATUS[jit]="build failed"; tail -30 "$OUT/jit.build.log"; fi
fi
if wants csharpc; then
  echo "== csharpc build"
  "$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > "$OUT/csharpc-compiler.build.log" 2>&1 || { echo "compiler build failed" >&2; tail -30 "$OUT/csharpc-compiler.build.log"; exit 1; }
  t0=$(date +%s%N)
  # shellcheck disable=SC2086
  # -v: the framework assemblies taken from IL and the time and memory of each phase go to the log.
  if "$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll" "$APP" -c Release $CSHARPC_FLAGS -v -o "$WORK/csharpc/TechEmpower" > "$OUT/csharpc.build.log" 2>&1; then
    BIN[csharpc]="$WORK/csharpc/TechEmpower"; STATUS[csharpc]="built in $(( ($(date +%s%N) - t0) / 1000000000 )) s"
  else
    STATUS[csharpc]="does not compile yet"
    python3 -I "$HERE/blockers.py" "$OUT/csharpc.build.log" > "$OUT/csharpc-blockers.md"
    head -60 "$OUT/csharpc-blockers.md"
  fi
fi

# ---- runs --------------------------------------------------------------------------------------
# start_server <build>: starts it pinned to the server CPUs; STARTUP = ms until the first answer (or why not).
start_server() {
  local build="$1" t0 t1 cmd
  if [[ "$build" == jit ]]; then cmd=("$DOTNET" "$WORK/jit/TechEmpower.dll"); else cmd=("${BIN[$build]}"); fi
  t0=$(date +%s%N)
  ASPNETCORE_URLS="http://127.0.0.1:$PORT" taskset -c "$SERVER_CPUS" "${cmd[@]}" > "$OUT/$build.server.log" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 2000); do
    if curl -s -o /dev/null "http://127.0.0.1:$PORT/plaintext"; then
      t1=$(date +%s%N); STARTUP=$(( (t1 - t0) / 1000000 )); return 0
    fi
    kill -0 "$SERVER_PID" 2>/dev/null || { STARTUP="the server exited ($(tail -c 300 "$OUT/$build.server.log" | tr '\n|' '  '))"; return 1; }
    sleep 0.005
  done
  STARTUP="no answer after 10 s"; return 1
}
stop_server() { kill "$SERVER_PID" 2>/dev/null; wait "$SERVER_PID" 2>/dev/null; SERVER_PID=""; }
rss_kib() { awk '/VmHWM/ {print $2}' "/proc/$SERVER_PID/status" 2>/dev/null || echo "?"; }

# wrk <out> <connections> <pipeline-depth> <path>: prints "req/s|avg latency|p99 latency"
load() {
  local out="$1" c="$2" depth="$3" path="$4" threads="$CLIENT_THREADS" script=()
  (( threads > c )) && threads=$c
  (( depth > 1 )) && script=(-s "$HERE/pipeline.lua" -- "$depth")
  taskset -c "$CLIENT_CPUS" wrk -H 'Host: localhost' -H 'Accept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7' \
    -H 'Connection: keep-alive' --latency -d "$WARMUP" -c "$c" --timeout 8 -t "$threads" "http://127.0.0.1:$PORT$path" "${script[@]}" > /dev/null 2>&1
  taskset -c "$CLIENT_CPUS" wrk -H 'Host: localhost' -H 'Accept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7' \
    -H 'Connection: keep-alive' --latency -d "$DURATION" -c "$c" --timeout 8 -t "$threads" "http://127.0.0.1:$PORT$path" "${script[@]}" > "$WORK/wrk.txt" 2>&1
  { echo "### $path c=$c pipeline=$depth"; cat "$WORK/wrk.txt"; echo; } >> "$out"
  awk '/^Requests\/sec/ {rps=$2} /^    Latency/ && !lat {lat=$2} /^ +99%/ {p99=$2} /Non-2xx|Socket errors/ {err=err $0}
       END {printf "%s|%s|%s%s\n", (rps ? sprintf("%.0f", rps) : "?"), (lat ? lat : "?"), (p99 ? p99 : "?"), (err ? " (errors)" : "")}' "$WORK/wrk.txt"
}

declare -A ROW
SCENARIOS=()
for c in $PLAINTEXT_CONNECTIONS; do SCENARIOS+=("plaintext|/plaintext|$c|$PIPELINE"); done
for c in $JSON_CONNECTIONS; do SCENARIOS+=("json|/json|$c|1"); done

for build in aot csharpc jit; do
  wants "$build" && [[ -n "${BIN[$build]:-}" ]] || continue
  echo "== run $build"
  # Start-up: best of 5 (process start until the first plaintext answer).
  best=""
  for _ in 1 2 3 4 5; do
    if start_server "$build"; then [[ -z "$best" || $STARTUP -lt $best ]] && best=$STARTUP; else STATUS[$build]="does not start: $STARTUP"; stop_server; continue 2; fi
    stop_server
  done
  ROW[$build,startup]="$best"
  start_server "$build" || { STATUS[$build]="does not start"; stop_server; continue; }
  ROW[$build,rss0]=$(rss_kib)
  if python3 -I "$HERE/verify.py" "127.0.0.1:$PORT" > "$OUT/$build.verify.txt" 2> "$OUT/$build.verify.err"; then ROW[$build,verify]="pass"
  else ROW[$build,verify]="FAIL: $(head -3 "$OUT/$build.verify.err" | tr '\n' ' ')"; fi
  if [[ $LOAD == 1 && "${ROW[$build,verify]}" == pass ]]; then
    : > "$OUT/$build.wrk.txt"
    for s in "${SCENARIOS[@]}"; do
      IFS='|' read -r name path c depth <<< "$s"
      ROW[$build,$name,$c]=$(load "$OUT/$build.wrk.txt" "$c" "$depth" "$path")
    done
  fi
  ROW[$build,rss]=$(rss_kib)
  stop_server
  if [[ "$build" == jit ]]; then ROW[$build,size]=$(du -sk "$WORK/jit" | cut -f1)
  else ROW[$build,size]=$(( $(stat -c %s "${BIN[$build]}") / 1024 )); fi
done

# csharpc must answer exactly like NativeAOT (same bodies and headers; Date normalized).
if [[ -f "$OUT/csharpc.verify.txt" && -f "$OUT/aot.verify.txt" ]]; then
  if diff -u "$OUT/aot.verify.txt" "$OUT/csharpc.verify.txt" > "$OUT/csharpc-vs-aot.diff"; then ROW[csharpc,same]="identical to NativeAOT"
  else ROW[csharpc,same]="DIFFERS from NativeAOT (csharpc-vs-aot.diff)"; ROW[csharpc,verify]="FAIL: responses differ"; fi
fi

# ---- report ------------------------------------------------------------------------------------
{
  echo "## TechEmpower plaintext/JSON: NativeAOT vs csharpc (vs the JIT)"
  echo
  echo "$(date -u +%Y-%m-%d) · $(uname -m) · $CPUS CPUs ($SERVER_CPUS server, $CLIENT_CPUS wrk) · .NET $("$DOTNET" --version) · wrk ${DURATION}s per run after ${WARMUP}s warm-up · plaintext pipelined ×$PIPELINE · csharpc $CSHARPC_FLAGS"
  echo
  echo "| | NativeAOT | csharpc | JIT |"
  echo "|---|---:|---:|---:|"
  cell() { local v="${ROW[$1,$2]:-}"; [[ -n "$v" ]] && echo "$v" || echo "—"; }
  echo "| build | ${STATUS[aot]:-skipped} | ${STATUS[csharpc]:-skipped} | ${STATUS[jit]:-skipped} |"
  echo "| TechEmpower checks | $(cell aot verify) | $(cell csharpc verify)${ROW[csharpc,same]:+ (${ROW[csharpc,same]})} | $(cell jit verify) |"
  for s in "${SCENARIOS[@]}"; do
    IFS='|' read -r name path c depth <<< "$s"
    # wrk measures no per-request latency for pipelined requests (it reports 0.00us): n/a there.
    fmt() { local v="${ROW[$1,$name,$c]:-}"; [[ -z "$v" ]] && { echo "—"; return; }; IFS='|' read -r rps avg p99 <<< "$v"; [[ $depth -gt 1 ]] && avg="n/a" p99="n/a"; echo "$rps req/s · $avg avg · $p99 p99"; }
    echo "| $name, $c connections$([[ $depth -gt 1 ]] && echo ", pipeline $depth") | $(fmt aot) | $(fmt csharpc) | $(fmt jit) |"
  done
  kib() { local v="${ROW[$1,$2]:-}"; [[ -z "$v" || "$v" == "?" ]] && { echo "—"; return; }; awk -v k="$v" 'BEGIN {printf "%.1f MiB", k / 1024}'; }
  echo "| peak RSS idle → after load | $(kib aot rss0) → $(kib aot rss) | $(kib csharpc rss0) → $(kib csharpc rss) | $(kib jit rss0) → $(kib jit rss) |"
  echo "| binary size | $(kib aot size) | $(kib csharpc size) | $(kib jit size) (app + dlls, needs the shared runtime) |"
  ms() { local v="${ROW[$1,startup]:-}"; [[ -n "$v" ]] && echo "$v ms" || echo "—"; }
  echo "| start-up (to first response, best of 5) | $(ms aot) | $(ms csharpc) | $(ms jit) |"
  if [[ -f "$OUT/csharpc-blockers.md" ]]; then
    echo
    echo "### What stops the csharpc build"
    echo
    cat "$OUT/csharpc-blockers.md"
  fi
} > "$OUT/results.md"
cat "$OUT/results.md"
