#!/usr/bin/env bash
# Real-world compatibility run: every program in the corpus is built and run under real .NET (the
# oracle), compiled with csharpc, run natively, and classified. See docs/18-compatibility.md.
#
#   tools/compat/run.sh [-j N] [--corpus DIR] [--work DIR] [--no-fetch] [FILTER...]
#
#   -j N        programs in parallel (default 2; each program already runs several processes)
#   --corpus    where the corpus is downloaded (default /tmp/compat-corpus)
#   --work      build trees, logs and results (default /tmp/compat-work); never inside the repo
#   FILTER      only program ids containing one of these substrings
#
# Results: $WORK/results/<id>.tsv (one line: id, category, detail) and $WORK/logs/<id>/*; the
# summary is printed by tools/compat/analyze.py, which run.sh calls at the end.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$ROOT/tools/compat"
JOBS=2; CORPUS=/tmp/compat-corpus; WORK=/tmp/compat-work; FETCH=1; FILTERS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    -j) JOBS="$2"; shift ;;
    --corpus) CORPUS="$2"; shift ;;
    --work) WORK="$2"; shift ;;
    --no-fetch) FETCH=0 ;;
    *) FILTERS+=("$1") ;;
  esac
  shift
done

export DOTNET="${DOTNET:-dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export CORPUS WORK
export CSHARPC_DLL="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
export TFM="${TFM:-net11.0}"
# Timeouts in seconds.
export T_DOTNET_BUILD="${T_DOTNET_BUILD:-300}" T_RUN="${T_RUN:-30}" T_CSHARPC="${T_CSHARPC:-600}"

[[ $FETCH == 1 ]] && { "$HERE/fetch.sh" "$CORPUS" || exit 1; }
"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed"; exit 1; }
mkdir -p "$WORK/results" "$WORK/logs"
python3 -I "$HERE/discover.py" "$CORPUS" "$WORK" "$WORK/tree" "$TFM" || exit 1

