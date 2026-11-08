#!/usr/bin/env bash
# Drop-in tests: real .NET projects built four ways, each compared against `dotnet run` output.
#
#   1. csharpc tests/projects/HelloProject/HelloProject.csproj            (CLI, project input)
#   2. dotnet build -p:UseCsharpc=true  (targets imported directly) (MSBuild integration)
#   3. dotnet run   -p:UseCsharpc=true                              (`dotnet run` runs native code)
#   4. dotnet publish -p:PublishCsharpc=true via the Csharpc.Sdk NuGet package from a local feed
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NuGetAudit=false
# Resolved path: on macOS mktemp's /var/folders/... is a symlinked path, and MSBuild's incremental clean
# deletes outputs it recorded under the other spelling (/private/var/...) of the same directory.
WORK="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT
source "$ROOT/tests/platform.sh"
pass=0; fail=0

check() { # name expected actual
  if diff -u "$2" "$3" > "$WORK/diff"; then echo "PASS $1"; pass=$((pass + 1));
  else echo "FAIL $1"; sed 's/^/     /' "$WORK/diff" | head -20; fail=$((fail + 1)); fi
}

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed"; exit 1; }
CSHARPC_DLL="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
TARGETS="$ROOT/src/Csharpc.Sdk/build/Csharpc.Sdk.targets"

for app in HelloProject; do
  src="$ROOT/tests/projects/$app"
  # Fresh copies of the sample tree so builds never pollute the repository.
  rm -rf "$WORK/samples"; cp -r "$ROOT/tests/projects" "$WORK/samples"; find "$WORK/samples" -name bin -o -name obj | xargs rm -rf
  proj="$WORK/samples/$app/$app.csproj"

  (cd "$WORK/samples/$app" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected" || { echo "FAIL $app: dotnet run failed"; fail=$((fail+1)); continue; }

  # 1. CLI
  if "$DOTNET" "$CSHARPC_DLL" "$proj" -o "$WORK/cli/" > "$WORK/log" 2>&1; then
    "$WORK/cli/$app$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "$app: csharpc <project>" "$WORK/expected" "$WORK/actual"
  else echo "FAIL $app: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi

  # 2. dotnet build -p:UseCsharpc=true
  common=(-p:UseCsharpc=true "-p:CustomAfterMicrosoftCommonTargets=$TARGETS" "-p:CsharpcCompilerPath=$CSHARPC_DLL")
  if "$DOTNET" build "$proj" -c Release "${common[@]}" > "$WORK/log" 2>&1; then
    exe="$WORK/samples/$app/bin/Release/net11.0/$app$EXE_SUFFIX"
    if file "$exe" | grep -qE "ELF|Mach-O|PE32\+" && ! grep -aq "You must install or update .NET" "$exe"; then
      "$exe" > "$WORK/actual" 2>&1; check "$app: dotnet build -p:UseCsharpc=true" "$WORK/expected" "$WORK/actual"
    else echo "FAIL $app: build output is not the native image"; fail=$((fail+1)); fi
  else echo "FAIL $app: dotnet build"; grep -E "error" "$WORK/log" | head | sed 's/^/     /'; fail=$((fail+1)); fi

  # 3. dotnet run -p:UseCsharpc=true
  (cd "$WORK/samples/$app" && "$DOTNET" run -c Release "${common[@]}" -v quiet 2>/dev/null) > "$WORK/actual"
  check "$app: dotnet run -p:UseCsharpc=true" "$WORK/expected" "$WORK/actual"

  # 4. NuGet package from a local feed + dotnet publish -p:PublishCsharpc=true
  "$DOTNET" pack "$ROOT/src/Csharpc.Sdk" -o "$WORK/feed" -v quiet > "$WORK/log" 2>&1 || { echo "FAIL pack"; tail "$WORK/log"; fail=$((fail+1)); continue; }
  feed="$WORK/feed"; [[ $HOST_OS == windows ]] && feed="$(cygpath -w "$feed")"  # NuGet reads it, not bash
  cat > "$WORK/samples/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="csharpc-local" value="$feed" />
  </packageSources>
</configuration>
EOF
  version="$(ls "$WORK/feed" | sed -n 's/^Csharpc\.Sdk\.\(.*\)\.nupkg$/\1/p')"
  (cd "$WORK/samples/$app" && "$DOTNET" add package Csharpc.Sdk --version "$version" > /dev/null 2>&1)
  if (cd "$WORK/samples/$app" && "$DOTNET" publish -c Release -p:PublishCsharpc=true -o "$WORK/publish" > "$WORK/log" 2>&1); then
    "$WORK/publish/$app$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "$app: dotnet publish -p:PublishCsharpc=true (NuGet)" "$WORK/expected" "$WORK/actual"
    leftovers="$(ls "$WORK/publish" | grep -E '\.dll$' || true)"
    [[ -z "$leftovers" ]] && { echo "PASS $app: publish output is a single native executable"; pass=$((pass+1)); } \
                          || { echo "FAIL $app: managed files left in publish dir: $leftovers"; grep -i -E "csharpc|delet" "$WORK/log" | head -20 | sed "s/^/     /"; fail=$((fail+1)); }
  else echo "FAIL $app: dotnet publish"; grep -E "error" "$WORK/log" | head | sed 's/^/     /'; fail=$((fail+1)); fi
done

# Project-level semantics (tests/projects/cli/<Name>/<Name>.csproj; other projects there are only
# referenced): `csharpc <project>` against `dotnet run`, both in their default (Debug) configuration.
for proj_src in "$ROOT"/tests/projects/cli/*/*.csproj; do
  app="$(basename "$proj_src" .csproj)"
  [[ "$(basename "$(dirname "$proj_src")")" == "$app" ]] && grep -q '<OutputType>Exe' "$proj_src" || continue
  rm -rf "$WORK/cli-samples"; cp -r "$ROOT/tests/projects/cli" "$WORK/cli-samples"; find "$WORK/cli-samples" -name bin -o -name obj | xargs rm -rf
  proj="$WORK/cli-samples/$app/$app.csproj"
  (cd "$WORK/cli-samples/$app" && "$DOTNET" run -v quiet 2>/dev/null) > "$WORK/expected" || { echo "FAIL $app: dotnet run failed"; fail=$((fail+1)); continue; }
  find "$WORK/cli-samples" -name bin -o -name obj | xargs rm -rf
  if "$DOTNET" "$CSHARPC_DLL" "$proj" -o "$WORK/cli/" > "$WORK/log" 2>&1; then
    "$WORK/cli/$app$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "$app: csharpc <project>" "$WORK/expected" "$WORK/actual"
  else echo "FAIL $app: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
done

# Projects csharpc must refuse with a diagnostic and exit code 1 (tests/projects/cli-errors/<Name>/expect-error
# holds the text; App/App.csproj is compiled): a failing referenced project is a diagnostic, never a crash.
for dir in "$ROOT"/tests/projects/cli-errors/*/; do
  name="$(basename "$dir")"
  [[ -f "$dir/expect-error" ]] || continue
  rm -rf "$WORK/cli-errors"; cp -r "$ROOT/tests/projects/cli-errors" "$WORK/cli-errors"
  "$DOTNET" "$CSHARPC_DLL" "$WORK/cli-errors/$name/App/App.csproj" -o "$WORK/cli-errors-out/" > "$WORK/log" 2>&1; code=$?
  if [[ $code != 1 ]]; then echo "FAIL $name: exit code $code, expected 1"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1))
  elif grep -q "Unhandled exception" "$WORK/log"; then echo "FAIL $name: unhandled exception"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1))
  elif grep -qF -f "$dir/expect-error" "$WORK/log"; then echo "PASS $name: diagnostic, exit code 1"; pass=$((pass+1))
  else echo "FAIL $name: wrong diagnostic"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
