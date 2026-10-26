#!/usr/bin/env bash
# Build the top-level DotnetTool pointer package that lists the RID-specific
# csharpc.<rid> packages. Does NOT invoke `dotnet pack` (that would rebuild the
# host RID and overwrite RID nupkgs). Writes a minimal DotnetTool v2 pointer.
#
#   tools/pack-tool-pointer.sh <version> [nupkg-dir]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?usage: pack-tool-pointer.sh <version> [nupkg-dir]}"
NUPKG_DIR="${2:-$ROOT/artifacts/release/nupkg}"
mkdir -p "$NUPKG_DIR"

RIDS=(linux-x64 linux-arm64 osx-arm64 win-x64)
missing=0
for rid in "${RIDS[@]}"; do
  if [[ ! -f $NUPKG_DIR/csharpc.${rid}.${VERSION}.nupkg ]]; then
    echo "pack-tool-pointer: missing csharpc.${rid}.${VERSION}.nupkg" >&2
    missing=1
  fi
done
if [[ $missing -ne 0 ]]; then
  echo "pack-tool-pointer: expected RID packages in $NUPKG_DIR:" >&2
  ls -la "$NUPKG_DIR" >&2 || true
  exit 1
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/tools/any/any" "$STAGE/package/services/metadata/core-properties"

rid_xml=""
for rid in "${RIDS[@]}"; do
  rid_xml+="    <RuntimeIdentifierPackage RuntimeIdentifier=\"${rid}\" Id=\"csharpc.${rid}\" />"$'\n'
done

cat > "$STAGE/tools/any/any/DotnetToolSettings.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<DotNetCliTool Version="2">
  <Commands>
    <Command Name="csharpc" />
  </Commands>
  <RuntimeIdentifierPackages>
${rid_xml}  </RuntimeIdentifierPackages>
</DotNetCliTool>
EOF

cat > "$STAGE/csharpc.nuspec" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>csharpc</id>
    <version>${VERSION}</version>
    <authors>csharpc contributors</authors>
    <description>csharpc: a C# compiler front-end for LLVM (Roslyn -&gt; LLVM IR -&gt; native). RID-specific NativeAOT tool; install with dotnet tool install -g csharpc.</description>
    <projectUrl>https://github.com/jonathanperis/csharpc</projectUrl>
    <license type="expression">MIT</license>
    <!-- nuget.org still requires licenseUrl when a license expression is set (legacy clients). -->
    <licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>
    <repository type="git" url="https://github.com/jonathanperis/csharpc" />
    <packageTypes>
      <packageType name="DotnetTool" />
    </packageTypes>
  </metadata>
</package>
EOF

cat > "$STAGE/[Content_Types].xml" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />
  <Default Extension="xml" ContentType="application/xml" />
  <Default Extension="nuspec" ContentType="application/octet" />
</Types>
EOF

mkdir -p "$STAGE/_rels"
cat > "$STAGE/_rels/.rels" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/csharpc.nuspec" Id="R1" />
  <Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/package/services/metadata/core-properties/core.psmdcp" Id="R2" />
</Relationships>
EOF

cat > "$STAGE/package/services/metadata/core-properties/core.psmdcp" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<coreProperties xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties">
  <id>csharpc</id>
  <version>${VERSION}</version>
  <authors>csharpc contributors</authors>
  <description>csharpc RID-specific NativeAOT tool pointer package</description>
</coreProperties>
EOF

POINTER="$NUPKG_DIR/csharpc.${VERSION}.nupkg"
rm -f "$POINTER"
(
  cd "$STAGE"
  if command -v zip >/dev/null 2>&1; then
    zip -qr "$POINTER" .
  else
    # bsdtar / Windows tar
    tar -a -cf "$POINTER" .
  fi
)

test -f "$POINTER"
echo "pack-tool-pointer: wrote $POINTER ($(wc -c < "$POINTER" | tr -d ' ') bytes)"
echo "$POINTER"
