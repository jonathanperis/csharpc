#!/usr/bin/env bash
# checksec-style report for ELF executables, using only readelf.
#
#   tools/checksec.sh ./app [./other ...]          one row per binary (Markdown table)
#   tools/checksec.sh --check ./app [./other ...]  exit 1 unless each has csharpc's default hardening
#                                                  (PIE, full RELRO, NX stack, separate code) and a
#                                                  segment layout that loads with any page size the
#                                                  architecture allows (tests/size.sh runs this)
#
# Columns: PIE, RELRO (partial/full), NX stack, stack canary (references __stack_chk_fail),
# FORTIFY (references *_chk functions), CET (IBT/SHSTK in .note.gnu.property), RPATH/RUNPATH,
# separate code (no 4 KiB file page holds both code and anything else: ELF headers, constants,
# data), and the largest page size the PT_LOAD layout is valid for.
set -uo pipefail

# "<offset> <vaddr> <filesz> <memsz> <flags> <align>" per PT_LOAD, in decimal (flags without spaces,
# e.g. "RE"). POSIX awk: no strtonum.
loads() {
  awk 'function hex(s,   v, i) { s = tolower(s); sub(/^0x/, "", s); v = 0
                                 for (i = 1; i <= length(s); i++) v = v * 16 + index("0123456789abcdef", substr(s, i, 1)) - 1
                                 return v }
       $1 == "LOAD" { f = ""; for (i = 7; i < NF; i++) f = f $i
                      printf "%.0f %.0f %.0f %.0f %s %.0f\n", hex($2), hex($3), hex($5), hex($6), f, hex($NF) }' <<<"$1"
}

# Separate code at 4 KiB granularity: the file pages of executable segments overlap neither the
# file pages of other segments nor the ELF and program headers.
separate_code() {
  local seg="$1" hdr_end="$2"
  loads "$seg" | awk -v hdr_end="$hdr_end" '
    { off = $1; size = $3; if (size == 0) next
      n++; lo[n] = int(off / 4096); hi[n] = int((off + size + 4095) / 4096); x[n] = ($5 ~ /E/) }
    END {
      for (i = 1; i <= n; i++) if (x[i]) {
        if (lo[i] < int((hdr_end + 4095) / 4096)) { print "no"; exit }
        for (j = 1; j <= n; j++) if (!x[j] && lo[j] < hi[i] && lo[i] < hi[j]) { print "no"; exit }
      }
      print "yes" }'
}

# Largest page size P (4, 16 or 64 KiB) the kernel and ld.so can map the PT_LOADs with: p_align >= P,
# p_offset congruent to p_vaddr modulo p_align, and no two segments sharing a P-sized page of memory.
max_page() {
  loads "$1" | awk '
    { n++; off[n] = $1; va[n] = $2; mem[n] = $4; al[n] = $6 }
    END {
      best = 0
      for (p = 4096; p <= 65536; p *= 4) {
        ok = 1
        for (i = 1; i <= n; i++) {
          if (al[i] < p || (va[i] - off[i]) % al[i] != 0) ok = 0
          for (j = i + 1; j <= n; j++)
            if (int((va[i] + mem[i] - 1) / p) >= int(va[j] / p) && int((va[j] + mem[j] - 1) / p) >= int(va[i] / p)) ok = 0
        }
        if (ok) best = p
      }
      print best ? best / 1024 "K" : "invalid" }'
}

check() {
  local f="$1" pie relro nx canary fortify cet rpath sepcode page
  local hdr dyn seg notes syms hdr_end
  hdr=$(readelf -h "$f" 2>/dev/null) || { echo "| $f | not an ELF file |"; return 1; }
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

  # The ELF header and the program headers that follow it (e_phoff + e_phnum * e_phentsize).
  hdr_end=$(awk -F: '/Start of program headers/ { o = $2 + 0 } /Size of program headers/ { s = $2 + 0 }
                     /Number of program headers/ { n = $2 + 0 } END { print o + s * n }' <<<"$hdr")
  sepcode=$(separate_code "$seg" "$hdr_end")
  page=$(max_page "$seg")

  printf '| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n' "$(basename "$f")" "$pie" "$relro" "$nx" "$canary" "$fortify" "$cet" "$rpath" "$sepcode" "$page"

  if [[ $CHECK == 1 ]]; then
    # AArch64 Linux kernels may use 4, 16 or 64 KiB pages; x86-64 always uses 4 KiB.
    local need=4K
    grep -q "Machine:.*AArch64" <<<"$hdr" && need=64K
    local bad=()
    [[ $pie == yes ]] || bad+=("not PIE")
    [[ $relro == full ]] || bad+=("RELRO $relro")
    [[ $nx == yes ]] || bad+=("executable stack")
    [[ $sepcode == yes ]] || bad+=("code shares file pages with non-code")
    [[ $page == invalid || ${page%K} -lt ${need%K} ]] && bad+=("PT_LOAD layout only valid up to $page pages, $need needed")
    if (( ${#bad[@]} )); then
      echo "FAIL $(basename "$f"): $(IFS=';'; echo "${bad[*]}")"
      echo "$seg" | grep -E "LOAD|RELRO"
      return 1
    fi
  fi
}

CHECK=0
[[ "${1:-}" == --check ]] && { CHECK=1; shift; }
echo "| binary | PIE | RELRO | NX | canary | FORTIFY | CET | RPATH | separate code | pages |"
echo "|---|---|---|---|---|---|---|---|---|---|"
status=0
for f in "$@"; do check "$f" || status=1; done
exit $status
