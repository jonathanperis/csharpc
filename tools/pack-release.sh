#!/usr/bin/env bash
# Build a platform release of csharpc: NativeAOT RID tool package + matching archive.
# The archive and the `csharpc.<rid>.*.nupkg` carry the same layout (binary, corelib/,
# runtime/, lib/libLLVM). The pointer package (`csharpc.*.nupkg`) is built separately
# once all RID packages exist (tools/pack-tool-pointer.sh).
#
# Usage:
#   tools/pack-release.sh <rid> [output-dir]
#   rid: linux-x64 | linux-arm64 | osx-arm64 | win-x64
#
# Env: CSHARPC_VERSION (default: Version from the csproj), CSHARPC_LIBLLVM (required if
# not discoverable on this host).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:?usage: pack-release.sh <rid> [output-dir]}"
OUT_ROOT="${2:-$ROOT/artifacts/release}"
ARCHIVE_DIR="$OUT_ROOT"
NUPKG_DIR="$OUT_ROOT/nupkg"
if [[ -z ${CSHARPC_VERSION:-} ]]; then
  VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/src/Csharpc/Csharpc.csproj" | head -1)"
else
  VERSION="$CSHARPC_VERSION"
fi
bytes_of() { wc -c < "$1" | tr -d '[:space:]'; }
dir_bytes() { find "$1" -type f -exec wc -c {} + 2>/dev/null | tail -1 | awk '{print $1}'; }

case "$RID" in
  linux-x64|linux-arm64|osx-arm64|win-x64) ;;
  *) echo "unsupported rid: $RID" >&2; exit 2 ;;
esac

