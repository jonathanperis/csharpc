#!/usr/bin/env bash
# M7 harness (docs/06-roadmap.md, "M7"), all phases on one machine: builds TechEmpower's ASP.NET Core
# [Platform] entry (benchmarks/techempower/app) with NativeAOT, csharpc and the JIT, measures every test
# type TechEmpower runs for it and writes the side-by-side report with a csharpc-vs-NativeAOT verdict per
# metric (benchmarks/techempower/harness.py has the details). CI runs the same phases as matrix jobs
# (.github/workflows/techempower.yml).
#
#   benchmarks/techempower/run.sh [--app platform|interim] [--out DIR] [--only aot,csharpc,jit]
#                                 [--tests plaintext,json,db,query,fortune,update,cached-query,deploy]
#                                 [--rounds N] [--no-load] [--images] [--no-tfb-verify]
#
# Writes DIR/results.md and DIR/results.json, DIR/build (binaries, logs, csharpc-blockers.md when csharpc
# can't build the app) and DIR/<test> (wrk output, server logs, verification transcripts). Exits 1 when a
# required build fails, a build fails TechEmpower's verification or answers differently from NativeAOT.
# Needs wrk, docker (PostgreSQL: TechEmpower's image), and for TechEmpower's verification siege and
# python3 with requests, colorama and psycopg2. Heavy: a full run takes about two hours; locally prefer
# --tests and DURATION=5. Environment as for harness.py (DURATION, PORT, CSHARPC_FLAGS, TFB_DIR, PROFILE).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP=platform OUT="$PWD/techempower-results" ONLY="aot,csharpc,jit" TESTS="" ROUNDS=3 MEASURE=() BUILD=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="$2"; shift ;;
    --out) OUT="$2"; shift ;;
    --only) ONLY="$2"; shift ;;
    --tests) TESTS="$2"; shift ;;
    --rounds) ROUNDS="$2"; shift ;;
    --no-load) MEASURE+=(--no-load) ;;
    --no-tfb-verify) MEASURE+=(--no-tfb-verify) ;;
    --images) BUILD+=(--images) ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done
if [[ -z "$TESTS" ]]; then
  if [[ $APP == platform ]]; then TESTS="deploy,plaintext,json,db,query,fortune,update,cached-query"; else TESTS="deploy,plaintext,json"; fi
fi
mkdir -p "$OUT"
python3 "$HERE/harness.py" build --app "$APP" --out "$OUT/build" --only "$ONLY" "${BUILD[@]}"
for test in ${TESTS//,/ }; do
  python3 "$HERE/harness.py" measure --app "$APP" --builds "$OUT/build" --test "$test" --out "$OUT/$test" --rounds "$ROUNDS" "${MEASURE[@]}"
done
python3 "$HERE/harness.py" report --out "$OUT" "$OUT"
