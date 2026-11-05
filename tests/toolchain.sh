#!/usr/bin/env bash
# Toolchain discovery: the C compiler that builds the runtime must match the libLLVM that reads its bitcode.
#
#   1. With no --cc / CSHARPC_CC, csharpc uses the clang of the LLVM install it loaded libLLVM from, not
#      whatever `clang` is first on PATH (Apple clang, or a clang from a newer LLVM, writes bitcode an
#      older libLLVM can't read).
#   2. When the runtime's bitcode can't be read, the error says why and what to set.
#
# Linux and macOS (Homebrew llvm@18 / apt's llvm-18); skipped elsewhere or when no such install exists.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$WORK"' EXIT
pass=0; fail=0

lib=""
for candidate in /usr/lib/llvm-18/lib/libLLVM.so "$(brew --prefix llvm@18 2>/dev/null)/lib/libLLVM.dylib"; do
  [[ -f "$candidate" && -x "$(dirname "$candidate")/../bin/clang" ]] && { lib="$candidate"; break; }
done
if [[ -z "$lib" || "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then echo "SKIP toolchain: no LLVM 18 install with a clang next to libLLVM"; exit 0; fi
real_clang="$(cd "$(dirname "$lib")/../bin" && pwd)/clang"

"$DOTNET" build "$ROOT/src/Csharpc" -c Release -v quiet -nologo > /dev/null || { echo "compiler build failed"; exit 1; }
CSHARPC_DLL="$ROOT/src/Csharpc/bin/Release/net11.0/csharpc.dll"
echo 'System.Console.WriteLine("hi");' > "$WORK/hello.cs"

# 1. A `clang` on PATH that is unusable: the install's own clang must be preferred.
mkdir -p "$WORK/fakebin"
printf '#!/bin/sh\necho "fake clang from PATH: must not be used" >&2\nexit 1\n' > "$WORK/fakebin/clang"
chmod +x "$WORK/fakebin/clang"
if env PATH="$WORK/fakebin:$PATH" XDG_CACHE_HOME="$WORK/cache1" CSHARPC_LIBLLVM="$lib" "$DOTNET" "$CSHARPC_DLL" "$WORK/hello.cs" -o "$WORK/hello" > "$WORK/log" 2>&1 \
   && [[ "$("$WORK/hello")" == "hi" ]]; then echo "PASS the clang next to libLLVM is preferred over PATH's"; pass=$((pass+1))
else echo "FAIL the clang next to libLLVM is preferred over PATH's"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi

# 2. A compiler whose bitcode libLLVM can't read: the message names the cause and the fix.
cat > "$WORK/badclang" <<SH
#!/bin/sh
"$real_clang" "\$@" || exit \$?
prev=""
for arg in "\$@"; do
  [ "\$prev" = "-o" ] && out="\$arg"
  prev="\$arg"
done
case " \$* " in *" -emit-llvm "*) printf 'not bitcode' > "\$out" ;; esac
SH
chmod +x "$WORK/badclang"
if env XDG_CACHE_HOME="$WORK/cache2" CSHARPC_CC="$WORK/badclang" CSHARPC_LIBLLVM="$lib" "$DOTNET" "$CSHARPC_DLL" "$WORK/hello.cs" -o "$WORK/hello2" > "$WORK/log" 2>&1; then
  echo "FAIL unreadable runtime bitcode: compiled anyway"; fail=$((fail+1))
elif grep -q "set CSHARPC_CC" "$WORK/log" && ! grep -q "Unhandled exception" "$WORK/log"; then echo "PASS unreadable runtime bitcode: the message says what to set"; pass=$((pass+1))
else echo "FAIL unreadable runtime bitcode: unhelpful message"; sed 's/^/     /' "$WORK/log" | head; fail=$((fail+1)); fi

echo
echo "$pass passed, $fail failed"
[[ $fail == 0 ]]
