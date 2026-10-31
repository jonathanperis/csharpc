#!/usr/bin/env bash
# Differential test runner.
#
#   tests/run.sh              compile every tests/samples/*.cs with csharpc, run it, and compare
#                             stdout + exit code against the recorded .NET results
#   tests/run.sh --regen      (re)record the expected results by running each sample on real .NET
#                             (`dotnet run file.cs`), which is the oracle for C# semantics
#   tests/run.sh --opt -O0    run the suite at a specific optimization level (default -O2)
#   tests/run.sh --all-opts   run the suite at -O0 and -O2
#   tests/run.sh --shard 0/4  deterministic slice of the filtered sample list (CI shards)
#   tests/run.sh --repeat 20  run the selection N times (flake hunting; fails if any pass fails)
#   tests/run.sh --list       print the selected sample names, one per line, and do not build
#   tests/run.sh 06           only samples whose name contains "06" ('06|12_' : either)
#   tests/run.sh --via-il     the program as IL (ADR 0010, "The program as IL"): build each sample with the
#                             SDK (`dotnet build NAME.cs`, the csc build `dotnet run` uses), compile the
#                             resulting NAME.dll with csharpc, and compare against the same recordings.
#                             Samples with a .diagnostics file must be rejected (any explicit diagnostic).
#                             CSHARPC_IL_BUILD=dir keeps the csc builds between runs (rebuilt when the .cs
#                             is newer); CSHARPC_IL_JOBS=N builds N samples at a time (default 2).
#
# A sample with a NAME.diagnostics file is one csharpc must reject cleanly (explicit diagnostics, no
# crash): the compiler's stderr, with the samples directory stripped from paths, must equal that file.
#
# Environment: CSHARPC_FLAGS="-g" (extra compiler flags, e.g. run the suite with debug info), CSHARPC_OPT=-O0 (same as --opt), CSHARPC_TEST_TIMEOUT=20 (seconds per binary),
#              DOTNET=path/to/dotnet, CSHARPC_TEST_TARGET=wasm32-wasip1 (compile for WebAssembly and run with
#              wasmtime; see tests/platform.sh).
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SAMPLES="$ROOT/tests/samples"
DOTNET="${DOTNET:-dotnet}"
OPTS=("${CSHARPC_OPT:--O2}")
REGEN=0
LIST_ONLY=0
FILTER=""
SHARD_INDEX=0
SHARD_COUNT=1
REPEAT=1
VIA_IL=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --regen) REGEN=1 ;;
    --list) LIST_ONLY=1 ;;
    --via-il) VIA_IL=1 ;;
    --opt) shift; OPTS=("$1") ;;
    --all-opts) OPTS=(-O0 -O2) ;;
    --shard)
      shift
      [[ "$1" =~ ^([0-9]+)/([1-9][0-9]*)$ ]] || { echo "bad --shard '$1' (want INDEX/COUNT)" >&2; exit 2; }
      SHARD_INDEX="${BASH_REMATCH[1]}"
      SHARD_COUNT="${BASH_REMATCH[2]}"
      if (( SHARD_INDEX >= SHARD_COUNT )); then
        echo "bad --shard $SHARD_INDEX/$SHARD_COUNT (index must be < count)" >&2
        exit 2
      fi
      ;;
    --repeat)
      shift
      [[ "$1" =~ ^[1-9][0-9]*$ ]] || { echo "bad --repeat '$1'" >&2; exit 2; }
      REPEAT="$1"
      ;;
    -O0|-O1|-O2|-O3) OPTS=("$1") ;;
    *) FILTER="$1" ;;
  esac
  shift
done

source "$ROOT/tests/platform.sh"

# LC_ALL=C so every runner slices the same names the same way. The test runs themselves keep the
# culture pin from platform.sh; this sort is a separate process.
selected=()
index=0
while IFS= read -r src; do
  [[ -z "$src" ]] && continue
  name="$(basename "$src" .cs)"
  if [[ -n "$FILTER" ]]; then
    matched=0
    IFS='|' read -ra alternatives <<< "$FILTER"
    for alternative in "${alternatives[@]}"; do [[ "$name" == *"$alternative"* ]] && { matched=1; break; }; done
    [[ $matched == 1 ]] || continue
  fi
  if (( index % SHARD_COUNT == SHARD_INDEX )); then
    selected+=("$name")
  fi
  index=$((index + 1))
