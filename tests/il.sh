#!/usr/bin/env bash
# IL front-end tests (docs/adr/0010-il-frontend.md): programs whose libraries reach csharpc only as
# compiled assemblies. Real .NET is the oracle, as everywhere else.
#
#   tests/projects/il/<App>/<App>.csproj    console app (OutputType Exe) referencing libraries by <Reference>
#   tests/projects/il/<Lib>/<Lib>.csproj    libraries: built with `dotnet build -c Release` first; csharpc
#                                           never sees their sources, only bin/Release/net11.0/<Lib>.dll
#   tests/projects/il/<App>/expect-error    optional: the app must fail to compile with this diagnostic text
#   tests/projects/il/<App>/levels          optional: the levels to compile it at instead of -O0 -O2 (a large app's -O2
#                                           build costs more CI time than it adds: IlKestrelErrorsApp)
#   tests/projects/il/<App>/os              optional: the host OSes (linux, macos, windows) it runs on; skipped elsewhere
#                                           (a Kestrel server needs what csharpc has on Linux only: inotify, epoll)
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
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NuGetAudit=false
WORK="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT
FILTER="${1:-}"
pass=0; fail=0
case "$(uname -s)" in Darwin) HOST_OS=macos ;; MINGW*|MSYS*|CYGWIN*) HOST_OS=windows ;; *) HOST_OS=linux ;; esac

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
  if [[ -s "$dir/os" ]] && ! grep -qw "$HOST_OS" "$dir/os"; then
    echo "SKIP $app (runs on $(tr '\n' ' ' < "$dir/os")only)"; continue
  fi
  levels="-O0 -O2"
  [[ -s "$dir/levels" ]] && levels="$(cat "$dir/levels")"
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

# 3b2. A self-contained build as IL: the framework's implementation assemblies sit next to the app and are
#      not references (deps.json's runtime pack); csharpc binds and compiles its own framework instead.
if [[ -z "$FILTER" || "IlSelfContainedApp" == *"$FILTER"* ]]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) rid=osx-arm64 ;; Darwin-*) rid=osx-x64 ;; MINGW*|MSYS*|CYGWIN*) rid=win-x64 ;;
    Linux-aarch64|Linux-arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;;
  esac
  sc="$WORK/il-selfcontained/IlSelfContainedApp"
  mkdir -p "$WORK/il-selfcontained" && cp -r "$ROOT/tests/projects/il-selfcontained/IlSelfContainedApp" "$sc"
  (cd "$sc" && "$DOTNET" run -v quiet -p:IlRid="$rid" 2>/dev/null) > "$WORK/expected"
  rm -rf "$sc/bin" "$sc/obj"
  if "$DOTNET" build "$sc/IlSelfContainedApp.csproj" -c Release -p:IlRid="$rid" -o "$WORK/scmain" -v quiet -nologo > "$WORK/log" 2>&1 \
     && [[ -f "$WORK/scmain/System.Private.CoreLib.dll" ]] \
     && "$DOTNET" "$CSHARPC_DLL" "$WORK/scmain/IlSelfContainedApp.dll" -O0 -o "$WORK/scmain-exe/" > "$WORK/log" 2>&1; then
    "$WORK/scmain-exe/IlSelfContainedApp" > "$WORK/actual" 2>&1; check "IlSelfContainedApp: csharpc IlSelfContainedApp.dll (a self-contained build)" "$WORK/expected" "$WORK/actual"
  else echo "FAIL IlSelfContainedApp: csharpc IlSelfContainedApp.dll"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
fi