resolve_libllvm() {
  if [[ -n ${CSHARPC_LIBLLVM:-} && -e ${CSHARPC_LIBLLVM} ]]; then
    printf '%s' "$CSHARPC_LIBLLVM"
    return
  fi
  case "$RID" in
    linux-*)
      for candidate in \
        /usr/lib/llvm-18/lib/libLLVM.so.18.1 \
        /usr/lib/llvm-18/lib/libLLVM.so \
        /usr/lib/*/libLLVM.so.18.1 \
        /usr/lib/libLLVM.so.18.1; do
        if [[ -e $candidate ]]; then printf '%s' "$candidate"; return; fi
      done
      ;;
    osx-arm64)
      local brew_prefix
      brew_prefix="$(brew --prefix llvm@18 2>/dev/null || true)"
      if [[ -n $brew_prefix && -e $brew_prefix/lib/libLLVM.dylib ]]; then
        printf '%s' "$brew_prefix/lib/libLLVM.dylib"
        return
      fi
      ;;
    win-x64)
      local clang_bin bin_dir
      clang_bin="$(command -v clang || true)"
      if [[ -n $clang_bin ]]; then
        bin_dir="$(cd "$(dirname "$clang_bin")" && pwd)"
        if [[ -e $bin_dir/libLLVM-18.dll ]]; then printf '%s' "$bin_dir/libLLVM-18.dll"; return; fi
      fi
      ;;
  esac
  echo "pack-release: libLLVM 18 not found; set CSHARPC_LIBLLVM" >&2
  exit 1
}

LIBLLVM="$(resolve_libllvm)"
export CSHARPC_LIBLLVM="$LIBLLVM"
mkdir -p "$ARCHIVE_DIR" "$NUPKG_DIR"

# Microsoft.NETCore.App.Ref (net11.0): NativeAOT csharpc has no TRUSTED_PLATFORM_ASSEMBLIES
# list, so binding needs these DLLs next to the binary when no SDK is installed.
resolve_ref_pack() {
  local root="${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}"
  local pack_root="$root/packs/Microsoft.NETCore.App.Ref"
  if [[ ! -d $pack_root ]]; then
    echo "pack-release: missing $pack_root (need the .NET 11 SDK to pack)" >&2
    exit 1
  fi
  local best=""
  local d
  for d in "$pack_root"/*; do
    [[ -d $d/ref/net11.0 ]] || continue
    best=$d
  done
  if [[ -z $best ]]; then
    echo "pack-release: no net11.0 reference pack under $pack_root" >&2
    exit 1
  fi
  printf '%s' "$best/ref/net11.0"
}
REF_PACK="$(resolve_ref_pack)"
echo "pack-release: reference pack=$REF_PACK"

echo "pack-release: packing NativeAOT RID tool for $RID (version $VERSION)"
echo "pack-release: libLLVM=$LIBLLVM"
# Pack (not bare publish): produces csharpc.$RID.$VERSION.nupkg with corelib/, runtime/, lib/.
dotnet pack "$ROOT/src/Csharpc" -c Release -r "$RID" -o "$NUPKG_DIR" \
  -p:PublishAot=true \
  -p:CsharpcPackRidTools=true \
  -p:CsharpcBundleLibLLVM=true \
  -p:CsharpcBundleRefPack="$REF_PACK" \
  -p:Version="$VERSION" \
  -p:PackageVersion="$VERSION" \
  -p:CsharpcLibLLVM="$LIBLLVM" \
  -v:m

NUPKG="$NUPKG_DIR/csharpc.${RID}.${VERSION}.nupkg"
test -f "$NUPKG" || { echo "pack-release: missing $NUPKG" >&2; ls -la "$NUPKG_DIR" >&2; exit 1; }

# Materialize the same layout as a release archive from the publish directory the pack used.
PUBLISH="$ROOT/src/Csharpc/bin/Release/net11.0/${RID}/publish"
if [[ ! -d $PUBLISH ]]; then
  # Fallback: extract the nupkg's tools payload.
  PUBLISH="$OUT_ROOT/staging-$RID"
  rm -rf "$PUBLISH"
  mkdir -p "$PUBLISH"
  unzip -q -o "$NUPKG" "tools/*" -d "$OUT_ROOT/nupkg-extract-$RID"
  # tools/any/<rid>/...
  src="$(find "$OUT_ROOT/nupkg-extract-$RID/tools" -type d -name "$RID" | head -1)"
  cp -a "$src"/. "$PUBLISH"/
fi

# Ensure lib alias + license (MSBuild copies the primary libLLVM file).
mkdir -p "$PUBLISH/lib"
case "$RID" in
  linux-*)
    if [[ ! -e $PUBLISH/lib/libLLVM.so.18.1 ]]; then
      cp -L "$LIBLLVM" "$PUBLISH/lib/libLLVM.so.18.1"
    fi
    ln -sfn libLLVM.so.18.1 "$PUBLISH/lib/libLLVM-18.so"
    ;;
  osx-arm64)
    if [[ ! -e $PUBLISH/lib/libLLVM.dylib ]]; then
      cp -L "$LIBLLVM" "$PUBLISH/lib/libLLVM.dylib"
    fi
    ;;
  win-x64)
    if [[ ! -e $PUBLISH/lib/libLLVM-18.dll ]]; then
      cp -L "$LIBLLVM" "$PUBLISH/lib/libLLVM-18.dll"
    fi
    ;;
esac
cat > "$PUBLISH/lib/LLVM-LICENSE.txt" <<'EOF'
This directory contains a binary build of LLVM's libLLVM (the C API shared
library), redistributed under the Apache License v2.0 with LLVM Exceptions:
https://llvm.org/LICENSE.txt

csharpc itself is MIT; see the repository LICENSE.
EOF
rm -f "$PUBLISH"/*.dbg "$PUBLISH"/*.pdb 2>/dev/null || true

test -d "$PUBLISH/corelib" || { echo "pack-release: missing corelib/ in publish output" >&2; exit 1; }
test -d "$PUBLISH/runtime" || { echo "pack-release: missing runtime/ in publish output" >&2; exit 1; }
if [[ $RID == win-x64 ]]; then
  test -f "$PUBLISH/csharpc.exe" || { echo "pack-release: missing csharpc.exe" >&2; exit 1; }
else
  test -f "$PUBLISH/csharpc" || { echo "pack-release: missing csharpc" >&2; exit 1; }
fi

NAME="csharpc-${VERSION}-${RID}"
BUNDLE="$OUT_ROOT/$NAME"
rm -rf "$BUNDLE"
mkdir -p "$BUNDLE"
cp -a "$PUBLISH"/. "$BUNDLE"/

if [[ $RID == win-x64 ]]; then
  ARCHIVE="$ARCHIVE_DIR/${NAME}.zip"
  rm -f "$ARCHIVE"
  # Prefer Info-ZIP or Python: Compress-Archive stores backslash separators, which Info-ZIP
  # unzip (Git Bash) treats as literal characters and exits 1 on the warning.
  if command -v zip >/dev/null 2>&1; then
    (cd "$OUT_ROOT" && zip -qr "$ARCHIVE" "$NAME")
  else
    python="${PYTHON:-}"
    if [[ -z $python ]]; then
      for candidate in python3 python py; do
        if command -v "$candidate" >/dev/null 2>&1; then python=$candidate; break; fi
      done
    fi
    if [[ -n $python ]]; then
      "$python" - "$OUT_ROOT" "$NAME" "$ARCHIVE" <<'PY'
import os, sys, zipfile
out_root, name, archive = sys.argv[1], sys.argv[2], sys.argv[3]
root = os.path.join(out_root, name)
with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for dirpath, _, filenames in os.walk(root):
        for filename in filenames:
            path = os.path.join(dirpath, filename)
            # Always forward slashes in the zip (portable across unzip implementations).
            arcname = os.path.relpath(path, out_root).replace(os.sep, "/")
            zf.write(path, arcname)
PY
    elif command -v tar >/dev/null 2>&1; then
      (cd "$OUT_ROOT" && tar -a -cf "$(basename "$ARCHIVE")" "$NAME")
    else
      echo "pack-release: need zip, python, or tar to build the Windows archive" >&2
      exit 1
    fi
  fi
else
  ARCHIVE="$ARCHIVE_DIR/${NAME}.tar.gz"
  rm -f "$ARCHIVE"
  tar -C "$OUT_ROOT" -czf "$ARCHIVE" "$NAME"
fi

{
  echo "rid=$RID"
  echo "version=$VERSION"
  echo "archive=$(basename "$ARCHIVE")"
  echo "archive_bytes=$(bytes_of "$ARCHIVE")"
  echo "nupkg=$(basename "$NUPKG")"
  echo "nupkg_bytes=$(bytes_of "$NUPKG")"
  if [[ $RID == win-x64 ]]; then
    echo "binary_bytes=$(bytes_of "$BUNDLE/csharpc.exe")"
  else
    echo "binary_bytes=$(bytes_of "$BUNDLE/csharpc")"
  fi
  echo "libllvm_bytes=$(dir_bytes "$BUNDLE/lib")"
  echo "bundle_bytes=$(dir_bytes "$BUNDLE")"
} | tee "$ARCHIVE_DIR/${NAME}.sizes.txt"

echo "pack-release: wrote $ARCHIVE"
echo "pack-release: wrote $NUPKG"
echo "$ARCHIVE"