done < <(printf '%s\n' "$SAMPLES"/*.cs | LC_ALL=C sort)

if [[ $LIST_ONLY == 1 ]]; then
  if ((${#selected[@]} > 0)); then printf '%s\n' "${selected[@]}"; fi
  exit 0
fi

# Compiler warnings would otherwise end up in the recorded stdout of `dotnet run`.
strip_warnings() { grep -Ev '^[^ ]+\([0-9]+,[0-9]+\): warning ' || true; }

if [[ $REGEN == 1 && $VIA_IL == 1 ]]; then echo "--regen records from .NET; it can't be combined with --via-il" >&2; exit 2; fi
if [[ $REGEN == 1 ]]; then
  if ((${#selected[@]} == 0)); then exit 0; fi
  for name in "${selected[@]}"; do
    printf 'recording %-32s' "$name"
    # File-based apps (dotnet run app.cs) — the real .NET compiler and runtime.
    # WarningLevel=0 keeps compiler warnings out of stdout; strip_warnings is a safety net.
    stdin_file=/dev/null; [[ -f "$SAMPLES/$name.stdin" ]] && stdin_file="$SAMPLES/$name.stdin"
    read -ra test_env <<< "$(platform_env "$SAMPLES" "$name")"
    (cd "$SAMPLES" && env "${test_env[@]}" "$DOTNET" run "$name.cs" -p:WarningLevel=0 2>/dev/null < "$stdin_file") > "$SAMPLES/$name.raw"
    code=$?
    strip_warnings < "$SAMPLES/$name.raw" > "$SAMPLES/$name.expected"
    rm -f "$SAMPLES/$name.raw"
    if [[ $code != 0 ]]; then echo "$code" > "$SAMPLES/$name.exitcode"; else rm -f "$SAMPLES/$name.exitcode"; fi
    echo "exit=$code"
  done
  exit 0
fi

if ((${#selected[@]} == 0)); then
  echo "0 passed, 0 failed"
  exit 0
fi

if (( SHARD_COUNT > 1 || REPEAT > 1 )); then
  echo "selection: ${#selected[@]} samples, shard $SHARD_INDEX/$SHARD_COUNT, repeat $REPEAT, opts ${OPTS[*]}"
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
# A temp dir without symlinks in its path: macOS's $TMPDIR (/var/folders/...) is reached through the
# /var -> /private/var link, so getcwd() inside it would not match Path.GetTempPath() (under .NET too).
TMPDIR="$(cd "${TMPDIR:-/tmp}" && pwd -P)/"; export TMPDIR
pass=0; fail=0; failed=()

# --via-il: the samples built by the SDK first (several at a time), then compiled from their IL below.
if [[ $VIA_IL == 1 ]]; then
  [[ ${#CULTURE_PIN[@]} -eq 0 && $TARGET_OS == "$HOST_OS" ]] || { echo "--via-il runs on Linux hosts only (no culture pin, no cross target)" >&2; exit 2; }
  IL_BUILD="${CSHARPC_IL_BUILD:-$OUT/il}"
  mkdir -p "$IL_BUILD"
  build_il() { # name: $IL_BUILD/NAME/NAME.dll (+ runtimeconfig.json), or $IL_BUILD/NAME.failed with csc's errors
    local name="$1" dir="$IL_BUILD/$1"
    [[ -f "$dir/$name.dll" && "$dir/$name.dll" -nt "$SAMPLES/$name.cs" ]] && return 0
    rm -rf "$dir" "$IL_BUILD/$name.failed"
    # Only the bare build: `dotnet run NAME.cs` builds the same way (Debug, the file's #: directives).
    "$DOTNET" build "$SAMPLES/$name.cs" -o "$dir" -p:WarningLevel=0 -v quiet -nologo > "$IL_BUILD/$name.log" 2>&1 \
      && rm -f "$IL_BUILD/$name.log" || mv "$IL_BUILD/$name.log" "$IL_BUILD/$name.failed"
    return 0
  }
  export -f build_il; export IL_BUILD SAMPLES DOTNET
  printf '%s\n' "${selected[@]}" | xargs -P "${CSHARPC_IL_JOBS:-2}" -I{} bash -c 'build_il "$1"' _ {}
fi
for ((rep = 1; rep <= REPEAT; rep++)); do
  for opt in "${OPTS[@]}"; do
    for name in "${selected[@]}"; do
      src="$SAMPLES/$name.cs"
      label="$name [$opt]"
      if (( REPEAT > 1 )); then label="$name [$opt] #$rep"; fi
      if [[ $VIA_IL == 1 ]]; then
        dll="$IL_BUILD/$name/$name.dll"
        if [[ -f "$IL_BUILD/$name.failed" ]]; then
          echo "FAIL $label (dotnet build failed)"; grep -E "error" "$IL_BUILD/$name.failed" | sed 's/^/     /' | head -5
          fail=$((fail + 1)); failed+=("$label"); continue
        fi
        src="$dll"
      fi
      if [[ -f "$SAMPLES/$name.diagnostics" && $VIA_IL == 1 ]]; then
        # Rejected programs: from IL, any explicit diagnostic (exit 1) will do; the wording differs from the source path's.
        "${CSHARPC[@]}" "$opt" ${CSHARPC_FLAGS:-} "$src" -o "$OUT/$name$opt$EXE_SUFFIX" > /dev/null 2> "$OUT/$name.compile"
        got_code=$?
        if [[ $got_code == 1 ]]; then echo "PASS $label (rejected)"; pass=$((pass + 1))
        else
          echo "FAIL $label (compiler exit $got_code, expected 1 with a diagnostic)"; sed 's/^/     /' "$OUT/$name.compile" | head -8
          fail=$((fail + 1)); failed+=("$label")
        fi
        continue
      fi
      if [[ -f "$SAMPLES/$name.diagnostics" ]]; then
        "${CSHARPC[@]}" "${TARGET_FLAGS[@]}" "$opt" ${CSHARPC_FLAGS:-} "$src" -o "$OUT/$name$opt$EXE_SUFFIX" > /dev/null 2> "$OUT/$name.compile"
        got_code=$?
        platform_compiler_output < "$OUT/$name.compile" | sed "s|^$SAMPLES/||" > "$OUT/$name.diag"
        if [[ $got_code == 1 ]] && diff -u "$SAMPLES/$name.diagnostics" "$OUT/$name.diag" > "$OUT/$name.diff"; then
          echo "PASS $label (rejected with the expected diagnostics)"; pass=$((pass + 1))
        else
          echo "FAIL $label (compiler exit $got_code, expected 1 with the recorded diagnostics)"
          sed 's/^/     /' "$OUT/$name.diff" | head -20
          fail=$((fail + 1)); failed+=("$label")
        fi
        continue
      fi
      expected="$SAMPLES/$name.expected"
      # A sample without a recording would otherwise pass every shard and CI's coverage check unrun.
      [[ -f "$expected" ]] || { echo "FAIL $label (no .expected — record it with --regen)"; fail=$((fail + 1)); failed+=("$label"); continue; }
      reason="$(platform_skip "$name")"
      [[ -n "$reason" ]] && ! platform_is_live "$reason" && { echo "SKIP $label (platform-dependent on $TARGET_OS: $reason)"; continue; }
      stdin_file=/dev/null; [[ -f "$SAMPLES/$name.stdin" ]] && stdin_file="$SAMPLES/$name.stdin"
      read -ra test_env <<< "$(platform_env "$SAMPLES" "$name")"
      pin=("${CULTURE_PIN[@]}"); [[ ${#test_env[@]} -gt 0 || $VIA_IL == 1 ]] && pin=()
      if [[ -n "$reason" ]]; then
        # Recorded live from real .NET on this machine (once per run).
        [[ -f "$OUT/$name.live" ]] || platform_live_expected "$SAMPLES" "$name.cs" "$stdin_file" "$OUT/$name.live" "${test_env[@]}" > "$OUT/$name.livecode"
        cp "$OUT/$name.live" "$OUT/$name.want"
        want_code="$(cat "$OUT/$name.livecode")"
      else
        platform_expected < "$expected" > "$OUT/$name.want"
        want_code=0
        [[ -f "$SAMPLES/$name.exitcode" ]] && want_code="$(platform_exit_code "$(cat "$SAMPLES/$name.exitcode")")"
      fi

      exe="$OUT/$name$opt$EXE_SUFFIX"
      if ! "${CSHARPC[@]}" "${TARGET_FLAGS[@]}" "$opt" ${CSHARPC_FLAGS:-} "$src" "${pin[@]}" -o "$exe" 2> "$OUT/$name.compile"; then
        echo "FAIL $label (compile error)"; sed 's/^/     /' "$OUT/$name.compile" | head -8
        fail=$((fail + 1)); failed+=("$label"); continue
      fi
      # Run in a child bash so the parent shell doesn't print "Aborted (core dumped)" job noise
      # for samples that intentionally die with an unhandled exception (SIGABRT, exit 134).
      env "${test_env[@]}" bash -c 'ulimit -c 0 2>/dev/null; t="$1" out="$2" err="$3" in="$4"; shift 4; timeout "$t" "$@" > "$out" 2> "$err" < "$in"' _ \
        "${CSHARPC_TEST_TIMEOUT:-90}" "$OUT/$name.stdout" "$OUT/$name.stderr" "$stdin_file" "${RUN_PREFIX[@]}" "$exe" 2>/dev/null
      got_code=$?
      if diff -u "$OUT/$name.want" "$OUT/$name.stdout" > "$OUT/$name.diff" && [[ "$got_code" == "$want_code" ]]; then
        echo "PASS $label"
        pass=$((pass + 1))
      else
        echo "FAIL $label (exit $got_code, expected $want_code)"
        sed 's/^/     /' "$OUT/$name.diff" | head -20
        # What the program printed to stderr (an unhandled exception, a runtime abort) says why.
        if [[ -s "$OUT/$name.stderr" ]]; then sed 's/^/     stderr: /' "$OUT/$name.stderr" | head -10; fi
        fail=$((fail + 1)); failed+=("$label")
      fi
    done
  done
done
echo
if (( REPEAT > 1 )); then
  echo "$pass passed, $fail failed across $REPEAT repeats${failed:+: ${failed[*]}}"
else
  echo "$pass passed, $fail failed${failed:+: ${failed[*]}}"
fi
[[ $fail == 0 ]]
