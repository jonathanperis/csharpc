#!/usr/bin/env bash
# C# conformance suite: tests/conformance/<area>/<NNN_name>.cs
#
#   tests/conformance.sh                 build csharpc (Release), compile + run every program, compare
#                                        stdout + exit code with the recorded .NET results, print per-area
#                                        coverage and the ranked list of failure reasons
#   tests/conformance.sh --regen         (re)record expectations with real .NET (`dotnet run file.cs`)
#   tests/conformance.sh -j 8 linq       parallelism / only areas (or paths) containing "linq"
#   tests/conformance.sh --opt -O0       compile with a different optimization level (default -O2)
#   tests/conformance.sh --no-build      reuse the existing Release build
#
# Outputs: tests/conformance/results.json (machine-readable) and tests/conformance/results.md.
# The ranked failure reasons are csharpc's prioritized backlog (docs/09-conformance.md).
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export CONF_ROOT="$ROOT"
exec python3 - "$@" <<'PYEOF'
import argparse, collections, concurrent.futures as cf, json, os, re, subprocess, sys, tempfile, time

ROOT = os.environ["CONF_ROOT"]
CONF = os.path.join(ROOT, "tests", os.environ.get("CONF_CORPUS", "conformance"))
DOTNET = os.environ.get("DOTNET", "dotnet")
WARNING = re.compile(r"^\S+\(\d+,\d+\): warning ")

ap = argparse.ArgumentParser(prog="tests/conformance.sh")
ap.add_argument("filters", nargs="*", help="only programs whose path contains one of these")
ap.add_argument("--regen", action="store_true")
ap.add_argument("-j", type=int, default=os.cpu_count() or 4)
ap.add_argument("--opt", default="-O2")
ap.add_argument("--no-build", action="store_true")
ap.add_argument("--timeout", type=int, default=20)
ap.add_argument("-v", "--verbose", action="store_true", help="print the diff/diagnostic of each failure")
args = ap.parse_args()

programs = sorted(
    os.path.relpath(os.path.join(d, f), CONF)[:-3]
    for d, _, files in os.walk(CONF) for f in files if f.endswith(".cs"))
if args.filters:
    programs = [p for p in programs if any(f in p for f in args.filters)]

env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")

def run(cmd, cwd=None, timeout=None):
    try:
        r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout, env=env, errors="replace")
        # killed by signal N -> report 128+N like a shell (SIGABRT = 134, as .NET's unhandled exceptions)
        return (128 - r.returncode if r.returncode < 0 else r.returncode), r.stdout, r.stderr
    except subprocess.TimeoutExpired as e:
        return "timeout", (e.stdout or b"").decode(errors="replace") if isinstance(e.stdout, bytes) else (e.stdout or ""), ""

# ---------------------------------------------------------------------------------------- regen
if args.regen:
    def record(p):
        src = os.path.join(CONF, p + ".cs")
        code, out, err = run([DOTNET, "run", os.path.basename(src), "-p:WarningLevel=0", "-p:AllowUnsafeBlocks=true"],
                             cwd=os.path.dirname(src), timeout=300)
        out = "".join(l for l in out.splitlines(keepends=True) if not WARNING.match(l))
        if code != 0 and ("error CS" in out or "error CS" in err or "build failed" in (out + err).lower()):
            return p, "BUILD-ERROR", (out + err)[-600:]
        with open(os.path.join(CONF, p + ".expected"), "w") as f: f.write(out)
        ec = os.path.join(CONF, p + ".exitcode")
        if code != 0:
            with open(ec, "w") as f: f.write(f"{code}\n")
        elif os.path.exists(ec): os.remove(ec)
        return p, code, ""
    bad = 0
    with cf.ThreadPoolExecutor(args.j) as pool:
        for p, code, detail in pool.map(record, programs):
            print(f"recorded {p:<55} exit={code}")
            if code == "BUILD-ERROR": bad += 1; print("   " + detail.replace("\n", "\n   "))
    sys.exit(1 if bad else 0)

# ------------------------------------------------------------------------------------- run suite
if not args.no_build:
    code, out, err = run([DOTNET, "build", os.path.join(ROOT, "src", "Csharpc"), "-c", "Release", "-v", "quiet", "-nologo"])
    if code != 0: print(out + err); sys.exit("csharpc build failed")
