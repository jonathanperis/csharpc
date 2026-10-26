#!/usr/bin/env bash
# Plans csharpc's CI run: a full sharded gate, a targeted (partial) run, or a docs-only skip.
# The five merge-bar check names (test (<platform>)) are produced only by a full or docs-only
# run of .github/workflows/ci.yml. A partial run never reports those names as success.
# See docs/05-testing.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Shard counts measured from CI run 40310822368 (docs/05-testing.md). x86-64 and Windows are the
# long poles and have runners to spare. arm64 and macOS queue, so two shards keeps them inside
# the wall-clock target without a second wave. wasm shares ubuntu-24.04 with x86-64; two shards
# is enough (the unsharded differential step was 21.5 min).
shard_count() {
  case "$1" in
    x86-64|windows-x64) echo 4 ;;
    arm64|macos-arm64|wasm32-wasi) echo 2 ;;
    *) echo "unknown platform: $1" >&2; return 1 ;;
  esac
}

platform_os() {
  case "$1" in
    x86-64|arm64) echo linux ;;
    macos-arm64) echo macos ;;
    windows-x64) echo windows ;;
    wasm32-wasi) echo wasm ;;
    *) return 1 ;;
  esac
}

platform_runner() {
  case "$1" in
    x86-64|wasm32-wasi) echo ubuntu-24.04 ;;
    arm64) echo ubuntu-24.04-arm ;;
    macos-arm64) echo macos-15 ;;
    windows-x64) echo windows-2025 ;;
    *) return 1 ;;
  esac
}

ALL_PLATFORMS="x86-64 arm64 macos-arm64 windows-x64 wasm32-wasi"

