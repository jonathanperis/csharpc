#!/usr/bin/env bash
# The IL survey's `aspnetcore` target (docs/18-compatibility.md#aspnet-core-and-the-techempower-app):
# every method of the ASP.NET Core assemblies the TechEmpower app (benchmarks/techempower/app) reaches
# — Microsoft.AspNetCore.*, Microsoft.Extensions.*, Microsoft.Net.Http.Headers, System.IO.Pipelines,
# System.Net.* — lowered by `csharpc --il-survey`, ranked by the number of methods each item stops.
#
#   tools/il-survey/aspnetcore.sh [--shards N --shard-index I] [--out FILE] [other survey.py options]
#
# The assemblies are the ones csharpc compiles from the shared frameworks' IL for the app (`csharpc -v`
# prints the choice), minus the Windows-only servers (IIS, HTTP.sys), which no Linux build reaches.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CSHARPC="${CSHARPC:-$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll}"
WORK="${SURVEY_WORK:-$(mktemp -d)}"
mkdir -p "$WORK"

dotnet "$CSHARPC" "$ROOT/benchmarks/techempower/app/TechEmpower.csproj" -v -O0 --emit llvm-bc -o "$WORK/app.bc" > "$WORK/app.log" 2>&1 || true
mapfile -t names < <(sed -n 's/^  framework IL: \([^ ]*\) .*/\1/p' "$WORK/app.log" | sort -u)
(( ${#names[@]} > 0 )) || { echo "aspnetcore.sh: csharpc chose no framework assemblies for the app:" >&2; tail -20 "$WORK/app.log" >&2; exit 1; }

shared="$(dirname "$(dirname "$(dotnet --list-runtimes | sed -n 's/^Microsoft.NETCore.App \(11\.[^ ]*\) \[\(.*\)\]$/\2\/\1/p' | sort -V | tail -1)")")"
netcore="$(ls -d "$shared"/Microsoft.NETCore.App/11.* | sort -V | tail -1)"
aspnet="$(ls -d "$shared"/Microsoft.AspNetCore.App/11.* | sort -V | tail -1)"
dlls=() surveyed=()
for name in "${names[@]}"; do
  if [[ -f "$aspnet/$name.dll" ]]; then dlls+=("$aspnet/$name.dll"); else dlls+=("$netcore/$name.dll"); fi
  case "$name" in
    Microsoft.AspNetCore.Server.IIS*|Microsoft.AspNetCore.Server.HttpSys) ;;
    Microsoft.AspNetCore.*|Microsoft.Extensions.*|Microsoft.Net.Http.Headers|System.IO.Pipelines|System.Net.*) surveyed+=("$name") ;;
  esac
done
echo "aspnetcore.sh: ${#dlls[@]} assemblies from IL, surveying ${#surveyed[@]}: ${surveyed[*]}"
IFS=,
exec python3 -I "$ROOT/tools/il-survey/survey.py" --no-packages --csharpc "$CSHARPC" --local "aspnetcore=${dlls[*]}" \
  --survey-assemblies "${surveyed[*]}" --rank-by methods --work "$WORK" "$@"