# One program: everything below runs in a private directory under $WORK/logs/<id>.
one() {
  local id="$1" repo="$2" proj="$3"
  local name="${repo#*/}"
  local csproj="$WORK/tree/$name/$proj" dir; dir="$(dirname "$csproj")"
  local L="$WORK/logs/$id" R="$WORK/results/$id.tsv"
  rm -rf "$L"; mkdir -p "$L"
  result() { printf '%s\t%s\t%s\n' "$id" "$1" "${2//$'\t'/ }" > "$R"; }

  # The program's own directory tree is copied for every run, so programs that read data files next
  # to them work, and files a run writes can't leak into the next run. Same path every time so
  # output that prints the working directory compares equal.
  fresh_cwd() { rm -rf "$L/cwd"; mkdir -p "$L/cwd"; (cd "$dir" && tar --exclude=./bin --exclude=./obj -cf - .) | tar -xf - -C "$L/cwd"; }
  # Deterministic environment for both runtimes: invariant culture, UTC, no stdin.
  run_prog() { # out err cmd...
    local out="$1" err="$2"; shift 2
    fresh_cwd
    (cd "$L/cwd" && ulimit -c 0 && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 TZ=UTC LANG=C.UTF-8 \
      timeout -k 5 "$T_RUN" "$@" > "$out" 2> "$err" < /dev/null)
  }

  # 1. Real .NET: build once, run twice.
  if ! timeout -k 10 "$T_DOTNET_BUILD" "$DOTNET" build "$csproj" -c Release -nologo -v quiet \
        "-p:OutDir=$L/dn/" -p:TreatWarningsAsErrors=false \
        -p:WarningLevel=0 -p:EnforceCodeStyleInBuild=false -p:UseAppHost=false > "$L/dotnet-build.log" 2>&1; then
    result SKIPPED "does not build under .NET $TFM"; return
  fi
  local rc; rc="$(ls "$L/dn/"*.runtimeconfig.json 2>/dev/null | head -1)"
  [[ -n "$rc" ]] || { result SKIPPED "no runnable assembly in .NET output"; return; }
  local dll="${rc%.runtimeconfig.json}.dll"
  run_prog "$L/dotnet.out" "$L/dotnet.err" "$DOTNET" "$dll"; echo $? > "$L/dotnet.code"
  run_prog "$L/dotnet2.out" "$L/dotnet2.err" "$DOTNET" "$dll"; echo $? > "$L/dotnet2.code"
  local want; want="$(cat "$L/dotnet.code")"
  if [[ $want == 124 || $want == 137 ]]; then result SKIPPED "times out under .NET (waits for input or runs > ${T_RUN}s)"; return; fi
  if ! cmp -s "$L/dotnet.out" "$L/dotnet2.out" || [[ $want != "$(cat "$L/dotnet2.code")" ]]; then
    result SKIPPED "non-deterministic under .NET"; return
  fi
  if grep -q "PlatformNotSupportedException" "$L/dotnet.err"; then result SKIPPED "Windows-only under .NET"; return; fi

  # 2. csharpc.
  local exe="$L/native/app" start end
  mkdir -p "$L/native"
  start=$(date +%s%N)
  timeout -k 10 "$T_CSHARPC" "$DOTNET" "$CSHARPC_DLL" "$csproj" -o "$exe" \
    > "$L/csharpc.log" 2>&1
  local cc=$?
  end=$(date +%s%N); echo $(( (end - start) / 1000000 )) > "$L/csharpc.ms"
  echo "$cc" > "$L/csharpc.code"
  if [[ $cc != 0 || ! -x "$exe" ]]; then
    if [[ $cc == 124 || $cc == 137 ]]; then result COMPILE-CRASH "timeout after ${T_CSHARPC}s"; return; fi
    local ice; ice="$(grep -m1 -E 'CSLLVM9999|CSLLVM0004|internal compiler error|Unhandled exception|   at Csharpc\.' "$L/csharpc.log")"
    if [[ -n "$ice" || $cc -ge 70 ]]; then result COMPILE-CRASH "exit $cc: ${ice:-$(tail -1 "$L/csharpc.log")}"; return; fi
    if grep -qE 'error CSLLVM000[125]' "$L/csharpc.log"; then
      result COMPILE-UNSUPPORTED "$(grep -m1 -oE 'error CSLLVM000[125]: .*' "$L/csharpc.log")"; return
    fi
    result COMPILE-ERROR "exit $cc: $(grep -m1 -E 'error' "$L/csharpc.log" || tail -1 "$L/csharpc.log")"; return
  fi

  # 3. Native run.
  run_prog "$L/native.out" "$L/native.err" "$exe"; local got=$?; echo "$got" > "$L/native.code"
  if cmp -s "$L/dotnet.out" "$L/native.out" && [[ $got == "$want" ]]; then result PASS ""; return; fi
  diff -u --label dotnet --label csharpc "$L/dotnet.out" "$L/native.out" > "$L/diff"
  if [[ $got == 124 || $got == 137 ]]; then result RUNTIME-CRASH "timeout (dotnet exit $want)"; return; fi
  if [[ $got -ge 128 && $got != "$want" ]]; then
    result RUNTIME-CRASH "signal $((got - 128)) (dotnet exit $want): $(grep -m1 -v '^\s*$' "$L/native.err")"; return
  fi
  result RUNTIME-DIFF "exit $got vs dotnet $want; $(grep -m1 -E '^[-+][^-+]' "$L/diff")"
}
export -f one

filter_ok() {
  [[ ${#FILTERS[@]} == 0 ]] && return 0
  local f; for f in "${FILTERS[@]}"; do [[ "$1" == *"$f"* ]] && return 0; done; return 1
}

list="$WORK/selected.tsv"; : > "$list"
while IFS=$'\t' read -r id repo sha proj url; do
  [[ "$id" == \#* ]] && continue
  filter_ok "$id" && printf '%s\t%s\t%s\n' "$id" "$repo" "$proj" >> "$list"
done < "$WORK/programs.tsv"
echo "running $(wc -l < "$list") programs with -j $JOBS (work dir $WORK)"
# shellcheck disable=SC2016
tr '\t' '\n' < "$list" | xargs -d '\n' -n 3 -P "$JOBS" bash -c 'one "$1" "$2" "$3"; cut -f1,2 "$WORK/results/$1.tsv"' _

python3 -I "$HERE/analyze.py" "$WORK"
