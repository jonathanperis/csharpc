#!/usr/bin/env bash
# IL front-end tests (docs/adr/0010-il-frontend.md): programs whose libraries reach csharpc only as
# compiled assemblies. Real .NET is the oracle, as everywhere else.
#
#   tests/projects/il/<App>/<App>.csproj    console app (OutputType Exe) referencing libraries by <Reference>
#   tests/projects/il/<Lib>/<Lib>.csproj    libraries: built with `dotnet build -c Release` first; csharpc
#                                           never sees their sources, only bin/Release/net11.0/<Lib>.dll
#   tests/projects/il/<App>/expect-error    optional: the app must fail to compile with this diagnostic text
#   tests/projects/il/<App>/heavy           optional: a compile too big for every CI shard (the Roslyn host, ~6 GB): skipped
#                                           unless CSHARPC_HEAVY_IL=1; its content, if any, lists the levels (default -O0 -O2)
#
# Each app is compiled with `csharpc <App>.csproj` at -O0 and -O2 and must print exactly what
# `dotnet run` prints (and exit with the same code). One app is also compiled from loose files with -r.
#
# Usage: tests/il.sh [name-filter]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT
FILTER="${1:-}"
pass=0; fail=0

check() { # name expected actual
  if diff -u "$2" "$3" > "$WORK/diff"; then echo "PASS $1"; pass=$((pass + 1));
  else echo "FAIL $1"; sed 's/^/     /' "$WORK/diff" | head -20; fail=$((fail + 1)); fi
}

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed"; exit 1; }
CSHARPC_DLL="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"

# A private copy of the tree, so builds never pollute the repository.
cp -r "$ROOT/tests/projects/il" "$WORK/il"
find "$WORK/il" -name bin -o -name obj | xargs rm -rf