CSHARPC = [DOTNET, os.path.join(ROOT, "src", "Csharpc", "bin", "Release", "net11.0", "csharpc.dll")]

UNSUPPORTED = re.compile(r"error CSLLVM0001: not supported by csharpc yet: (.*)")
MISSING = re.compile(r"error CSLLVM0002: '(.*?)' has no implementation in the csharpc runtime yet")

def generic_shape(name):
    # List<int> -> List<>, Func<int, string> -> Func<,>  (nested args collapse too)
    prev = None
    while prev != name:
        prev = name
        name = re.sub(r"<([^<>]*)>", lambda m: "<" + "," * m.group(1).count(",") + ">", name)
    return name

def normalize(reason):
    m = re.fullmatch(r"'(.*)' has no implementation in the csharpc runtime yet", reason.strip())
    if m: return "missing library member: " + generic_shape(m.group(1))
    # Keep namespace-qualified (library) names — they say *which* BCL type is missing; user names -> '*'.
    reason = re.sub(r"'([^']*)'", lambda m: "'" + generic_shape(m.group(1)) + "'" if re.match(r"^[A-Za-z_][\w.]*\.\w", m.group(1)) else "'*'", reason)
    return re.sub(r"(?<![\w<])\d+", "N", reason).strip()

def missing_group(member):
    # 'System.Linq.Enumerable.Where<TSource>(...)' -> 'System.Linq.Enumerable.Where'
    m = re.match(r"(.+?)\.(\w+|\.ctor)(?:<[^()]*>)?\((.*)\)$", member)
    return f"missing library member: {generic_shape(m.group(1))}.{m.group(2)}" if m else f"missing library member: {generic_shape(member)}"

def check(p, workdir):
    src = os.path.join(CONF, p + ".cs")
    exp_path = os.path.join(CONF, p + ".expected")
    if not os.path.exists(exp_path): return dict(program=p, status="skip", reasons=["no expectation (run --regen)"])
    want_code = int(open(os.path.join(CONF, p + ".exitcode")).read()) if os.path.exists(os.path.join(CONF, p + ".exitcode")) else 0
    exe = os.path.join(workdir, p.replace("/", "_"))
    t0 = time.time()
    code, out, err = run(CSHARPC + [args.opt, src, "-o", exe], timeout=180)
    compile_ms = int((time.time() - t0) * 1000)
    if code != 0:
        text = out + err
        reasons = sorted({normalize(m) for m in UNSUPPORTED.findall(text)} | {missing_group(m) for m in MISSING.findall(text)})
        if not reasons:
            if "error CS" in text: reasons = ["C# front-end error under csharpc: " + (re.search(r"error (CS\d+)", text).group(1))]
            elif "internal compiler error" in text: reasons = ["internal compiler error: invalid LLVM IR"]
            elif "Unhandled exception" in text or "   at " in text:
                m = re.search(r"Unhandled exception\. ([\w.]+)", text)
                reasons = ["compiler crash: " + (m.group(1) if m else "unknown")]
            elif code == "timeout": reasons = ["compiler timeout"]
            elif "failed with exit code" in text: reasons = ["link/runtime build error"]
            else: reasons = ["compiler failed: " + text.strip().splitlines()[-1][:120] if text.strip() else "compiler failed (no output)"]
        return dict(program=p, status="compile-error", reasons=reasons, compile_ms=compile_ms, detail=text[-1500:])
    code, out, err = run(["bash", "-c", 'ulimit -c 0; exec "$0"', exe], timeout=args.timeout)
    expected = open(exp_path).read()
    if code == "timeout":
        return dict(program=p, status="timeout", reasons=["runtime timeout / hang"], compile_ms=compile_ms)
    if out == expected and code == want_code:
        return dict(program=p, status="pass", reasons=[], compile_ms=compile_ms)
    if code != want_code and code in (-11, 139, -6, 134) and want_code == 0:
        reason = "runtime crash (SIGSEGV)" if code in (-11, 139) else "runtime abort: " + (err.strip().splitlines()[0][:100] if err.strip() else "?")
    else:
        reason = "wrong output" if code == want_code else f"wrong exit code ({code}, expected {want_code})"
    import difflib
    diff = "".join(difflib.unified_diff(expected.splitlines(True), out.splitlines(True), "expected", "csharpc", n=1))
    return dict(program=p, status="fail", reasons=[normalize(reason)], compile_ms=compile_ms, detail=(diff + err)[-1500:])

