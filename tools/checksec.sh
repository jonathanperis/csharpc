#!/usr/bin/env bash
# checksec-style report for ELF executables, using only readelf.
#
#   tools/checksec.sh ./app [./other ...]      one row per binary (Markdown table)
#
# Columns: PIE, RELRO (partial/full), NX stack, stack canary (references __stack_chk_fail),
# FORTIFY (references *_chk functions), CET (IBT/SHSTK in .note.gnu.property), RPATH/RUNPATH.
set -uo pipefail

check() {
  local f="$1" pie relro nx canary fortify cet rpath
  local hdr dyn seg notes syms
  hdr=$(readelf -h "$f" 2>/dev/null) || { echo "| $f | not an ELF file |"; return; }
  dyn=$(readelf -d "$f" 2>/dev/null)
  seg=$(readelf -lW "$f" 2>/dev/null)
  notes=$(readelf -n "$f" 2>/dev/null)
  syms=$(readelf --dyn-syms -W "$f" 2>/dev/null; readelf -sW "$f" 2>/dev/null)

  if grep -q "Type:.*DYN" <<<"$hdr"; then
    if grep -q "PIE" <<<"$dyn" || grep -q "INTERP" <<<"$seg"; then pie=yes; else pie="DSO"; fi
  else pie=no; fi

  if grep -q "GNU_RELRO" <<<"$seg"; then
    if grep -Eq "BIND_NOW|FLAGS.*NOW" <<<"$dyn"; then relro=full; else relro=partial; fi
  else relro=no; fi

  if grep -E "GNU_STACK" <<<"$seg" | grep -q "RWE"; then nx=no; else nx=yes; fi
  if grep -q "__stack_chk_fail" <<<"$syms"; then canary=yes; else canary=no; fi
  if grep -Eq "__[a-z0-9_]+_chk(@|$| )" <<<"$syms"; then fortify=yes; else fortify=no; fi

  cet=""
  grep -q "IBT" <<<"$notes" && cet="IBT"
  grep -q "SHSTK" <<<"$notes" && cet="${cet:+$cet+}SHSTK"
  [[ -z "$cet" ]] && cet=no

  if grep -Eq "RPATH|RUNPATH" <<<"$dyn"; then rpath=yes; else rpath=no; fi
  printf '| %s | %s | %s | %s | %s | %s | %s | %s |\n' "$(basename "$f")" "$pie" "$relro" "$nx" "$canary" "$fortify" "$cet" "$rpath"
}

echo "| binary | PIE | RELRO | NX | canary | FORTIFY | CET | RPATH |"
echo "|---|---|---|---|---|---|---|---|"
for f in "$@"; do check "$f"; done
