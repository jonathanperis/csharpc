#!/usr/bin/env bash
# Build a platform release bundle of csharpc: NativeAOT executable + corelib/ + runtime/ +
# bundled libLLVM under lib/. Measured (linux-x64, 2026-10-25): NativeAOT binary ~42 MiB,
# ~19 s self-contained R2R vs ~61 s NativeAOT; NativeAOT chosen so a clean machine needs no
# .NET SDK or runtime — only clang (and lld when available) to compile/link user programs.
#
# Usage:
#   tools/pack-release.sh <rid> [output-dir]
#   rid: linux-x64 | linux-arm64 | osx-arm64 | win-x64
#
# Writes <output-dir>/csharpc-<rid>.tar.gz (or .zip on Windows) and prints its path.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:?usage: pack-release.sh <rid> [output-dir]}"
OUT_ROOT="${2:-$ROOT/artifacts/release}"
STAGE="$OUT_ROOT/staging-$RID"
ARCHIVE_DIR="$OUT_ROOT"
if [[ -z ${CSHARPC_VERSION:-} ]]; then
  VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/src/Csharpc/Csharpc.csproj" | head -1)"
else
  VERSION="$CSHARPC_VERSION"
fi
bytes_of() { wc -c < "$1" | tr -d '[:space:]'; }
dir_bytes() {
  # Portable: sum file sizes (macOS has no du -sb).
  find "$1" -type f -exec wc -c {} + 2>/dev/null | tail -1 | awk '{print $1}'
}

case "$RID" in
  linux-x64|linux-arm64|osx-arm64|win-x64) ;;
  *) echo "unsupported rid: $RID" >&2; exit 2 ;;
esac

rm -rf "$STAGE"
mkdir -p "$STAGE" "$ARCHIVE_DIR"

echo "pack-release: publishing NativeAOT csharpc for $RID (version $VERSION)"
dotnet publish "$ROOT/src/Csharpc" -c Release -r "$RID" -p:PublishAot=true -o "$STAGE" -v:m
# NativeAOT leaves a .dbg / .pdb next to the binary; drop them from the user-facing archive.
rm -f "$STAGE"/*.dbg "$STAGE"/*.pdb "$STAGE"/*.xml 2>/dev/null || true

bundle_libllvm() {
  mkdir -p "$STAGE/lib"
  case "$RID" in
    linux-*)
      # Prefer the soname file; fall back to the unversioned symlink target.
      local src=""
      for candidate in \
        /usr/lib/llvm-18/lib/libLLVM.so.18.1 \
        /usr/lib/llvm-18/lib/libLLVM.so \
        /usr/lib/*/libLLVM.so.18.1 \
        /usr/lib/libLLVM.so.18.1; do
        if [[ -e $candidate ]]; then src=$candidate; break; fi
      done
      if [[ -z $src ]]; then
        echo "pack-release: libLLVM 18 not found; install libllvm18 / llvm-18" >&2
        exit 1
      fi
      cp -L "$src" "$STAGE/lib/libLLVM.so.18.1"
      ln -sf libLLVM.so.18.1 "$STAGE/lib/libLLVM-18.so"
      ;;
    osx-arm64)
      local brew_prefix
      brew_prefix="$(brew --prefix llvm@18 2>/dev/null || true)"
      local src="${CSHARPC_LIBLLVM:-}"
      if [[ -z $src && -n $brew_prefix && -e $brew_prefix/lib/libLLVM.dylib ]]; then
        src="$brew_prefix/lib/libLLVM.dylib"
      fi
      if [[ -z $src || ! -e $src ]]; then
        echo "pack-release: libLLVM.dylib not found; install brew llvm@18 or set CSHARPC_LIBLLVM" >&2
        exit 1
      fi
      cp -L "$src" "$STAGE/lib/libLLVM.dylib"
      ;;
    win-x64)
      local src="${CSHARPC_LIBLLVM:-}"
      if [[ -z $src ]]; then
        # LLVM-MinGW puts libLLVM-18.dll on PATH next to clang.
        local clang_bin
        clang_bin="$(command -v clang || true)"
        if [[ -n $clang_bin ]]; then
          local bin_dir
          bin_dir="$(cd "$(dirname "$clang_bin")" && pwd)"
          if [[ -e $bin_dir/libLLVM-18.dll ]]; then src="$bin_dir/libLLVM-18.dll"; fi
        fi
      fi
      if [[ -z $src || ! -e $src ]]; then
        echo "pack-release: libLLVM-18.dll not found; install LLVM-MinGW or set CSHARPC_LIBLLVM" >&2
        exit 1
      fi
      cp -L "$src" "$STAGE/lib/libLLVM-18.dll"
      ;;
  esac
  # SPDX notice for the bundled shared library (Apache-2.0 WITH LLVM-exception).
  cat > "$STAGE/lib/LLVM-LICENSE.txt" <<'EOF'
