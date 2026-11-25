#!/usr/bin/env bash
# Prints the per-library table of ADR 0023 (libraries and CoreLib types served from the framework's IL) as one Markdown table,
# from its rows in docs/il-first/<library>.md: one file per library, so flips of different libraries never edit the same file.
#   tools/il-first/table.sh > table.md
set -euo pipefail
dir="$(cd "$(dirname "$0")/../.." && pwd)/docs/il-first"
first=1
for f in "$dir"/*.md; do
  [[ "$(basename "$f")" == README.md ]] && continue
  # The table in each file: its header and separator (printed once), then its rows.
  awk -v first="$first" '/^\| / { n++; if (n > 1 || first) print; next } /^\|---/ { if (first) print }' "$f"
  first=0
done