# 3c. A web app as IL: its runtimeconfig.json lists Microsoft.AspNetCore.App, which csharpc references as the
#     host would load it (no --framework).
if [[ -z "$FILTER" || "IlAspNetCoreApp" == *"$FILTER"* ]]; then
  (cd "$WORK/il/IlAspNetCoreApp" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected"
  if "$DOTNET" build "$WORK/il/IlAspNetCoreApp/IlAspNetCoreApp.csproj" -c Release -o "$WORK/webmain" -v quiet -nologo > "$WORK/log" 2>&1 \
     && grep -q '"Microsoft.AspNetCore.App"' "$WORK/webmain/IlAspNetCoreApp.runtimeconfig.json" \
     && "$DOTNET" "$CSHARPC_DLL" "$WORK/webmain/IlAspNetCoreApp.dll" -O0 -o "$WORK/webmain-exe/" > "$WORK/log" 2>&1; then
    (cd "$WORK/il/IlAspNetCoreApp" && "$WORK/webmain-exe/IlAspNetCoreApp") > "$WORK/actual" 2>&1
    check "IlAspNetCoreApp: csharpc IlAspNetCoreApp.dll (frameworks from runtimeconfig.json)" "$WORK/expected" "$WORK/actual"
  else echo "FAIL IlAspNetCoreApp: csharpc IlAspNetCoreApp.dll"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
fi

# 3d. RID-specific package assets: RidPkg (built here into a local feed) ships a portable lib/ assembly, a different
#     assembly under runtimes/<rid>/lib and a native library under runtimes/<rid>/native. The host picks the
#     runtime-specific ones; csharpc must too, from the project (NuGet restore for the target's RID) and from the
#     program as IL (deps.json runtimeTargets), and put the native library next to the executable.
if [[ -z "$FILTER" || "RidApp" == *"$FILTER"* ]]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) rid=osx-arm64; native_name=libridnative.dylib; native_flags="-dynamiclib" ;;
    Darwin-*) rid=osx-x64; native_name=libridnative.dylib; native_flags="-dynamiclib" ;;
    MINGW*|MSYS*|CYGWIN*) rid=win-x64; native_name=ridnative.dll; native_flags="-shared" ;;
    Linux-aarch64|Linux-arm64) rid=linux-arm64; native_name=libridnative.so; native_flags="-shared -fPIC" ;;
    *) rid=linux-x64; native_name=libridnative.so; native_flags="-shared -fPIC" ;;
  esac
  rd="$WORK/il-rid"; mkdir -p "$rd" && cp -r "$ROOT/tests/projects/il-rid/." "$rd/"
  feed="$rd/feed"
  # (the package is built once; every step below is plain dotnet and clang)
  if "$DOTNET" build "$rd/RidPkgPortable/RidPkg.csproj" -c Release -o "$rd/out-portable" -v quiet -nologo > "$WORK/log" 2>&1 \
     && "$DOTNET" build "$rd/RidPkgSpecific/RidPkg.csproj" -c Release -o "$rd/out-specific" -v quiet -nologo >> "$WORK/log" 2>&1 \
     && clang $native_flags -o "$rd/$native_name" "$rd/native/ridnative.c" >> "$WORK/log" 2>&1 \
     && "$DOTNET" pack "$rd/RidPackage/RidPkg.Pack.csproj" -c Release -o "$feed" -v quiet -nologo -p:PortableDll="$rd/out-portable/RidPkg.dll" \
          -p:SpecificDll="$rd/out-specific/RidPkg.dll" -p:PackRid="$rid" -p:NativeLibrary="$rd/$native_name" >> "$WORK/log" 2>&1; then
    feed_path="$(cd "$feed" && (pwd -W 2>/dev/null || pwd))"
    printf '<configuration><packageSources><clear/><add key="local" value="%s"/></packageSources></configuration>\n' "$feed_path" > "$rd/RidApp/nuget.config"
    (cd "$rd/RidApp" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected"
    rm -rf "$rd/RidApp/bin" "$rd/RidApp/obj"
    for opt in -O0 -O2; do
      if "$DOTNET" "$CSHARPC_DLL" "$rd/RidApp/RidApp.csproj" "$opt" -o "$WORK/rid$opt/RidApp" > "$WORK/log" 2>&1; then
        "$WORK/rid$opt/RidApp" > "$WORK/actual" 2>&1; check "RidApp $opt: runtimes/<rid> assets from the project" "$WORK/expected" "$WORK/actual"
      else echo "FAIL RidApp $opt: csharpc RidApp.csproj"; sed 's/^/     /' "$WORK/log" | head -20; fail=$((fail+1)); fi
    done
    rm -rf "$rd/RidApp/bin" "$rd/RidApp/obj"
    if "$DOTNET" build "$rd/RidApp/RidApp.csproj" -c Release -o "$WORK/ridmain" -v quiet -nologo > "$WORK/log" 2>&1 \
       && "$DOTNET" "$CSHARPC_DLL" "$WORK/ridmain/RidApp.dll" -O0 -o "$WORK/ridmain-exe/RidApp" > "$WORK/log" 2>&1; then
      "$WORK/ridmain-exe/RidApp" > "$WORK/actual" 2>&1; check "RidApp: runtimes/<rid> assets of the program as IL (deps.json)" "$WORK/expected" "$WORK/actual"
    else echo "FAIL RidApp: csharpc RidApp.dll"; sed 's/^/     /' "$WORK/log" | head -20; fail=$((fail+1)); fi
  else echo "FAIL RidApp: building the RidPkg test package"; sed 's/^/     /' "$WORK/log" | head -20; fail=$((fail+1)); fi
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
  expect_failure "--framework-il System.Collections" "--framework-il: 'System.Collections' can't be compiled from IL: csharpc implements its type" "$metadata" --framework-il System.Collections
  # (native shims on Unix, its csharpc intrinsics on Windows, where it calls kernel32 directly)
  expect_failure "--framework-il System.Console" "--framework-il: 'System.Console' can't be compiled from IL" "$metadata" --framework-il System.Console
  expect_failure "--framework-il NoSuchAssembly" "--framework-il: 'NoSuchAssembly' is not an assembly of the shared framework" "$metadata" --framework-il NoSuchAssembly
  # --il-first (docs/adr/0023-framework-il-first.md): the opt-in drop of an assembly's shadows; the runtime's own code and facades stay hand-implemented.
  expect_failure "--il-first NoSuchAssembly" "--il-first: 'NoSuchAssembly' is not an assembly of the shared framework" "$metadata" --il-first NoSuchAssembly
  expect_failure "--il-first System.Private.CoreLib" "--il-first: System.Private.CoreLib is the runtime's own code" "$metadata" --il-first System.Private.CoreLib
  expect_failure "--il-first System.Runtime" "--il-first: 'System.Runtime' is a facade of the runtime's CoreLib" "$metadata" --il-first System.Runtime
fi

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
