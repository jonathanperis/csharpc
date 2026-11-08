#!/usr/bin/env bash
# Regenerates tools/il-first/ownership.tsv (the ownership map of src/Csharpc/corelib, issue #191, docs/21-ownership.md).
#   tools/il-first/ownership.sh [--summary | --contract | --unmatched]
# Needs the pinned .NET SDK (global.json); reads the shared framework of the runtime it runs on.
set -euo pipefail
cd "$(dirname "$0")/../.."
[ -d "$HOME/.dotnet11" ] && export PATH="$HOME/.dotnet11:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec dotnet run tools/il-first/ownership.cs -- "$@"
