#!/usr/bin/env bash
# Runs one or more single-file reproducers under real .NET (`dotnet run file.cs`) and csharpc and
# shows whether stdout and the exit code agree.
#
#   tools/compat/repro.sh tools/compat/repros/*.cs
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 TZ=UTC
CSHARPC_DLL="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
status=0
for src in "$@"; do
  name="$(basename "$src" .cs)"
  cp "$src" "$T/$name.cs"
  (cd "$T" && timeout 120 "$DOTNET" run "$name.cs" -p:WarningLevel=0 > "$T/want" 2> "$T/want.err" < /dev/null); want=$?
  timeout 600 "$DOTNET" "$CSHARPC_DLL" "$T/$name.cs" -o "$T/$name" > "$T/cc.log" 2>&1; cc=$?
  if [[ $cc != 0 ]]; then
    echo "== $name: csharpc failed (exit $cc)"; sed 's/^/   /' "$T/cc.log" | head -8; status=1; continue
  fi
  (cd "$T" && ulimit -c 0 && timeout 60 "$T/$name" > "$T/got" 2> "$T/got.err" < /dev/null); got=$?
  if cmp -s "$T/want" "$T/got" && [[ $want == "$got" ]]; then echo "== $name: same (exit $got)"
  else
    echo "== $name: DIFFERENT (dotnet exit $want, csharpc exit $got)"; status=1
    diff -u --label dotnet --label csharpc "$T/want" "$T/got" | head -30 | sed 's/^/   /'
    head -3 "$T/got.err" | sed 's/^/   stderr: /'
  fi
done
exit $status
