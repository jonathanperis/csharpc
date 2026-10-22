#!/usr/bin/env bash
# Downloads the pinned corpus sources (official Microsoft repositories) as tarballs at fixed commits
# and extracts only the directories the corpus uses. Nothing from the corpus is executed here.
#
#   tools/compat/fetch.sh [CORPUS_DIR]      (default /tmp/compat-corpus)
set -euo pipefail
CORPUS="${1:-/tmp/compat-corpus}"
# repo  commit  paths-to-extract...
REPOS=(
  "dotnet/samples ab42c8a4251cb90787aa5d3c94b78d28d85bc46b core/console-apps snippets/csharp csharp core/linq core/tutorials"
  "dotnet/docs 705acf6d28963fdb60e49ddd1c5437b3e0419a66 docs/csharp docs/standard docs/fundamentals docs/core"
)
mkdir -p "$CORPUS"
for line in "${REPOS[@]}"; do
  read -r repo sha paths <<< "$line"
  name="${repo#*/}"
  dest="$CORPUS/$name"
  if [[ -f "$dest/.sha" && "$(cat "$dest/.sha")" == "$sha" ]]; then echo "$repo@$sha already present"; continue; fi
  rm -rf "$dest"; mkdir -p "$dest"
  echo "fetching $repo@$sha"
  wild=(--wildcards); for p in $paths; do wild+=("*/$p/*"); done
  # Some paths may not exist in a repo; tar then reports "not found in archive" -- tolerate that.
  curl -fsSL "https://codeload.github.com/$repo/tar.gz/$sha" \
    | tar -xz -C "$dest" --strip-components=1 --no-same-owner --no-same-permissions \
        --exclude='*.png' --exclude='*.jpg' --exclude='*.gif' --exclude='*.svg' "${wild[@]}" || true
  echo "$sha" > "$dest/.sha"
done
