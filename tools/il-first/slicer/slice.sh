#!/usr/bin/env bash
# Cuts the part of System.Private.CoreLib's IL that the given types need into a slice assembly (issue #191 part B, docs/21-ownership.md).
#
#   tools/il-first/slicer/slice.sh <out.dll> <root type[,root type]> [analyze] [slicer args]
#
# Root types are CoreLib metadata names with '!' for the backtick: 'System.Collections.Generic.List!1'. The result is the assembly
# <out.dll> and <out>.map; use them with
#
#   CSHARPC_UPSTREAM_MAP=<out>.map csharpc -r <out.dll> app.cs -o app
#
# Contract choices the spike made for List, Dictionary, HashSet and StringBuilder (all explicit below):
#   --replace  SR.InternalGetResourceString goes to csharpc's own lookup; every reached SR getter carries its text as a literal
#   --redirect the internal string-comparer family (Marvin, OrdinalIgnoreCase casing over ICU) is csharpc's own type of the same shape
#              (corelib/UpstreamSupport.cs); ArraySortHelper<T> comes from the framework's IL (issue #224: its
#              RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter is expanded at compile time)
#   --stop     the Interop.Sys / Interop.Globalization imports stay outside (libSystem.Native shims, other agents' work)
# Needs Mono.Cecil from the ILLink tasks package the SDK restores (~/.nuget/packages/microsoft.net.illink.tasks/<sdk version>).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
export PATH="$HOME/.dotnet11:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
bin="${SLICER_BIN:-/tmp/csharpc-slicer}"
[ -f "$bin/Slicer.dll" ] || dotnet build "$here" -c Release -o "$bin" > /dev/null
out="$1"; roots="$2"; shift 2
mode=emit
if [ "${1:-}" = analyze ]; then mode=analyze; shift; fi
# The contract choices live in the slicer's "csharpc" profile (shared with the build, src/Csharpc/Csharpc.csproj).
exec dotnet "$bin/Slicer.dll" "$mode" --roots "$roots" --profile csharpc --out "$out" "$@"
