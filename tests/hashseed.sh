#!/usr/bin/env bash
# string.GetHashCode is Marvin seeded per process from the OS random number generator, as in .NET (issue #285, hash flooding):
# the same string hashes the same within a process and differently across processes. A recorded sample cannot check this
# (the value is not part of any output), so this script runs the compiled program three times.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/seed.cs" <<'EOF'
var a = "csharpc hash seed".GetHashCode();
var b = new string("csharpc hash seed".ToCharArray()).GetHashCode();
var empty = "".GetHashCode();
System.Console.WriteLine($"{a} {(a == b ? "same" : "DIFFERENT")} {empty}");
EOF

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed"; exit 1; }
"$DOTNET" "$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll" "$WORK/seed.cs" -o "$WORK/seed" > "$WORK/compile.log" 2>&1 \
  || { cat "$WORK/compile.log"; echo "FAIL hashseed: does not compile"; exit 1; }

outputs=()
for _ in 1 2 3; do outputs+=("$("$WORK/seed")"); done
if printf '%s\n' "${outputs[@]}" | grep -q DIFFERENT; then echo "FAIL hashseed: one string hashed two ways within a process"; exit 1; fi
distinct="$(printf '%s\n' "${outputs[@]}" | sort -u | wc -l)"
if (( distinct < 2 )); then echo "FAIL hashseed: three processes hashed alike (${outputs[0]}): the seed is not random"; exit 1; fi
echo "PASS hashseed: $distinct different hash values in 3 processes"
