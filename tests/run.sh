#!/usr/bin/env bash
# Differential test runner.
#
#   tests/run.sh              compile every tests/samples/*.cs with csharpc, run it, and compare
#                             stdout + exit code against the recorded .NET results
#   tests/run.sh --regen      (re)record the expected results by running each sample on real .NET
#                             (`dotnet run file.cs`), which is the oracle for C# semantics
#   tests/run.sh --opt -O0    run the suite at a specific optimization level (default -O2)
#   tests/run.sh --all-opts   run the suite at -O0 and -O2
#   tests/run.sh 06           only samples whose name contains "06"
#
# A sample with a NAME.diagnostics file is one csharpc must reject cleanly (explicit diagnostics, no
# crash): the compiler's stderr, with the samples directory stripped from paths, must equal that file.
#
# Environment: CSHARPC_FLAGS="-g" (extra compiler flags, e.g. run the suite with debug info), CSHARPC_OPT=-O0 (same as --opt), CSHARPC_TEST_TIMEOUT=20 (seconds per binary),
#              DOTNET=path/to/dotnet.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SAMPLES="$ROOT/tests/samples"
DOTNET="${DOTNET:-dotnet}"
OPTS=("${CSHARPC_OPT:--O2}")
REGEN=0
FILTER=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --regen) REGEN=1 ;;
    --opt) shift; OPTS=("$1") ;;
    --all-opts) OPTS=(-O0 -O2) ;;
    -O0|-O1|-O2|-O3) OPTS=("$1") ;;
    *) FILTER="$1" ;;
  esac
  shift
done

# Compiler warnings would otherwise end up in the recorded stdout of `dotnet run`.
strip_warnings() { grep -Ev '^[^ ]+\([0-9]+,[0-9]+\): warning ' || true; }

if [[ $REGEN == 1 ]]; then
  for src in "$SAMPLES"/*.cs; do
    name="$(basename "$src" .cs)"
    [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
    printf 'recording %-32s' "$name"
    # File-based apps (dotnet run app.cs) — the real .NET compiler and runtime.
    # WarningLevel=0 keeps compiler warnings out of stdout; strip_warnings is a safety net.
    stdin_file=/dev/null; [[ -f "$SAMPLES/$name.stdin" ]] && stdin_file="$SAMPLES/$name.stdin"
    (cd "$SAMPLES" && "$DOTNET" run "$name.cs" -p:WarningLevel=0 2>/dev/null < "$stdin_file") > "$SAMPLES/$name.raw"
    code=$?
    strip_warnings < "$SAMPLES/$name.raw" > "$SAMPLES/$name.expected"
    rm -f "$SAMPLES/$name.raw"
    if [[ $code != 0 ]]; then echo "$code" > "$SAMPLES/$name.exitcode"; else rm -f "$SAMPLES/$name.exitcode"; fi
    echo "exit=$code"
  done
  exit 0
fi

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "build failed"; exit 1; }
CSHARPC=("$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll")

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
case "$(uname -s)" in Darwin) HOST_OS=macos ;; *) HOST_OS=linux ;; esac
# A temp dir without symlinks in its path: macOS's $TMPDIR (/var/folders/...) is reached through the
# /var -> /private/var link, so getcwd() inside it would not match Path.GetTempPath() (under .NET too).
TMPDIR="$(cd "${TMPDIR:-/tmp}" && pwd -P)/"; export TMPDIR
# Platform-dependent tests (tests/platform-skips.txt): prints the reason if $1 is skipped on this OS.
platform_skip() { awk -v os="$HOST_OS" -v t="$1" '$1 == os && $2 == t { $1 = $2 = ""; sub(/^ +/, ""); print; exit }' "$ROOT/tests/platform-skips.txt"; }
pass=0; fail=0; failed=()
for opt in "${OPTS[@]}"; do
  for src in "$SAMPLES"/*.cs; do
    name="$(basename "$src" .cs)"
    [[ -n "$FILTER" && "$name" != *"$FILTER"* ]] && continue
    label="$name [$opt]"
    if [[ -f "$SAMPLES/$name.diagnostics" ]]; then
      "${CSHARPC[@]}" "$opt" ${CSHARPC_FLAGS:-} "$src" -o "$OUT/$name$opt" > /dev/null 2> "$OUT/$name.compile"
      got_code=$?
      sed "s|^$SAMPLES/||" "$OUT/$name.compile" > "$OUT/$name.diag"
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
    [[ -f "$expected" ]] || { echo "SKIP $label (no .expected — run with --regen)"; continue; }
    reason="$(platform_skip "$name")"
    [[ -n "$reason" ]] && { echo "SKIP $label (platform-dependent on $HOST_OS: $reason)"; continue; }
    want_code=0
    [[ -f "$SAMPLES/$name.exitcode" ]] && want_code="$(cat "$SAMPLES/$name.exitcode")"

    exe="$OUT/$name$opt"
    if ! "${CSHARPC[@]}" "$opt" ${CSHARPC_FLAGS:-} "$src" -o "$exe" 2> "$OUT/$name.compile"; then
      echo "FAIL $label (compile error)"; sed 's/^/     /' "$OUT/$name.compile" | head -8
      fail=$((fail + 1)); failed+=("$label"); continue
    fi
    # Run in a child bash so the parent shell doesn't print "Aborted (core dumped)" job noise
    # for samples that intentionally die with an unhandled exception (SIGABRT, exit 134).
    stdin_file=/dev/null; [[ -f "$SAMPLES/$name.stdin" ]] && stdin_file="$SAMPLES/$name.stdin"
    bash -c 'ulimit -c 0; timeout "$1" "$2" > "$3" 2> "$4" < "$5"' _ \
      "${CSHARPC_TEST_TIMEOUT:-90}" "$exe" "$OUT/$name.stdout" "$OUT/$name.stderr" "$stdin_file" 2>/dev/null
    got_code=$?
    if diff -u "$expected" "$OUT/$name.stdout" > "$OUT/$name.diff" && [[ "$got_code" == "$want_code" ]]; then
      echo "PASS $label"
      pass=$((pass + 1))
    else
      echo "FAIL $label (exit $got_code, expected $want_code)"
      sed 's/^/     /' "$OUT/$name.diff" | head -20
      fail=$((fail + 1)); failed+=("$label")
    fi
  done
done
echo
echo "$pass passed, $fail failed${failed:+: ${failed[*]}}"
[[ $fail == 0 ]]
