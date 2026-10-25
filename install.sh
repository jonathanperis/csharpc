#!/usr/bin/env bash
# Install a csharpc release archive for this machine from GitHub Releases.
# Picks the RID, downloads the archive + SHA256SUMS, verifies, extracts to
# ~/.local/share/csharpc (override with CSHARPC_INSTALL_DIR) and puts a symlink on PATH
# (~/.local/bin/csharpc by default).
#
#   curl -fsSL https://raw.githubusercontent.com/jonathanperis/csharpc/main/install.sh | bash
#   ./install.sh [version]          # default: latest release tag (incl. prereleases)
#
# Prerequisites after install: clang (and ideally lld). libLLVM is in the archive.
set -euo pipefail

REPO="${CSHARPC_REPO:-jonathanperis/csharpc}"
VERSION="${1:-}"
PREFIX="${CSHARPC_INSTALL_DIR:-$HOME/.local/share/csharpc}"
BIN_DIR="${CSHARPC_BIN_DIR:-$HOME/.local/bin}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "install.sh: need '$1' on PATH" >&2; exit 1; }; }
need curl
need tar
need sha256sum

uname_s="$(uname -s)"
uname_m="$(uname -m)"
case "$uname_s/$uname_m" in
  Linux/x86_64)   RID=linux-x64 ;;
  Linux/aarch64)  RID=linux-arm64 ;;
  Darwin/arm64)   RID=osx-arm64 ;;
  *) echo "install.sh: unsupported platform $uname_s/$uname_m (supported: linux-x64, linux-arm64, osx-arm64; use install.ps1 on Windows)" >&2; exit 1 ;;
esac

api="https://api.github.com/repos/${REPO}/releases"
if [[ -z $VERSION ]]; then
  # Prefer the newest release (prereleases included) — csharpc's first tags are -spike.
  tag="$(curl -fsSL "$api" | grep -oP '"tag_name":\s*"\K[^"]+' | head -1 || true)"
  if [[ -z $tag ]]; then
    echo "install.sh: no releases found at $REPO" >&2
    exit 1
  fi
else
  tag="$VERSION"
  [[ $tag == v* ]] || tag="v$tag"
fi

ver="${tag#v}"
asset="csharpc-${ver}-${RID}.tar.gz"
base="https://github.com/${REPO}/releases/download/${tag}"
tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

echo "install.sh: downloading $asset ($tag)"
curl -fsSL "$base/$asset" -o "$tmpdir/$asset"
curl -fsSL "$base/SHA256SUMS" -o "$tmpdir/SHA256SUMS"
(cd "$tmpdir" && grep " $asset\$" SHA256SUMS | sha256sum -c -)

rm -rf "$PREFIX"
mkdir -p "$PREFIX" "$BIN_DIR"
tar -xzf "$tmpdir/$asset" -C "$PREFIX" --strip-components=1
ln -sfn "$PREFIX/csharpc" "$BIN_DIR/csharpc"

echo "install.sh: installed $tag ($RID) to $PREFIX"
echo "install.sh: symlink $BIN_DIR/csharpc"
if ! command -v clang >/dev/null 2>&1; then
  echo "install.sh: note: install clang (and lld) to compile programs — e.g. apt install clang-18 lld-18, or brew install llvm@18" >&2
fi
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *) echo "install.sh: add $BIN_DIR to PATH" >&2 ;;
esac
"$BIN_DIR/csharpc" --help >/dev/null 2>&1 || true
echo "install.sh: done. Try: csharpc hello.cs -o hello && ./hello"
