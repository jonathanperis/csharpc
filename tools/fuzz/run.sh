#!/usr/bin/env bash
# Build and run the csrt libFuzzer harness under ASan + UBSan.
#   tools/fuzz/run.sh [seconds-per-run] [jobs]     (default 300 s, 1 job)
# Crashes are written to tools/fuzz/artifacts/; the corpus persists in tools/fuzz/corpus/.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SECONDS_PER_RUN="${1:-300}"
JOBS="${2:-1}"
BUILD="$HERE/build"
mkdir -p "$BUILD" "$HERE/corpus" "$HERE/artifacts"
FLAGS=(-g -O1 -fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -fexceptions -funwind-tables)
objs=()
for c in "$ROOT"/src/runtime/*.c; do
  o="$BUILD/$(basename "$c" .c).o"
  clang -std=c11 "${FLAGS[@]}" -fsanitize=fuzzer-no-link -c "$c" -o "$o"
  objs+=("$o")
done
clang++ -std=c++17 "${FLAGS[@]}" -fsanitize=fuzzer "$HERE/fuzz_runtime.cpp" "${objs[@]}" -lm -pthread -o "$BUILD/fuzz_runtime"
cd "$HERE"
ASAN_OPTIONS="detect_leaks=0:detect_stack_use_after_return=0:allocator_may_return_null=1" \
  "$BUILD/fuzz_runtime" -max_total_time="$SECONDS_PER_RUN" -jobs="$JOBS" -workers="$JOBS" \
  -artifact_prefix="$HERE/artifacts/" -max_len=512 -rss_limit_mb=4096 corpus/