done

# NativeAOT's native-linking items (docs/16-interop-io.md, "Static archives and DirectPInvoke"): DirectPInvoke makes a library's
# [DllImport]s direct calls and NativeLibrary adds a static archive to the link line. tests/projects/native/DirectPInvoke
# calls a tiny C library, built here as a shared library (`dotnet run` loads it next to the program: the reference output)
# and as a static archive (linked into the csharpc executable, which is then run where no shared copy exists).
# Windows (MSVC-style libraries, import libraries) is not covered yet.
if [[ $HOST_OS == windows ]]; then
  echo "SKIP DirectPInvoke: static archives are not tested on Windows yet"
elif ! command -v clang > /dev/null || ! command -v ar > /dev/null; then
  echo "SKIP DirectPInvoke: no clang/ar to build the native library"
else
  native="$WORK/native"; rm -rf "$native"; mkdir -p "$native"; cp -r "$ROOT/tests/projects/native/DirectPInvoke" "$native/app"
  shared=libnativeadd.so; shared_flags=(-shared -fPIC); [[ $HOST_OS == macos ]] && { shared=libnativeadd.dylib; shared_flags=(-dynamiclib); }
  if clang -c -fPIC -O1 "$native/app/nativeadd.c" -o "$native/nativeadd.o" && ar rcs "$native/libnativeadd.a" "$native/nativeadd.o" \
     && clang "${shared_flags[@]}" "$native/nativeadd.o" -o "$native/$shared"; then
    (cd "$native/app" && "$DOTNET" run -v quiet -p:NativeShared="$native/$shared" 2>/dev/null) > "$WORK/expected" || { echo "FAIL DirectPInvoke: dotnet run failed"; fail=$((fail+1)); }
    rm -rf "$native/app/bin" "$native/app/obj"
    # Project items: <DirectPInvoke Include="nativeadd" /> and <NativeLibrary Include="libnativeadd.a" />.
    if "$DOTNET" "$CSHARPC_DLL" "$native/app/DirectPInvoke.csproj" -p:NativeArchive="$native/libnativeadd.a" -o "$native/out/" > "$WORK/log" 2>&1; then
      "$native/out/DirectPInvoke$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "DirectPInvoke: project items (DirectPInvoke + NativeLibrary)" "$WORK/expected" "$WORK/actual"
      if [[ $HOST_OS == linux ]]; then
        if ldd "$native/out/DirectPInvoke" | grep -q nativeadd; then echo "FAIL DirectPInvoke: the executable depends on a shared nativeadd"; fail=$((fail+1));
        else echo "PASS DirectPInvoke: the native library is linked in, not loaded"; pass=$((pass+1)); fi
      fi
    else echo "FAIL DirectPInvoke: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
    # The same through the command line: DirectPInvokeList item (entries as "library!entry" too) and --native-library.
    printf '%s\n' '# NativeAOT list: a library, or one entry point of it' 'nativeadd!native_add' 'nativeadd' > "$native/direct.txt"
    rm -rf "$native/app/bin" "$native/app/obj"
    if "$DOTNET" "$CSHARPC_DLL" "$native/app/DirectPInvoke.csproj" -p:DirectPInvokeListFile="$native/direct.txt" --native-library "$native/libnativeadd.a" -o "$native/out-list/" > "$WORK/log" 2>&1; then
      "$native/out-list/DirectPInvoke$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "DirectPInvoke: DirectPInvokeList item + --native-library" "$WORK/expected" "$WORK/actual"
    else echo "FAIL DirectPInvoke: DirectPInvokeList"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
    # A package that ships only a static archive (runtimes/<rid>/native/libnativeadd.a): the DirectPInvoke of the project links it.
    case "$(uname -s)-$(uname -m)" in
      Darwin-arm64) rid=osx-arm64 ;; Darwin-*) rid=osx-x64 ;; Linux-aarch64|Linux-arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;;
    esac
    rm -rf "$native/app/bin" "$native/app/obj"
    cp -r "$ROOT/tests/projects/native/NativeArchivePackage" "$native/pkg"
    if "$DOTNET" pack "$native/pkg/NativeArchivePkg.csproj" -c Release -o "$native/feed" -v quiet -nologo -p:NativeArchive="$native/libnativeadd.a" -p:PackRid="$rid" > "$WORK/log" 2>&1; then
      printf '<configuration><packageSources><clear/><add key="local" value="%s"/></packageSources></configuration>\n' "$native/feed" > "$native/app/nuget.config"
      if "$DOTNET" "$CSHARPC_DLL" "$native/app/DirectPInvoke.csproj" -p:NativePackage=true -o "$native/out-pkg/" > "$WORK/log" 2>&1; then
        "$native/out-pkg/DirectPInvoke$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "DirectPInvoke: static archive from a package's runtimes/<rid>/native" "$WORK/expected" "$WORK/actual"
        [[ ! -e "$native/out-pkg/libnativeadd.a" ]] && { echo "PASS DirectPInvoke: the archive is linked, not copied next to the executable"; pass=$((pass+1)); } \
                                                    || { echo "FAIL DirectPInvoke: the package's archive was copied next to the executable"; fail=$((fail+1)); }
      else echo "FAIL DirectPInvoke: project with a static-archive package"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
      rm -f "$native/app/nuget.config"
    else echo "FAIL DirectPInvoke: packing the static-archive package"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
    # The program as IL (a csc-built assembly), with ILC-style options.
    rm -rf "$native/app/bin" "$native/app/obj"
    if (cd "$native/app" && "$DOTNET" build -c Release -v quiet > "$WORK/log" 2>&1) \
       && "$DOTNET" "$CSHARPC_DLL" "$native/app/bin/Release/net11.0/DirectPInvoke.dll" --directpinvoke:nativeadd --native-library "$native/libnativeadd.a" -o "$native/out-il/" >> "$WORK/log" 2>&1; then
      "$native/out-il/DirectPInvoke$EXE_SUFFIX" > "$WORK/actual" 2>&1; check "DirectPInvoke: program as IL (--directpinvoke:nativeadd --native-library)" "$WORK/expected" "$WORK/actual"
    else echo "FAIL DirectPInvoke: program as IL"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
    # Explicit failures: a missing archive is reported by name, a direct import nothing defines is a link error.
    rm -rf "$native/app/bin" "$native/app/obj"
    "$DOTNET" "$CSHARPC_DLL" "$native/app/DirectPInvoke.csproj" --directpinvoke nativeadd --native-library "$native/missing.a" -o "$native/out-err/" > "$WORK/log" 2>&1; code=$?
    if [[ $code == 1 ]] && grep -q "NativeLibrary: file '.*missing.a' not found" "$WORK/log"; then echo "PASS DirectPInvoke: a missing NativeLibrary is a diagnostic"; pass=$((pass+1));
    else echo "FAIL DirectPInvoke: missing NativeLibrary (exit $code)"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
    "$DOTNET" "$CSHARPC_DLL" "$native/app/DirectPInvoke.csproj" --directpinvoke nativeadd -o "$native/out-err/" > "$WORK/log" 2>&1; code=$?
    if [[ $code != 0 ]] && grep -q "native_add" "$WORK/log"; then echo "PASS DirectPInvoke: an undefined direct import is a link error naming the symbol"; pass=$((pass+1));
    else echo "FAIL DirectPInvoke: undefined direct import (exit $code)"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
  else echo "FAIL DirectPInvoke: could not build the native library"; fail=$((fail+1)); fi
fi

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
