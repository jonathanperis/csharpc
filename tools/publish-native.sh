#!/usr/bin/env bash
# Build csharpc itself as a NativeAOT executable: ~5x less CPU per compile than the
# JIT-hosted compiler (no JIT warm-up of Roslyn and csharpc on every run).
#
#   tools/publish-native.sh [output-dir]      (default: artifacts/csharpc-native)
#   artifacts/csharpc-native/csharpc hello.cs -o hello
#
# The output directory also holds runtime/ and corelib/, which csharpc finds next to itself.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/csharpc-native}"
RID="linux-$(uname -m | sed 's/x86_64/x64/; s/aarch64/arm64/')"
dotnet publish "$ROOT/src/Csharpc" -c Release -r "$RID" -p:PublishAot=true -o "$OUT"
rm -f "$OUT"/*.dbg
echo "csharpc: $OUT/csharpc"
