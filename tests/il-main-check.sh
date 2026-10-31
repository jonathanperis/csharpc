#!/usr/bin/env bash
# Compares a `tests/run.sh --via-il` log with the list of samples known to fail through IL
# (tests/il-main-known-failures.txt: one sample name per line, `#` comments, a reason after the name).
# Prints a Markdown summary (pass rate, regressions, samples that now pass) and fails when a sample
# outside the list fails or a listed one passes at every optimization level it ran at.
#
# Usage: tests/il-main-check.sh <run.sh log> <known-failures file> [label]
set -uo pipefail
log="$1" known_file="$2" label="${3:-}"

declare -A known=() failed=() ran=()
while read -r name _; do [[ -n "$name" && "$name" != \#* ]] && known["$name"]=1; done < "$known_file"
while IFS= read -r line; do
  if [[ "$line" =~ ^(PASS|FAIL)\ ([^ ]+)\ \[ ]]; then
    ran["${BASH_REMATCH[2]}"]=1
    [[ ${BASH_REMATCH[1]} == FAIL ]] && failed["${BASH_REMATCH[2]}"]=1
  fi
done < "$log"

regressions=() fixed=()
for name in "${!failed[@]}"; do [[ -n "${known[$name]:-}" ]] || regressions+=("$name"); done
for name in "${!ran[@]}"; do [[ -n "${known[$name]:-}" && -z "${failed[$name]:-}" ]] && fixed+=("$name"); done

total=${#ran[@]} failing=${#failed[@]}
echo "### Program as IL ${label:+($label)}"
echo
echo "$((total - failing))/$total samples pass through csc -> IL -> csharpc ($failing failing, $((failing - ${#regressions[@]})) of them known)."
if ((${#regressions[@]} > 0)); then
  echo
  echo "**Regressions** (failing, not in $(basename "$known_file")):"
  printf -- '- `%s`\n' $(printf '%s\n' "${regressions[@]}" | LC_ALL=C sort)
fi
if ((${#fixed[@]} > 0)); then
  echo
  echo "**Now passing** (remove from $(basename "$known_file")):"
  printf -- '- `%s`\n' $(printf '%s\n' "${fixed[@]}" | LC_ALL=C sort)
fi
((${#regressions[@]} == 0 && ${#fixed[@]} == 0))
