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
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
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
    "$WORK/cli/$app" > "$WORK/actual" 2>&1; check "$app: csharpc <project>" "$WORK/expected" "$WORK/actual"
  else echo "FAIL $app: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi

  # 2. dotnet build -p:UseCsharpc=true
  common=(-p:UseCsharpc=true "-p:CustomAfterMicrosoftCommonTargets=$TARGETS" "-p:CsharpcCompilerPath=$CSHARPC_DLL")
  if "$DOTNET" build "$proj" -c Release "${common[@]}" > "$WORK/log" 2>&1; then
    exe="$WORK/samples/$app/bin/Release/net11.0/$app"
    if file "$exe" | grep -q ELF && ! strings "$exe" | grep -q "You must install or update .NET"; then
      "$exe" > "$WORK/actual" 2>&1; check "$app: dotnet build -p:UseCsharpc=true" "$WORK/expected" "$WORK/actual"
    else echo "FAIL $app: build output is not the native image"; fail=$((fail+1)); fi
  else echo "FAIL $app: dotnet build"; grep -E "error" "$WORK/log" | head | sed 's/^/     /'; fail=$((fail+1)); fi

  # 3. dotnet run -p:UseCsharpc=true
  (cd "$WORK/samples/$app" && "$DOTNET" run -c Release "${common[@]}" -v quiet 2>/dev/null) > "$WORK/actual"
  check "$app: dotnet run -p:UseCsharpc=true" "$WORK/expected" "$WORK/actual"

  # 4. NuGet package from a local feed + dotnet publish -p:PublishCsharpc=true
  "$DOTNET" pack "$ROOT/src/Csharpc.Sdk" -o "$WORK/feed" -v quiet > "$WORK/log" 2>&1 || { echo "FAIL pack"; tail "$WORK/log"; fail=$((fail+1)); continue; }
  cat > "$WORK/samples/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="csharpc-local" value="$WORK/feed" />
  </packageSources>
</configuration>
EOF
  version="$(ls "$WORK/feed" | sed -n 's/^Csharpc\.Sdk\.\(.*\)\.nupkg$/\1/p')"
  (cd "$WORK/samples/$app" && "$DOTNET" add package Csharpc.Sdk --version "$version" > /dev/null 2>&1)
  if (cd "$WORK/samples/$app" && "$DOTNET" publish -c Release -p:PublishCsharpc=true -o "$WORK/publish" > "$WORK/log" 2>&1); then
    "$WORK/publish/$app" > "$WORK/actual" 2>&1; check "$app: dotnet publish -p:PublishCsharpc=true (NuGet)" "$WORK/expected" "$WORK/actual"
    leftovers="$(ls "$WORK/publish" | grep -E '\.dll$' || true)"
    [[ -z "$leftovers" ]] && { echo "PASS $app: publish output is a single native executable"; pass=$((pass+1)); } \
                          || { echo "FAIL $app: managed files left in publish dir: $leftovers"; fail=$((fail+1)); }
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
    "$WORK/cli/$app" > "$WORK/actual" 2>&1; check "$app: csharpc <project>" "$WORK/expected" "$WORK/actual"
  else echo "FAIL $app: csharpc <project>"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi
done

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