results = []
with tempfile.TemporaryDirectory() as work, cf.ThreadPoolExecutor(args.j) as pool:
    for r in pool.map(lambda p: check(p, work), programs):
        results.append(r)
        mark = {"pass": "PASS", "skip": "SKIP"}.get(r["status"], "FAIL")
        print(f"{mark} {r['program']:<55} {'; '.join(r['reasons'])[:110]}")
        if args.verbose and r.get("detail") and mark == "FAIL": print("     " + r["detail"].replace("\n", "\n     "))

# ------------------------------------------------------------------------------------- summary
areas = collections.OrderedDict()
for r in results:
    a = r["program"].split("/")[0]
    s = areas.setdefault(a, {"pass": 0, "total": 0})
    if r["status"] == "skip": continue
    s["total"] += 1
    s["pass"] += r["status"] == "pass"
total = sum(s["total"] for s in areas.values()); passed = sum(s["pass"] for s in areas.values())
reasons = collections.Counter(reason for r in results if r["status"] not in ("pass", "skip") for reason in r["reasons"])
blocked_by = collections.defaultdict(list)
for r in results:
    for reason in r["reasons"]: blocked_by[reason].append(r["program"])

pct = lambda a, b: f"{100.0 * a / b:.1f}%" if b else "n/a"
print(f"\n{'area':<16}{'pass':>6}{'total':>7}{'coverage':>10}")
for a, s in areas.items(): print(f"{a:<16}{s['pass']:>6}{s['total']:>7}{pct(s['pass'], s['total']):>10}")
print(f"{'TOTAL':<16}{passed:>6}{total:>7}{pct(passed, total):>10}\n")
print("Failure reasons (programs blocked):")
for reason, n in reasons.most_common(): print(f"  {n:4}  {reason}")

summary = dict(date=time.strftime("%Y-%m-%d"), opt=args.opt, passed=passed, total=total,
               coverage=round(100.0 * passed / total, 1) if total else 0,
               areas={a: dict(s, coverage=round(100.0 * s["pass"] / s["total"], 1) if s["total"] else 0) for a, s in areas.items()},
               reasons=[dict(reason=k, programs=n, examples=sorted(blocked_by[k])[:5]) for k, n in reasons.most_common()],
               results=[{k: v for k, v in r.items() if k != "detail"} for r in results])
if not args.filters:
    with open(os.path.join(CONF, "results.json"), "w") as f: json.dump(summary, f, indent=1)
    with open(os.path.join(CONF, "results.md"), "w") as f:
        f.write(f"# Conformance results ({summary['date']}, csharpc {args.opt})\n\n")
        f.write(f"**{passed}/{total} programs pass ({pct(passed, total)})**\n\n| area | pass | total | coverage |\n|---|---:|---:|---:|\n")
        for a, s in areas.items(): f.write(f"| {a} | {s['pass']} | {s['total']} | {pct(s['pass'], s['total'])} |\n")
        f.write(f"| **total** | **{passed}** | **{total}** | **{pct(passed, total)}** |\n\n## Failure reasons (ranked by programs blocked)\n\n| # | programs | reason | e.g. |\n|---:|---:|---|---|\n")
        for i, (k, n) in enumerate(reasons.most_common(), 1):
            f.write(f"| {i} | {n} | {k.replace('|', '/')} | {', '.join(sorted(blocked_by[k])[:3])} |\n")
# <corpus>/known-failures.txt: programs known to fail (one per line, '#' comments). They don't fail
# the run, so CI can gate on the rest; a listed program that now passes does fail it, so the list
# never hides a fix (remove the line). Filtered runs only check the programs they ran.
known_path = os.path.join(CONF, "known-failures.txt")
known = set()
if os.path.exists(known_path):
    for line in open(known_path):
        line = line.split("#", 1)[0].strip()
        if line: known.add(line)
failed = {r["program"] for r in results if r["status"] not in ("pass", "skip")}
ran = {r["program"] for r in results}
new_failures = sorted(failed - known)
fixed = sorted(p for p in known & ran if p not in failed)
for p in new_failures: print(f"NEW FAILURE {p}")
for p in fixed: print(f"KNOWN FAILURE NOW PASSES {p} (remove it from {os.path.relpath(known_path, ROOT)})")
sys.exit(0 if not new_failures and not fixed else 1)
PYEOF
