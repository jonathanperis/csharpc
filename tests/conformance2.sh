#!/usr/bin/env bash
# Adversarial conformance corpus (edge cases / spec corners): tests/conformance2/<area>/<NNN_name>.cs.
# Same options as tests/conformance.sh; results go to tests/conformance2/results.{json,md}.
export CONF_CORPUS=conformance2
exec "$(dirname "${BASH_SOURCE[0]}")/conformance.sh" "$@"