This directory contains a binary build of LLVM's libLLVM (the C API shared
library), redistributed under the Apache License v2.0 with LLVM Exceptions:
https://llvm.org/LICENSE.txt

csharpc itself is MIT; see the repository LICENSE.
EOF
}

bundle_libllvm

# Layout check: corelib and runtime must sit next to the executable (AppContext.BaseDirectory).
test -d "$STAGE/corelib" || { echo "pack-release: missing corelib/" >&2; exit 1; }
test -d "$STAGE/runtime" || { echo "pack-release: missing runtime/" >&2; exit 1; }
if [[ $RID == win-x64 ]]; then
  test -f "$STAGE/csharpc.exe" || { echo "pack-release: missing csharpc.exe" >&2; exit 1; }
else
  test -f "$STAGE/csharpc" || { echo "pack-release: missing csharpc" >&2; exit 1; }
fi

NAME="csharpc-${VERSION}-${RID}"
# Stage under a versioned top-level directory so extractors get a single folder.
BUNDLE="$OUT_ROOT/$NAME"
rm -rf "$BUNDLE"
mkdir -p "$BUNDLE"
# Copy contents (not the staging dir itself) so the archive root is csharpc-<ver>-<rid>/.
cp -a "$STAGE"/. "$BUNDLE"/

if [[ $RID == win-x64 ]]; then
  ARCHIVE="$ARCHIVE_DIR/${NAME}.zip"
  rm -f "$ARCHIVE"
  if command -v zip >/dev/null 2>&1; then
    (cd "$OUT_ROOT" && zip -qr "$ARCHIVE" "$NAME")
  else
    # Windows runners without Info-ZIP: PowerShell's Compress-Archive.
    powershell.exe -NoProfile -Command \
      "Compress-Archive -Path (Join-Path '$OUT_ROOT' '$NAME') -DestinationPath '$ARCHIVE' -Force"
  fi
else
  ARCHIVE="$ARCHIVE_DIR/${NAME}.tar.gz"
  rm -f "$ARCHIVE"
  tar -C "$OUT_ROOT" -czf "$ARCHIVE" "$NAME"
fi


# Record sizes for the release log / status doc.
{
  echo "rid=$RID"
  echo "version=$VERSION"
  echo "archive=$(basename "$ARCHIVE")"
  echo "archive_bytes=$(bytes_of "$ARCHIVE")"
  if [[ $RID == win-x64 ]]; then
    echo "binary_bytes=$(bytes_of "$BUNDLE/csharpc.exe")"
  else
    echo "binary_bytes=$(bytes_of "$BUNDLE/csharpc")"
  fi
  echo "libllvm_bytes=$(dir_bytes "$BUNDLE/lib")"
  echo "bundle_bytes=$(dir_bytes "$BUNDLE")"
} | tee "$ARCHIVE_DIR/${NAME}.sizes.txt"

echo "pack-release: wrote $ARCHIVE"
echo "$ARCHIVE"