is_docs_path() {
  case "$1" in
    docs|docs/*|*.md|LICENSE|LICENSE.txt|COPYING|NOTICE|AUTHORS) return 0 ;;
    *) return 1 ;;
  esac
}

# Empty input is docs-only: a commit that does not touch a file did not change the compiler.
is_docs_list() {
  local p
  while IFS= read -r p; do
    [[ -z "$p" ]] && continue
    is_docs_path "$p" || return 1
  done
  return 0
}

# Tokens: key=value, plus the bare word "file" (spec lives in ci/targets).
# Sets FILTER PLATFORMS OPT REPEAT SKIP_OTHER HAVE_SPEC NARROWING.
apply_target_token() {
  local token="$1" key value
  if [[ "$token" == "file" ]]; then
    HAVE_SPEC=1
    return 0
  fi
  [[ "$token" == *=* ]] || { echo "bad CI target token '$token' (want key=value)" >&2; return 1; }
  key="${token%%=*}"
  value="${token#*=}"
  HAVE_SPEC=1
  case "$key" in
    filter) FILTER="$value" ;;
    platforms) PLATFORMS="$value" ;;
    opt) OPT="$value" ;;
    repeat) REPEAT="$value" ;;
    skip_other) SKIP_OTHER="$value" ;;
    *) echo "unknown CI target key '$key'" >&2; return 1 ;;
  esac
}

parse_trailer() {
  local msg="$1" line
  while IFS= read -r line; do
    [[ "$line" == "CI-Only:"* ]] || continue
    HAVE_SPEC=1
    local rest="${line#CI-Only:}"
    local token
    # shellcheck disable=SC2086
    for token in $rest; do
      apply_target_token "$token"
    done
  done <<< "$msg"
}

parse_targets_file() {
  local file="$1" line key value
  [[ -f "$file" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%%#*}"
    line="${line#"${line%%[![:space:]]*}"}"
    line="${line%"${line##*[![:space:]]}"}"
    [[ -z "$line" ]] && continue
    [[ "$line" == *=* ]] || { echo "bad ci/targets line '$line'" >&2; return 1; }
    key="${line%%=*}"
    value="${line#*=}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    apply_target_token "$key=$value"
  done < "$file"
}

normalize_bool() {
  case "$1" in
    true|True|TRUE|yes|1) echo true ;;
    false|False|FALSE|no|0) echo false ;;
    *) echo "bad boolean '$1'" >&2; return 1 ;;
  esac
}

# A targeted run must narrow the gate. skip_other alone would re-run every platform and still
# not count, which wastes the runners the shard split is trying to save.
validate_target() {
  local p opt_ok=""
  [[ -n "$PLATFORMS" ]] || PLATFORMS=all
  [[ -n "$OPT" ]] || OPT=all
  [[ -n "$REPEAT" ]] || REPEAT=1
  if [[ -z "$SKIP_OTHER" ]]; then
    # A flake hunt should not also run conformance. Full dispatch leaves this unset and is not partial.
    SKIP_OTHER=true
  fi
  SKIP_OTHER="$(normalize_bool "$SKIP_OTHER")"
  case "$OPT" in
    all|-O0|-O2) ;;
    *) echo "bad opt '$OPT' (want all, -O0, or -O2)" >&2; return 1 ;;
  esac
  [[ "$REPEAT" =~ ^[1-9][0-9]*$ ]] || { echo "bad repeat '$REPEAT'" >&2; return 1; }
  if (( REPEAT > 50 )); then
    echo "repeat $REPEAT is above the cap of 50" >&2
    return 1
  fi
  if (( REPEAT > 1 )) && [[ -z "$FILTER" ]]; then
    echo "repeat requires a filter (refusing to repeat the whole suite)" >&2
    return 1
  fi
  if [[ "$PLATFORMS" == all ]]; then
    :
  else
    local IFS=','
    local -a parts=()
    read -ra parts <<< "${PLATFORMS// /}"
    [[ ${#parts[@]} -gt 0 ]] || { echo "platforms is empty" >&2; return 1; }
    for p in "${parts[@]}"; do
      [[ -n "$p" ]] || continue
      case " $ALL_PLATFORMS " in
        *" $p "*) ;;
        *) echo "unknown platform '$p'" >&2; return 1 ;;
      esac
    done
    PLATFORMS="$(IFS=,; echo "${parts[*]}")"
  fi
  NARROWING=0
  [[ -n "$FILTER" ]] && NARROWING=1
  [[ "$PLATFORMS" != all ]] && NARROWING=1
  [[ "$OPT" != all ]] && NARROWING=1
  (( REPEAT > 1 )) && NARROWING=1
  if [[ "$SKIP_OTHER" == false && "$NARROWING" == 0 ]]; then
    echo "CI target does not narrow the suite; run the CI workflow for a full gate" >&2
    return 1
  fi
  # skip_other=true with nothing else still runs every differential shard's worth of work
  # under a partial label. Require a real narrowing so "CI-Only:" cannot silently drop the gate.
  if [[ "$NARROWING" == 0 ]]; then
    echo "CI-Only needs a filter, a platform subset, an opt level, or a repeat count" >&2
    return 1
  fi
  return 0
}

selected_platforms() {
  if [[ "$PLATFORMS" == all ]]; then
    echo "$ALL_PLATFORMS"
  else
    echo "${PLATFORMS//,/ }"
  fi
}

# Print matrix rows. Kind is "diff" (one job per platform; partial runs are not sharded, so a
# repeat's log stays in one place) or the full gate's sharded rows.
write_matrix_lines() {
  local mode="$1" p os runner shards shard job artifact
  if [[ "$mode" == partial ]]; then
    for p in $(selected_platforms); do
      os="$(platform_os "$p")"
      runner="$(platform_runner "$p")"
      job="partial ($p)"
      artifact="partial-$p"
      printf 'diff\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$job" "$p" "$os" "$runner" "0" "1" "$OPT" "$artifact"
    done
    if [[ "$SKIP_OTHER" == false ]]; then
      for p in $(selected_platforms); do
        os="$(platform_os "$p")"
        runner="$(platform_runner "$p")"
        printf 'suites\t%s\t%s\t%s\t%s\t%s\n' \
          "partial suites ($p)" "$p" "$os" "$runner" "suites-$p"
      done
    fi
    return 0
  fi
  for p in $ALL_PLATFORMS; do
    os="$(platform_os "$p")"
    runner="$(platform_runner "$p")"
    shards="$(shard_count "$p")"
    shard=0
    while (( shard < shards )); do
      job="diff ($p $shard/$shards)"
      artifact="diff-$p-$shard"
      printf 'diff\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$job" "$p" "$os" "$runner" "$shard" "$shards" "all" "$artifact"
      shard=$((shard + 1))
    done
    printf 'suites\t%s\t%s\t%s\t%s\t%s\n' \
      "suites ($p)" "$p" "$os" "$runner" "suites-$p"
  done
}

rows_to_json() {
  python3 -c '
import json, sys
diff, suites = [], []
for line in sys.stdin:
    line = line.rstrip("\n")
    if not line:
        continue
    parts = line.split("\t")
    kind = parts[0]
    if kind == "diff":
        _, job, platform, os_, runner, shard, shards, opt, artifact = parts
        diff.append({
            "job_name": job,
            "platform": platform,
            "os": os_,
            "runner": runner,
            "shard": shard,
            "shards": shards,
            "opt": opt,
            "artifact": artifact,
        })
    elif kind == "suites":
        _, job, platform, os_, runner, artifact = parts
        suites.append({
            "job_name": job,
            "platform": platform,
            "os": os_,
            "runner": runner,
            "artifact": artifact,
        })
    else:
        raise SystemExit(f"bad matrix row {line!r}")
print(json.dumps({"include": diff}, separators=(",", ":")))
print(json.dumps({"include": suites}, separators=(",", ":")))
'
}

parent_gate_green() {
  local sha="$1" tmp rc
  [[ -n "${CI_REPO:-}" ]] || return 1
  tmp="$(mktemp)"
  if ! gh api --paginate "/repos/${CI_REPO}/commits/${sha}/check-runs?per_page=100" \
      --jq '.check_runs[] | [.name, (.conclusion // ""), (.completed_at // "")] | @tsv' >"$tmp" 2>/dev/null; then
    rm -f "$tmp"
    return 1
  fi
  python3 - "$tmp" <<'PY'
import sys
want = (
    "test (x86-64)",
    "test (arm64)",
    "test (macos-arm64)",
    "test (windows-x64)",
    "test (wasm32-wasi)",
)
latest = {}
with open(sys.argv[1], encoding="utf-8") as fh:
    for line in fh:
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 3:
            continue
        name, conclusion, completed = parts[0], parts[1], parts[2]
        prev = latest.get(name)
        if prev is None or completed >= prev[0]:
            latest[name] = (completed, conclusion)
for name in want:
    if name not in latest or latest[name][1] != "success":
        sys.exit(1)
PY
  rc=$?
  rm -f "$tmp"
  return "$rc"
}

# Walk back through commits that did not change code until one has a green full (or docs-skipped)
# gate. A cancelled or failed parent means this tree's code has not passed, so the suite runs.
find_green_ancestor() {
  local ancestor="$1" i parent step
  for ((i = 0; i < 30; i++)); do
    if parent_gate_green "$ancestor"; then
      DOCS_PARENT="$ancestor"
      return 0
    fi
    parent="$(git rev-parse "${ancestor}^" 2>/dev/null)" || return 1
    step="$(git diff --no-renames --name-only "$parent" "$ancestor")" || return 1
    is_docs_list <<< "$step" || return 1
    ancestor="$parent"
  done
  return 1
}

emit_outputs() {
  local dest="${GITHUB_OUTPUT:-}"
  local diff_json suites_json
  {
    read -r diff_json
    read -r suites_json
  } < <(write_matrix_lines "$MODE" | rows_to_json)
  # An empty include is a workflow error, and a skipped job still needs a parseable matrix.
  if [[ "$diff_json" == '{"include":[]}' ]]; then
    diff_json='{"include":[{"job_name":"unused","platform":"x86-64","os":"linux","runner":"ubuntu-24.04","shard":"0","shards":"1","opt":"all","artifact":"unused-diff"}]}'
  fi
  if [[ "$suites_json" == '{"include":[]}' ]]; then
    suites_json='{"include":[{"job_name":"unused","platform":"x86-64","os":"linux","runner":"ubuntu-24.04","artifact":"unused-suites"}]}'
  fi
  local out="$dest"
  [[ -n "$out" ]] || out=/dev/stdout
  {
    echo "mode=$MODE"
    echo "filter<<EOF_FILTER"
    printf '%s\n' "$FILTER"
    echo "EOF_FILTER"
    echo "platforms=$PLATFORMS"
    echo "opt=$OPT"
    echo "repeat=$REPEAT"
    echo "skip_other=$SKIP_OTHER"
    echo "run_suites=$RUN_SUITES"
    echo "reason<<EOF_REASON"
    printf '%s\n' "$REASON"
    echo "EOF_REASON"
    echo "parent_sha=${DOCS_PARENT:-}"
    echo "diff_matrix<<EOF_DIFF"
    printf '%s\n' "$diff_json"
    echo "EOF_DIFF"
    echo "suites_matrix<<EOF_SUITES"
    printf '%s\n' "$suites_json"
    echo "EOF_SUITES"
  } >>"$out"
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    {
      echo "### CI plan: \`$MODE\`"
      echo
      echo "$REASON"
      echo
      echo "- platforms: \`$PLATFORMS\`"
      echo "- filter: \`${FILTER:-<all>}\`"
      echo "- opt: \`$OPT\`"
      echo "- repeat: \`$REPEAT\`"
      echo "- skip other suites: \`$SKIP_OTHER\`"
    } >>"$GITHUB_STEP_SUMMARY"
  fi
}

cmd_emit() {
  MODE=full
  REASON="full gate"
  FILTER=""
  PLATFORMS=all
  OPT=all
  REPEAT=1
  # Empty until a targeted spec sets it. validate_target defaults a partial run to skip_other=true
  # so a flake hunt does not also spend a macOS/Windows runner on conformance.
  SKIP_OTHER=""
  HAVE_SPEC=0
  NARROWING=0
  RUN_SUITES=true
  DOCS_PARENT=""
  local event="${CI_EVENT:?}"
  local ref="${CI_REF:-}"

  if [[ "$event" == workflow_dispatch ]]; then
    FILTER="${INPUT_FILTER:-}"
    PLATFORMS="${INPUT_PLATFORMS:-all}"
    OPT="${INPUT_OPT:-all}"
    REPEAT="${INPUT_REPEAT:-1}"
    SKIP_OTHER="${INPUT_SKIP_OTHER:-false}"
    HAVE_SPEC=1
    if [[ "${CI_DISPATCH_MUST_NARROW:-}" == 1 ]]; then
      validate_target
      MODE=partial
      REASON="workflow_dispatch targeted run (not the merge gate)"
      [[ "$SKIP_OTHER" == true ]] && RUN_SUITES=false || RUN_SUITES=true
    else
      # The CI workflow's own dispatch is always a full gate. Inputs are ignored there;
      # targeted dispatch lives in ci-targeted.yml.
      MODE=full
      FILTER=""
      PLATFORMS=all
      OPT=all
      REPEAT=1
      SKIP_OTHER=false
      RUN_SUITES=true
      REASON="workflow_dispatch full gate"
    fi
  elif [[ "$event" == push ]] && [[ "$ref" == refs/heads/ci/* ]]; then
    # ci/** only. `[[ == refs/heads/ci/* ]]` matches nested names too (`*` matches `/`).
    # main ignores the trailer and ci/targets so a bad merge cannot drop the gate.
    parse_targets_file "$ROOT/ci/targets"
    parse_trailer "${CI_COMMIT_MSG:-}"
    if [[ "$HAVE_SPEC" == 1 ]]; then
      validate_target
      MODE=partial
      REASON="ci/** targeted run (CI-Only trailer or ci/targets); not the merge gate"
      [[ "$SKIP_OTHER" == true ]] && RUN_SUITES=false || RUN_SUITES=true
    fi
  fi

  if [[ "$MODE" != partial ]]; then
    SKIP_OTHER=false
  fi

  if [[ "$MODE" == full ]]; then
    local files="" anchor=""
    if [[ "$event" == pull_request && -n "${CI_BASE:-}" && -n "${CI_SHA:-}" ]]; then
      if git cat-file -e "${CI_BASE}^{commit}" 2>/dev/null; then
        files="$(git diff --no-renames --name-only "$CI_BASE" "$CI_SHA" || true)"
        anchor="$CI_BASE"
      fi
    elif [[ "$event" == push && -n "${CI_BEFORE:-}" && "$CI_BEFORE" != 0000000* && -n "${CI_SHA:-}" ]]; then
      if git cat-file -e "${CI_BEFORE}^{commit}" 2>/dev/null; then
        files="$(git diff --no-renames --name-only "$CI_BEFORE" "$CI_SHA" || true)"
        anchor="$CI_BEFORE"
      fi
    fi
    if [[ -n "$anchor" ]] && is_docs_list <<< "$files"; then
      if find_green_ancestor "$anchor"; then
        MODE=docs
        RUN_SUITES=false
        REASON="docs-only change; parent $DOCS_PARENT already has a green test (*) gate. Compiler suite skipped; the five merge-bar checks report success."
      else
        REASON="docs-only paths, but no ancestor has a green test (*) gate (in progress, failed, or cancelled). Running the full suite."
      fi
    fi
  fi

  emit_outputs
}

cmd_write_manifest() {
  local shard="" opt="" filter=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --shard) shift; shard="$1" ;;
      --opt) shift; opt="$1" ;;
      --filter) shift; filter="$1" ;;
      *) echo "unknown arg $1" >&2; return 2 ;;
    esac
    shift
  done
  [[ -n "$shard" && -n "$opt" ]] || { echo "write-manifest needs --shard and --opt" >&2; return 2; }
  local opts=()
  if [[ "$opt" == all ]]; then opts=(-O0 -O2); else opts=("$opt"); fi
  local names name o count=0
  if [[ -n "$filter" ]]; then
    names="$(bash "$ROOT/tests/run.sh" --list --shard "$shard" "$filter")"
  else
    names="$(bash "$ROOT/tests/run.sh" --list --shard "$shard")"
  fi
  [[ -n "$names" ]] || { echo "shard $shard opt $opt filter '${filter}' selected no samples" >&2; return 1; }
  while IFS= read -r name; do
    [[ -z "$name" ]] && continue
    for o in "${opts[@]}"; do
      printf '%s %s\n' "$name" "$o"
      count=$((count + 1))
    done
  done <<< "$names"
  [[ "$count" -gt 0 ]]
}

cmd_run_shard() {
  local opt="${CI_OPT:?}" shard="${CI_SHARD:?}" shards="${CI_SHARDS:?}"
  local repeat="${CI_REPEAT:-1}" filter="${CI_FILTER:-}"
  cmd_write_manifest --shard "$shard/$shards" --opt "$opt" ${filter:+--filter "$filter"} > manifest.txt
  local args=(--shard "$shard/$shards")
  if [[ "$opt" == all ]]; then args+=(--all-opts); else args+=(--opt "$opt"); fi
  if [[ "$repeat" != 1 ]]; then args+=(--repeat "$repeat"); fi
  if [[ -n "$filter" ]]; then args+=("$filter"); fi
  bash "$ROOT/tests/run.sh" "${args[@]}"
}

cmd_coverage() {
  local diff_dir="" suites_file="" samples_dir=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --diff) shift; diff_dir="$1" ;;
      --suites) shift; suites_file="$1" ;;
      --samples) shift; samples_dir="$1" ;;
      *) echo "unknown arg $1" >&2; return 2 ;;
    esac
    shift
  done
  [[ -n "$diff_dir" && -n "$suites_file" && -n "$samples_dir" ]] || {
    echo "coverage needs --diff, --suites, and --samples" >&2
    return 2
  }
  local fail=0 manifest result name opt
  local tmp expected got
  tmp="$(mktemp -d)"
  # Every sample at -O0 and at -O2, exactly once across the shard manifests.
  : >"$tmp/expected"
  while IFS= read -r name; do
    [[ -z "$name" ]] && continue
    printf '%s -O0\n' "$name" >>"$tmp/expected"
    printf '%s -O2\n' "$name" >>"$tmp/expected"
  done < <(printf '%s\n' "$samples_dir"/*.cs | LC_ALL=C sort | xargs -n1 basename | sed 's/\.cs$//')
  LC_ALL=C sort -o "$tmp/expected" "$tmp/expected"

  : >"$tmp/got"
  local found=0
  while IFS= read -r manifest; do
    found=1
    result="$(dirname "$manifest")/result.txt"
    if [[ ! -f "$result" ]]; then
      echo "FAIL $(dirname "$manifest"): no result.txt"
      fail=1
    else
      local status
      status="$(tr -d '\r' <"$result" | tr -d '\n')"
      if [[ "$status" != success ]]; then
        echo "FAIL $(basename "$(dirname "$manifest")"): shard result is ${status:-missing}"
        fail=1
      fi
    fi
    tr -d '\r' <"$manifest" | sed '/^[[:space:]]*$/d; /^#/d' >>"$tmp/got"
  done < <(find "$diff_dir" -name manifest.txt -type f | LC_ALL=C sort)
  if [[ "$found" == 0 ]]; then
    echo "FAIL no shard manifests under $diff_dir"
    fail=1
  fi
  LC_ALL=C sort -o "$tmp/got" "$tmp/got"
  if [[ -s "$tmp/got" ]] && uniq -d "$tmp/got" | grep -q .; then
    echo "FAIL duplicate sample/opt pairs:"
    uniq -d "$tmp/got" | head -20
    fail=1
  fi
  if ! diff -u "$tmp/expected" "$tmp/got" >"$tmp/pairs.diff"; then
    echo "FAIL shard manifests do not cover every sample at -O0 and -O2 exactly once"
    # Show a short summary, not the whole 800-line diff.
    echo "  missing $(( $(grep -c '^-' "$tmp/pairs.diff" || true) - 1 )) lines, extra $(( $(grep -c '^+' "$tmp/pairs.diff" || true) - 1 )) lines (first differences):"
    grep -E '^[+-]' "$tmp/pairs.diff" | grep -vE '^[+-]{3}' | head -30
    fail=1
  else
    echo "coverage: $(wc -l <"$tmp/expected") sample/opt pairs, each exactly once"
  fi
  if [[ ! -f "$suites_file" ]]; then
    echo "FAIL no suites result at $suites_file"
    fail=1
  else
    local suites_status
    suites_status="$(tr -d '\r' <"$suites_file" | tr -d '\n')"
    if [[ "$suites_status" != success ]]; then
      echo "FAIL suites result is ${suites_status:-missing}"
      fail=1
    else
      echo "suites: success"
    fi
  fi
  rm -rf "$tmp"
  [[ "$fail" == 0 ]]
}

cmd_release_gate() {
  local tsv="$1"
  local fail=0
  # Job lines are "name<whitespace>conclusion". Normalize so a space or a tab both work.
  local norm
  norm="$(mktemp)"
  sed -E 's/[[:space:]]+(success|failure|cancelled|skipped|neutral|timed_out|startup_failure)$/	\1/' "$tsv" >"$norm"
  local p
  for p in $ALL_PLATFORMS; do
    if ! grep -qx "test (${p})	success" "$norm"; then
      echo "not a full gate: test ($p) is not success"
      fail=1
    fi
    if ! grep -E "^diff \\(${p} .+	success$" "$norm" >/dev/null; then
      echo "not a full gate: no successful diff shard for $p"
      fail=1
    fi
    if grep -E "^diff \\(${p} " "$norm" | grep -v '	success$' >/dev/null; then
      echo "not a full gate: a diff shard for $p was not success"
      fail=1
    fi
  done
  if grep -E '^partial ' "$norm" >/dev/null; then
    echo "not a full gate: partial jobs are present"
    fail=1
  fi
  rm -f "$norm"
  [[ "$fail" == 0 ]]
}

cmd_self_check() {
  local tmp fail=0
  tmp="$(mktemp -d)"
  # Docs paths.
  is_docs_path "docs/05-testing.md" || fail=1
  is_docs_path "README.md" || fail=1
  is_docs_path "docs/adr/0001-roslyn-as-frontend.md" || fail=1
  is_docs_path "LICENSE" || fail=1
  is_docs_path "src/Csharpc/Driver/Program.cs" && fail=1
  is_docs_path ".github/workflows/ci.yml" && fail=1
  is_docs_path "ci/targets" && fail=1
  printf '%s\n' "docs/20-status.md" "README.md" | is_docs_list || fail=1
  printf '%s\n' "docs/20-status.md" "src/runtime/csrt.h" | is_docs_list && fail=1
  printf '' | is_docs_list || fail=1

  # Shard partition covers every sample exactly once.
  local all a b
  all="$(bash "$ROOT/tests/run.sh" --list | LC_ALL=C sort)"
  : >"$tmp/union"
  local i n
  for n in 2 4; do
    : >"$tmp/union"
    for ((i = 0; i < n; i++)); do
      bash "$ROOT/tests/run.sh" --list --shard "$i/$n" >>"$tmp/union"
    done
    a="$(printf '%s\n' "$all" | LC_ALL=C sort)"
    b="$(LC_ALL=C sort "$tmp/union")"
    [[ "$a" == "$b" ]] || { echo "shard cover failed for /$n"; fail=1; }
    [[ -z "$(uniq -d <<<"$b")" ]] || { echo "shard duplicate for /$n"; fail=1; }
  done
  # Filter then shard: 544 is one sample, shard 0/1 keeps it, shard 1/2 drops it or keeps it, union is one.
  local one
  one="$(bash "$ROOT/tests/run.sh" --list --shard 0/1 544_blocking_collection)"
  [[ "$one" == "544_blocking_collection" ]] || { echo "filter shard mismatch: $one"; fail=1; }

  # Trailer + file overlay. File first, trailer wins on keys it sets.
  FILTER=""; PLATFORMS=""; OPT=""; REPEAT=""; SKIP_OTHER=""; HAVE_SPEC=0
  printf 'filter=111\nplatforms=arm64\nrepeat=3\n' >"$tmp/targets"
  parse_targets_file "$tmp/targets"
  parse_trailer $'note\n\nCI-Only: filter=544 platforms=macos-arm64 repeat=20\n'
  [[ "$FILTER" == 544 && "$PLATFORMS" == macos-arm64 && "$REPEAT" == 20 && "$HAVE_SPEC" == 1 ]] || {
    echo "trailer overlay failed: filter=$FILTER platforms=$PLATFORMS repeat=$REPEAT"
    fail=1
  }
  OPT=all
  SKIP_OTHER=""
  validate_target || { echo "validate_target rejected a good spec"; fail=1; }
  [[ "$SKIP_OTHER" == true && "$NARROWING" == 1 ]] || { echo "defaults failed"; fail=1; }

  # Repeat of the whole suite is refused.
  FILTER=""; PLATFORMS=all; OPT=all; REPEAT=20; SKIP_OTHER=""
  if validate_target >/dev/null 2>&1; then
    echo "repeat without filter should fail"
    fail=1
  fi

  # Release gate.
  cat >"$tmp/jobs" <<'EOF'
plan	success
diff (x86-64 0/4)	success
diff (x86-64 1/4)	success
diff (x86-64 2/4)	success
diff (x86-64 3/4)	success
diff (arm64 0/2)	success
diff (arm64 1/2)	success
diff (macos-arm64 0/2)	success
diff (macos-arm64 1/2)	success
diff (windows-x64 0/4)	success
diff (windows-x64 1/4)	success
diff (windows-x64 2/4)	success
diff (windows-x64 3/4)	success
diff (wasm32-wasi 0/2)	success
diff (wasm32-wasi 1/2)	success
suites (x86-64)	success
test (x86-64)	success
test (arm64)	success
test (macos-arm64)	success
test (windows-x64)	success
test (wasm32-wasi)	success
EOF
  if ! cmd_release_gate "$tmp/jobs" >/dev/null; then
    echo "full fixture should pass the release gate"
    fail=1
  fi
  printf '%s\n' "plan	success" "partial (macos-arm64)	success" 'test (${{ matrix.platform }})	skipped' >"$tmp/partial"
  if cmd_release_gate "$tmp/partial" >/dev/null; then
    echo "partial fixture should not pass the release gate"
    fail=1
  fi
  printf '%s\n' "plan	success" "test (x86-64)	success" "test (arm64)	success" "test (macos-arm64)	success" "test (windows-x64)	success" "test (wasm32-wasi)	success" 'diff (${{ matrix.job_name }})	skipped' >"$tmp/docs"
  if cmd_release_gate "$tmp/docs" >/dev/null; then
    echo "docs fixture should not pass the release gate"
    fail=1
  fi

  # Manifest pairs for one shard match --list.
  bash "$ROOT/tools/ci-plan.sh" write-manifest --shard 0/4 --opt all >"$tmp/man"
  local listed pairs
  listed="$(bash "$ROOT/tests/run.sh" --list --shard 0/4 | wc -l)"
  pairs="$(wc -l <"$tmp/man")"
  [[ "$pairs" == $((listed * 2)) ]] || { echo "manifest pair count $pairs vs $listed"; fail=1; }

  rm -rf "$tmp"
  if [[ "$fail" != 0 ]]; then
    echo "ci-plan self-check failed"
    return 1
  fi
  echo "ci-plan self-check ok"
}

usage() {
  echo "usage: tools/ci-plan.sh {emit|write-manifest|run-shard|coverage|release-gate|self-check}" >&2
  return 2
}

main() {
  local cmd="${1:-}"
  shift || true
  case "$cmd" in
    emit) cmd_emit "$@" ;;
    write-manifest) cmd_write_manifest "$@" ;;
    run-shard) cmd_run_shard "$@" ;;
    coverage) cmd_coverage "$@" ;;
    release-gate)
      local tsv="${1:-}"
      if [[ -z "$tsv" ]]; then tsv="$(mktemp)"; cat >"$tsv"; fi
      cmd_release_gate "$tsv"
      ;;
    self-check) cmd_self_check ;;
    *) usage ;;
  esac
}

main "$@"
