# Sourced first by the test, benchmark and tool scripts (#198): they run under `set -u` with arrays that may be empty, which bash
# before 4.4 reports as unbound ("TARGET_FLAGS[@]: unbound variable"), and use other bash 4 features. macOS's /bin/bash is 3.2: a
# newer bash (Homebrew's) runs the script instead, with the same arguments; without one, the script stops with what to install.
if [ "${BASH_VERSINFO[0]}" -lt 4 ] || { [ "${BASH_VERSINFO[0]}" -eq 4 ] && [ "${BASH_VERSINFO[1]}" -lt 4 ]; }; then
  for csharpc_newer_bash in /opt/homebrew/bin/bash /usr/local/bin/bash; do
    if [ -x "$csharpc_newer_bash" ] \
      && "$csharpc_newer_bash" -c '(( BASH_VERSINFO[0] > 4 || (BASH_VERSINFO[0] == 4 && BASH_VERSINFO[1] >= 4) ))' 2>/dev/null; then
      exec "$csharpc_newer_bash" "$0" "$@"
    fi
  done
  echo "$0 needs bash 4.4 or newer; this is bash $BASH_VERSION (on macOS: brew install bash)" >&2
  exit 2
fi
