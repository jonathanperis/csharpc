#!/usr/bin/env bash
# The samples that exercise the framework's CoreLib IL, compiled from slices of it (issue #191 part B, tools/il-first/slicer).
#
#   tests/slicer.sh [-O0|-O2] [sample-name-filter ...]
#
# Slices System.Guid, System.Text.StringBuilder, System.Number, Dictionary and HashSet out of the shared framework's System.Private.CoreLib (the
# primitives' implementations of CoreLib-internal interfaces such as IUtfChar<T> and IBinaryIntegerParseAndFormatInfo<T>
# travel in the slice, issue #221; boxed primitives dispatch ISpanFormattable), compiles each sample with
# CSHARPC_UPSTREAM_MAP pointing at the slice, runs it and compares stdout and the exit code with the recording made on real
# .NET. These paths are off by default, so the differential suite never reaches them; this script is how they are tested.
# Needs the Mono.Cecil of the ILLink tasks package the SDK restores (see slice.sh). Not part of the merge gate.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="$HOME/.dotnet11:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
OPT="-O2"
if [[ "${1:-}" == -O* ]]; then OPT="$1"; shift; fi
SAMPLES=("$@")
[[ ${#SAMPLES[@]} -eq 0 ]] && SAMPLES=(161_stringbuilder 201_integer_append_edges 250_stringbuilder_interpolation 1294_guid_text_roundtrip 101_dictionary 102_collections 843_dictionary_nongeneric_idictionary 1298_default_comparer_calls)
CSC="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
[[ -f "$CSC" ]] || dotnet build "$ROOT/src/Csharpc" -c Release -m:1 > /dev/null || exit 1
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT

"$ROOT/tools/il-first/slicer/slice.sh" "$WORK/slice.dll" System.Guid,System.Text.StringBuilder,System.Number,System.Collections.Generic.Dictionary!2,System.Collections.Generic.HashSet!1 > "$WORK/slice.log" 2>&1 \
  || { cat "$WORK/slice.log"; echo "FAIL: slicing"; exit 1; }
export CSHARPC_UPSTREAM_MAP="$WORK/slice.map"

pass=0; fail=0
for name in "${SAMPLES[@]}"; do
  src="$ROOT/tests/samples/$name.cs"
  [[ -f "$src" ]] || { echo "FAIL $name: no such sample"; fail=$((fail + 1)); continue; }
  if ! dotnet "$CSC" "$OPT" -r "$WORK/slice.dll" "$src" -o "$WORK/$name" > "$WORK/$name.log" 2>&1; then
    echo "FAIL $name: does not compile from the slice"; head -3 "$WORK/$name.log" | cut -c1-300 | sed 's/^/     /'; fail=$((fail + 1)); continue
  fi
  input=/dev/null; [[ -f "$ROOT/tests/samples/$name.stdin" ]] && input="$ROOT/tests/samples/$name.stdin"
  "$WORK/$name" < "$input" > "$WORK/$name.out" 2> /dev/null; code=$?
  expected=0; [[ -f "$ROOT/tests/samples/$name.exitcode" ]] && expected=$(cat "$ROOT/tests/samples/$name.exitcode")
  if cmp -s "$WORK/$name.out" "$ROOT/tests/samples/$name.expected" && [[ $code == "$expected" ]]; then
    echo "PASS $name [$OPT, slice]"; pass=$((pass + 1))
  else
    echo "FAIL $name: output differs (exit $code, expected $expected)"; diff "$WORK/$name.out" "$ROOT/tests/samples/$name.expected" | head -5 | sed 's/^/     /'; fail=$((fail + 1))
  fi
done
echo; echo "$pass passed, $fail failed"
[[ $fail -eq 0 ]]