# 1. Libraries: everything that is not an executable.
for proj in "$WORK"/il/*/*.csproj; do
  grep -q '<OutputType>Exe' "$proj" && continue
  if ! "$DOTNET" build "$proj" -c Release -v quiet -nologo > "$WORK/log" 2>&1; then
    echo "FAIL $(basename "$proj" .csproj): dotnet build of the library failed"; sed 's/^/     /' "$WORK/log" | grep -E "error" | head; fail=$((fail+1))
  fi
done

# 2. Apps.
for proj in "$WORK"/il/*/*.csproj; do
  grep -q '<OutputType>Exe' "$proj" || continue
  app="$(basename "$proj" .csproj)"
  dir="$(dirname "$proj")"
  [[ -n "$FILTER" && "$app" != *"$FILTER"* ]] && continue
  levels="-O0 -O2"
  if [[ -f "$dir/heavy" ]]; then
    [[ -n "${CSHARPC_HEAVY_IL:-}" ]] || { echo "SKIP $app (heavy: CSHARPC_HEAVY_IL=1 runs it; .github/workflows/bootstrap.yml)"; continue; }
    [[ -s "$dir/heavy" ]] && levels="$(cat "$dir/heavy")"
    levels="${CSHARPC_HEAVY_LEVELS:-$levels}"
  fi

  if [[ -f "$dir/expect-error" ]]; then
    if "$DOTNET" "$CSHARPC_DLL" "$proj" -o "$WORK/out/" > "$WORK/log" 2>&1; then
      echo "FAIL $app: compiled, but a diagnostic was expected"; fail=$((fail+1))
    elif grep -qF -f "$dir/expect-error" "$WORK/log"; then
      echo "PASS $app: expected diagnostic"; pass=$((pass+1))
    else
      echo "FAIL $app: wrong diagnostic"; sed 's/^/     /' "$WORK/log" | head; echo "     expected: $(cat "$dir/expect-error")"; fail=$((fail+1))
    fi
    continue
  fi

  (cd "$dir" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected"; expected_code=$?
  rm -rf "$dir/bin" "$dir/obj" # csharpc must not see the managed build
  for opt in $levels; do
    if "$DOTNET" "$CSHARPC_DLL" "$proj" "$opt" -o "$WORK/out$opt/" > "$WORK/log" 2>&1; then
      # Run where `dotnet run` runs (apps may read their sibling libraries by relative path).
      (cd "$dir" && "$WORK/out$opt/$app") > "$WORK/actual" 2>&1; code=$?
      check "$app $opt" "$WORK/expected" "$WORK/actual"
      [[ $code == "$expected_code" ]] || { echo "FAIL $app $opt: exit code $code, .NET exits $expected_code"; fail=$((fail+1)); }
    else
      echo "FAIL $app $opt: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head -20; fail=$((fail+1))
    fi
  done
done

# 3. Loose files + -r (the command-line form of an assembly reference).
if [[ -z "$FILTER" || "IlApp" == *"$FILTER"* ]]; then
  lib="$WORK/il/IlShapes/bin/Release/net11.0/IlShapes.dll"
  (cd "$WORK/il/IlApp" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected"
  if "$DOTNET" "$CSHARPC_DLL" "$WORK/il/IlApp/Program.cs" -r "$lib" -o "$WORK/loose" > "$WORK/log" 2>&1; then
    "$WORK/loose" > "$WORK/actual" 2>&1; check "IlApp: csharpc Program.cs -r IlShapes.dll" "$WORK/expected" "$WORK/actual"
  else echo "FAIL IlApp: csharpc -r"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
fi

# 3b. The program as IL (ADR 0010, "The program as IL"): csharpc compiles the app's own assembly as csc built
#     it, with the libraries `dotnet build` copied next to it (IlShapes.dll) found there.
if [[ -z "$FILTER" || "IlApp" == *"$FILTER"* ]]; then
  if "$DOTNET" build "$WORK/il/IlApp/IlApp.csproj" -c Release -o "$WORK/ilmain" -v quiet -nologo > "$WORK/log" 2>&1 \
     && "$DOTNET" "$CSHARPC_DLL" "$WORK/ilmain/IlApp.dll" -O0 -o "$WORK/ilmain-exe/" > "$WORK/log" 2>&1; then
    (cd "$WORK/il/IlApp" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected"
    "$WORK/ilmain-exe/IlApp" > "$WORK/actual" 2>&1; check "IlApp: csharpc IlApp.dll (the program as IL)" "$WORK/expected" "$WORK/actual"
  else echo "FAIL IlApp: csharpc IlApp.dll"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
fi

# 4. --framework-il (docs/adr/0013-framework-il.md): framework assemblies the program uses are compiled
#    from the shared framework's IL by default (IlMetadataApp, IlExtensionsApp above); the option turns
#    that off or forces assemblies in, and refuses what csharpc can't take as IL, all with diagnostics.
if [[ -z "$FILTER" || "framework-il" == *"$FILTER"* ]]; then
  expect_failure() { # name expected-text csharpc-args...
    local name="$1" text="$2"; shift 2
    if "$DOTNET" "$CSHARPC_DLL" "$@" -O0 -o "$WORK/fwil" > "$WORK/log" 2>&1; then
      echo "FAIL $name: compiled, but a diagnostic was expected"; fail=$((fail+1))
    elif grep -qF -- "$text" "$WORK/log"; then echo "PASS $name"; pass=$((pass+1))
    else echo "FAIL $name: wrong diagnostic"; sed 's/^/     /' "$WORK/log" | head; echo "     expected: $text"; fail=$((fail+1)); fi
  }
  metadata="$WORK/il/IlMetadataApp/Program.cs"
  expect_failure "--framework-il none" "'System.Reflection.PortableExecutable.PEReader" "$metadata" --framework-il none
  expect_failure "--framework-il System.Linq" "--framework-il: 'System.Linq' can't be compiled from IL: csharpc implements its type" "$metadata" --framework-il System.Linq
  # (native shims on Unix, its csharpc intrinsics on Windows, where it calls kernel32 directly)
  expect_failure "--framework-il System.Console" "--framework-il: 'System.Console' can't be compiled from IL" "$metadata" --framework-il System.Console
  expect_failure "--framework-il NoSuchAssembly" "--framework-il: 'NoSuchAssembly' is not an assembly of the shared framework" "$metadata" --framework-il NoSuchAssembly
fi

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
