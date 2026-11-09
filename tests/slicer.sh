#!/usr/bin/env bash
# The samples that exercise the framework's CoreLib IL, compiled from slices of it (issue #191 part B, tools/il-first/slicer).
#
#   tests/slicer.sh [-O0|-O2] [sample-name-filter ...]
#
# Slices System.Guid, System.Text.StringBuilder, System.Number and List out of the shared framework's System.Private.CoreLib (the
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
[[ ${#SAMPLES[@]} -eq 0 ]] && SAMPLES=(161_stringbuilder 201_integer_append_edges 250_stringbuilder_interpolation 1294_guid_text_roundtrip 1299_list_sort_helpers)
CSC="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
[[ -f "$CSC" ]] || dotnet build "$ROOT/src/Csharpc" -c Release -m:1 > /dev/null || exit 1
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT

"$ROOT/tools/il-first/slicer/slice.sh" "$WORK/slice.dll" System.Guid,System.Text.StringBuilder,System.Number,System.Collections.Generic.List!1 > "$WORK/slice.log" 2>&1 \
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
  timeout 60 "$WORK/$name" < "$input" > "$WORK/$name.out" 2> /dev/null; code=$?  # (a hang fails in a minute instead of burning a core)
  expected=0; [[ -f "$ROOT/tests/samples/$name.exitcode" ]] && expected=$(cat "$ROOT/tests/samples/$name.exitcode")
  if cmp -s "$WORK/$name.out" "$ROOT/tests/samples/$name.expected" && [[ $code == "$expected" ]]; then
    echo "PASS $name [$OPT, slice]"; pass=$((pass + 1))
  else
    echo "FAIL $name: output differs (exit $code, expected $expected)"; diff "$WORK/$name.out" "$ROOT/tests/samples/$name.expected" | head -5 | sed 's/^/     /'; fail=$((fail + 1))
  fi
done
# MemoryExtensions from the framework's IL, on its own (issue #252): its searches (IndexOf, LastIndexOf, Contains, IndexOfAny, SequenceEqual)
# run on Vector128 where the target has it, and must give the recorded answers of the scalar reference. Alone because its wider surface
# (SearchValues, Ascii, Vector256 paths) is not served yet when Guid/StringBuilder code reaches it too.
if "$ROOT/tools/il-first/slicer/slice.sh" "$WORK/memory.dll" System.MemoryExtensions > "$WORK/memory.log" 2>&1 \
   && CSHARPC_UPSTREAM_MAP="$WORK/memory.map" dotnet "$CSC" "$OPT" -r "$WORK/memory.dll" "$ROOT/tests/samples/1301_span_search_vector_paths.cs" -o "$WORK/s1301" > "$WORK/s1301.log" 2>&1 \
   && timeout 120 "$WORK/s1301" > "$WORK/s1301.out" 2> /dev/null && cmp -s "$WORK/s1301.out" "$ROOT/tests/samples/1301_span_search_vector_paths.expected"; then
  echo "PASS 1301_span_search_vector_paths [$OPT, MemoryExtensions slice]"; pass=$((pass + 1))
else
  echo "FAIL 1301_span_search_vector_paths from the MemoryExtensions slice"; head -3 "$WORK/memory.log" "$WORK/s1301.log" 2> /dev/null | cut -c1-300 | sed 's/^/     /'; fail=$((fail + 1))
fi
# RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter (issue #224). The member is internal to CoreLib and the framework's own callers
# are behind feature switches csharpc folds (ArraySortHelper<T>) or need reflection it has not got (ComparerHelpers), so the IL library
# that calls it is written with Cecil (tools/il-first/slicer/Synth.cs). create_instance.expected was recorded on real .NET from the
# same program against a C# Synth that makes the instances with Activator and MakeGenericType.
SLICER_BIN="${SLICER_BIN:-/tmp/csharpc-slicer}"
if dotnet "$SLICER_BIN/Slicer.dll" synth-create-instance "$WORK/synth.dll" > "$WORK/synth.log" 2>&1 \
   && env -u CSHARPC_UPSTREAM_MAP dotnet "$CSC" "$OPT" -r "$WORK/synth.dll" "$ROOT/tests/slicer/create_instance.cs" -o "$WORK/create_instance" > "$WORK/create_instance.log" 2>&1 \
   && timeout 60 "$WORK/create_instance" > "$WORK/create_instance.out" 2> /dev/null && cmp -s "$WORK/create_instance.out" "$ROOT/tests/slicer/create_instance.expected"; then
  echo "PASS create_instance [$OPT, IL library]"; pass=$((pass + 1))
else
  echo "FAIL create_instance"; head -3 "$WORK/synth.log" "$WORK/create_instance.log" 2> /dev/null | cut -c1-300 | sed 's/^/     /'
  [[ -f "$WORK/create_instance.out" ]] && diff "$WORK/create_instance.out" "$ROOT/tests/slicer/create_instance.expected" | head -5 | sed 's/^/     /'
  fail=$((fail + 1))
fi
# Arguments that are not typeof constants stay an explicit diagnostic.
echo 'System.Console.WriteLine(Synth.Dynamic(typeof(int)));' > "$WORK/dynamic.cs"
if env -u CSHARPC_UPSTREAM_MAP dotnet "$CSC" "$OPT" -r "$WORK/synth.dll" "$WORK/dynamic.cs" -o "$WORK/dynamic" > "$WORK/dynamic.log" 2>&1 || ! grep -q "not a typeof constant" "$WORK/dynamic.log"; then
  echo "FAIL create_instance_dynamic: expected the explicit diagnostic"; head -3 "$WORK/dynamic.log" | cut -c1-300 | sed 's/^/     /'; fail=$((fail + 1))
else
  echo "PASS create_instance_dynamic [$OPT, explicit diagnostic]"; pass=$((pass + 1))
fi
echo; echo "$pass passed, $fail failed"
[[ $fail -eq 0 ]]
