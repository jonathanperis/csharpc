#!/usr/bin/env python3
"""Aggregate a compatibility run (run with `python3 -I`).

    python3 -I tools/compat/analyze.py WORK_DIR [--markdown]

Reads WORK_DIR/selected.tsv, results/<id>.tsv and logs/<id>/csharpc.log; prints NativeAOT parity
(csharpc PASS among the programs NativeAOT publishes and runs like the JIT) with the reasons programs
are NativeAOT-unsupported and compile time/size against NativeAOT, the JIT-based per-category counts,
the unsupported constructs/APIs ranked by how many in-scope programs each blocks, and every bug
(COMPILE-CRASH, RUNTIME-DIFF, RUNTIME-CRASH) with a diff excerpt. Writes WORK_DIR/summary.md.
"""
import collections
import os
import re
import sys

CATEGORIES = ["PASS", "COMPILE-UNSUPPORTED", "COMPILE-ERROR", "COMPILE-CRASH", "RUNTIME-DIFF",
              "RUNTIME-CRASH", "SKIPPED"]
UNSUPPORTED = re.compile(r"error (CSLLVM000[125]): (.*)$")


def read(path, limit=None):
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return f.read() if limit is None else f.read(limit)
    except OSError:
        return ""


def strip_generics(s):
    """'A<B<C>>.M<T>(x)' -> 'A<>.M<>(x)' so instantiations of one API count as one."""
    out, depth = [], 0
    for ch in s:
        if ch == "<":
            if depth == 0:
                out.append("<>")
            depth += 1
        elif ch == ">":
            depth -= 1
        elif depth == 0:
            out.append(ch)
    return "".join(out)


def member_owner(member):
    """'System.DateTime.Parse(string)' -> 'System.DateTime'."""
    m = strip_generics(member)
    if m.startswith("("):
        return "System.ValueTuple"
    m = m.split("(")[0].replace("..ctor", ".ctor")
    m = re.sub(r"\.this\[\]$", ".this", m)
    m = re.sub(r"\.(explicit|implicit) operator.*$", ".op", m)
    return normalize_type(m.rsplit(".", 1)[0] if "." in m else m)


KEYWORDS = {"string": "System.String", "object": "System.Object", "int": "System.Int32",
            "long": "System.Int64", "double": "System.Double", "float": "System.Single",
            "decimal": "System.Decimal", "char": "System.Char", "bool": "System.Boolean",
            "byte": "System.Byte", "short": "System.Int16", "uint": "System.UInt32",
            "ulong": "System.UInt64"}


def normalize_type(t):
    if t.startswith("("):
        return "System.ValueTuple"
    return KEYWORDS.get(t, t)


AREAS = ["System.Text.RegularExpressions", "System.Linq.Expressions", "System.Xml", "System.IO",
         "System.Text.Json", "System.Text", "System.Globalization", "System.Reflection",
         "System.Threading", "System.Collections", "System.ComponentModel", "System.Linq",
         "System.Runtime", "System.Diagnostics", "System.Numerics", "System.Buffers", "System.Net"]


def area_of(owner):
    if owner == "System.Uri":
        return "System.Net"
    for a in AREAS:
        if owner == a or owner.startswith(a + "."):
            return a
    return "System (core types)" if owner.startswith("System.") else "language"


def classify_diag(code, msg):
    """-> (construct kind, owning type or construct, member/detail) for one diagnostic."""
    msg = re.sub(r"^not supported by csharpc yet: ", "", msg)
    m = re.match(r"'(.*)' has no implementation in the csharpc runtime yet", msg)
    if m:
        member = strip_generics(m.group(1))
        return "missing BCL member", member_owner(m.group(1)), member
    m = re.match(r"creating instances of '(.*)'", msg)
    if m:
        t = normalize_type(strip_generics(m.group(1)))
        return "constructing a BCL class", t, f"new {t}"
    m = re.match(r"struct '(.*)'", msg)
    if m:
        t = normalize_type(strip_generics(m.group(1)))
        return "BCL struct", t, t
    m = re.match(r"deriving from library class '(.*)'", msg)
    if m:
        t = strip_generics(m.group(1))
        return "deriving from a BCL class", t, f"class : {t}"
    m = re.match(r"(type test against|formatting|setting|operator) '(.*)'", msg)
    if m:
        t = strip_generics(m.group(2))
        owner = member_owner(t) if m.group(1) in ("setting", "operator") else normalize_type(t)
        return m.group(1), owner, f"{m.group(1)} {t}"
    m = re.match(r"list pattern on '(.*)'", msg)
    if m:
        return "list pattern on List<T>", "list pattern on List<T>", "list pattern on List<T>"
    m = re.match(r"reference assemblies lack '(.*)'", msg)
    if m:
        return "reference assemblies", m.group(1), msg
    if msg.startswith("operation DynamicInvocation") or "Dynamic" in msg:
        return "dynamic", "dynamic", msg
    construct = re.sub(r"'[^']*'", "'…'", msg)
    return "language construct", construct, construct


def aot_reason(detail):
    """'output differs (warnings IL2026); -x' -> 'output differs'."""
    return re.split(r"[:;(]", detail, 1)[0].strip() or "unknown"


def median(xs):
    xs = sorted(xs)
    n = len(xs)
    return (xs[n // 2] if n % 2 else (xs[n // 2 - 1] + xs[n // 2]) / 2) if xs else 0


def kib(n):
    return f"{n / 1024:,.0f} KiB" if n < 10 * 1024 * 1024 else f"{n / 1024 / 1024:.1f} MiB"


def main():
    work = sys.argv[1]
    urls = {}
    for line in read(os.path.join(work, "programs.tsv")).splitlines():
        if line.startswith("#") or not line.strip():
            continue
        f = line.split("\t")
        urls[f[0]] = f[4]
    ids = [l.split("\t")[0] for l in read(os.path.join(work, "selected.tsv")).splitlines() if l.strip()]
    results, aot, metrics = {}, {}, {}
    for pid in ids:
        r = read(os.path.join(work, "results", pid + ".tsv")).rstrip("\n").split("\t")
        if len(r) >= 2:
            results[pid] = (r[1], r[2] if len(r) > 2 else "")
        # Columns 4-9 (run.sh with NativeAOT): aot, aot detail, csharpc ms, NativeAOT ms, bytes, bytes.
        if len(r) >= 9 and r[3] != "-":
            aot[pid] = (r[3], r[4])
            metrics[pid] = tuple(int(x) if x.isdigit() else None for x in r[5:9])
    counts = collections.Counter(c for c, _ in results.values())
    attempted = len(results) - counts["SKIPPED"]

    out = []
    w = out.append
    w(f"## Results ({len(results)} of {len(ids)} programs finished)\n")

    # The headline: of the programs NativeAOT publishes and runs exactly like the JIT, how many does
    # csharpc match (ADR 0016)?
    in_scope = sorted(p for p, (c, _) in results.items() if c != "SKIPPED" and aot.get(p, ("",))[0] == "NATIVEAOT-OK")
    unsupported = sorted(p for p, (c, _) in results.items() if c != "SKIPPED" and aot.get(p, ("",))[0] == "NATIVEAOT-UNSUPPORTED")
    if aot:
        scope_counts = collections.Counter(results[p][0] for p in in_scope)
        n = len(in_scope)
        w(f"**NativeAOT parity: {scope_counts['PASS']} of {n} programs "
          f"({100 * scope_counts['PASS'] / max(n, 1):.1f} %)** — csharpc PASS among the programs NativeAOT "
          f"publishes and runs exactly like the JIT. {len(unsupported)} compared programs are out of scope "
          f"(NativeAOT-unsupported), {counts['SKIPPED']} skipped under .NET.\n")
        w("| csharpc result (NativeAOT-working programs) | programs | share |")
        w("|---|---:|---:|")
        for c in CATEGORIES[:-1]:
            w(f"| {c} | {scope_counts[c]} | {100 * scope_counts[c] / max(n, 1):.1f} % |")
        w("")
        reasons = collections.Counter(aot_reason(aot[p][1]) for p in unsupported)
        if reasons:
            w("### NativeAOT-unsupported (out of the parity denominator)\n")
            w("| reason | programs | of them csharpc PASS |\n|---|---:|---:|")
            for k, v in reasons.most_common():
                ok = sum(1 for p in unsupported if aot_reason(aot[p][1]) == k and results[p][0] == "PASS")
                w(f"| {k} | {v} | {ok} |")
            w("")
        warned = [p for p in in_scope if aot[p][1].startswith("warnings")]
        if warned:
            codes = collections.Counter(c for p in warned for c in aot[p][1].split(" ", 1)[1].split(","))
            w(f"{len(warned)} of the in-scope programs publish with trim/AOT warnings but run exactly like the "
              f"JIT ({', '.join(f'{k} {v}' for k, v in codes.most_common())}); they stay in scope.\n")
        # Compile time and size: programs both compilers handle (csharpc PASS, NativeAOT OK).
        both = [p for p in in_scope if results[p][0] == "PASS" and None not in metrics.get(p, (None,))]
        if both:
            cms, ams, cb, ab = ([metrics[p][i] for p in both] for i in range(4))
            tr = [metrics[p][0] / max(metrics[p][1], 1) for p in both]
            sr = [metrics[p][2] / max(metrics[p][3], 1) for p in both]
            w(f"### Compile time and size vs NativeAOT ({len(both)} programs: csharpc PASS, NativeAOT OK)\n")
            w("| | csharpc | NativeAOT | csharpc / NativeAOT (median of per-program ratios) | csharpc better in |")
            w("|---|---:|---:|---:|---:|")
            w(f"| compile time, median | {median(cms) / 1000:.1f} s | {median(ams) / 1000:.1f} s | "
              f"{median(tr):.2f} | {sum(1 for x in tr if x < 1)} of {len(both)} |")
            w(f"| compile time, max | {max(cms) / 1000:.1f} s | {max(ams) / 1000:.1f} s | | |")
            w(f"| executable size, median | {kib(median(cb))} | {kib(median(ab))} | {median(sr):.2f} | "
              f"{sum(1 for x in sr if x < 1)} of {len(both)} |")
            w(f"| executable size, max | {kib(max(cb))} | {kib(max(ab))} | | |")
            w("\nCompile time: `csharpc App.csproj -c Release` vs `dotnet publish -c Release -r <rid> "
              "-p:PublishAot=true`, wall clock, both including project evaluation and linking (the publish "
              "also restores and runs csc). Size: the executable only (NativeAOT's symbols are in a "
              "separate `.dbg` file).\n")

    w("### JIT baseline (all compared programs)\n")
    w("| category | programs | share of compared |")
    w("|---|---:|---:|")
    for c in CATEGORIES:
        share = "" if c == "SKIPPED" or not attempted else f"{100 * counts[c] / attempted:.1f} %"
        w(f"| {c} | {counts[c]} | {share} |")
    w(f"\nCompared (not skipped): {attempted}; pass rate {100 * counts['PASS'] / max(attempted, 1):.1f} %")
    compiled = counts["PASS"] + counts["RUNTIME-DIFF"] + counts["RUNTIME-CRASH"]
    w(f"Of the {compiled} programs csharpc compiled, {counts['PASS']} "
      f"({100 * counts['PASS'] / max(compiled, 1):.1f} %) behave identically to .NET.\n")
    repos = collections.defaultdict(collections.Counter)
    for pid, (c, _) in results.items():
        repos[pid.split("-")[0]][c] += 1
        if pid in in_scope:
            repos[pid.split("-")[0]]["in-scope"] += 1
            repos[pid.split("-")[0]]["in-scope-pass"] += c == "PASS"
    w("| corpus | programs | skipped | compared | pass | pass rate | NativeAOT-working | parity |\n"
      "|---|---:|---:|---:|---:|---:|---:|---:|")
    for repo, cnt in sorted(repos.items()):
        n, sk = sum(cnt[c] for c in CATEGORIES), cnt["SKIPPED"]
        parity = f"{100 * cnt['in-scope-pass'] / cnt['in-scope']:.1f} %" if cnt["in-scope"] else "-"
        w(f"| dotnet/{repo} | {n} | {sk} | {n - sk} | {cnt['PASS']} | {100 * cnt['PASS'] / max(n - sk, 1):.1f} % "
          f"| {cnt['in-scope']} | {parity} |")
    w("")
    times = []
    for pid, (c, _) in results.items():
        t = read(os.path.join(work, "logs", pid, "csharpc.ms")).strip()
        if c == "PASS" and t.isdigit():
            times.append(int(t))
    if times:
        times.sort()
        w(f"csharpc compile time of passing programs (incl. project evaluation, -O2, link): median "
          f"{times[len(times) // 2] / 1000:.1f} s, max {times[-1] / 1000:.1f} s.\n")

    skipped = collections.Counter(d for c, d in results.values() if c == "SKIPPED")
    if skipped:
        w("### Skipped\n")
        w("| reason | programs |\n|---|---:|")
        for k, v in skipped.most_common():
            w(f"| {k} | {v} |")
        w("")

    # Blockers: every unsupported diagnostic of every program that failed to compile with one. With
    # NativeAOT results, only the in-scope programs count (what parity needs).
    scope = set(in_scope) if aot else set(results)
    by_kind, by_owner, by_area, by_member = (collections.defaultdict(set) for _ in range(4))
    owners_of = {}
    for pid, (c, _) in results.items():
        if c != "COMPILE-UNSUPPORTED" or pid not in scope:
            continue
        owners = set()
        for line in read(os.path.join(work, "logs", pid, "csharpc.log")).splitlines():
            m = UNSUPPORTED.search(line)
            if not m:
                continue
            kind, owner, member = classify_diag(m.group(1), m.group(2))
            by_kind[kind].add(pid)
            by_owner[owner].add(pid)
            by_area[area_of(owner) if kind not in ("language construct", "dynamic", "list pattern on List<T>") else "language"].add(pid)
            by_member[member].add(pid)
            owners.add(owner)
        owners_of[pid] = owners
    sole = collections.Counter(next(iter(o)) for o in owners_of.values() if len(o) == 1)
    blocked = len(owners_of)

    def table(title, data, header, limit, extra=None):
        w(f"### {title}\n")
        w(f"| # | {header} | programs blocked |" + (" only blocker |" if extra else ""))
        w("|---:|---|---:|" + ("---:|" if extra else ""))
        for i, (k, ps) in enumerate(sorted(data.items(), key=lambda kv: (-len(kv[1]), kv[0]))[:limit], 1):
            w(f"| {i} | `{k}` | {len(ps)} |" + (f" {extra[k]} |" if extra else ""))
        w("")

    w(f"{blocked} {'NativeAOT-working ' if aot else ''}programs fail with explicit csharpc diagnostics. "
      "A program counts once per row even")
    w("when it hits the row several times; most programs hit several rows. \"Only blocker\" = the")
    w("programs for which this type/construct is the sole reason they don't compile.\n")
    table("Blockers by kind", by_kind, "kind of diagnostic", 30)
    table("Blockers by area", by_area, "area (namespace)", 30)
    table("Blockers by type or construct", by_owner, "type or construct", 50, sole)
    table("Blockers by member", by_member, "member or construct", 60)

    errors = collections.Counter()
    for pid, (c, d) in results.items():
        if c == "COMPILE-ERROR":
            m = re.search(r"error ([A-Z]+\d+)", d)
            errors[m.group(1) if m else d[:80]] += 1
    if errors:
        w("### Compile errors (not csharpc diagnostics)\n")
        w("| error | programs |\n|---|---:|")
        for k, v in errors.most_common():
            w(f"| {k} | {v} |")
        w("")

    def scope_tag(pid):
        return "" if not aot else (" [in scope]" if pid in in_scope else " [NativeAOT-unsupported]")

    for cat in ("COMPILE-CRASH", "RUNTIME-CRASH", "RUNTIME-DIFF", "COMPILE-ERROR"):
        bugs = sorted(pid for pid, (c, _) in results.items() if c == cat)
        if not bugs:
            continue
        w(f"### {cat} ({len(bugs)})\n")
        for pid in bugs:
            w(f"- **{pid}**{scope_tag(pid)} ({urls.get(pid, '')}): {results[pid][1][:300]}")
            if cat == "RUNTIME-DIFF":
                diff = read(os.path.join(work, "logs", pid, "diff"), 4000).splitlines()[2:14]
                if diff:
                    w("  ```diff\n" + "\n".join("  " + l[:160] for l in diff) + "\n  ```")
            if cat == "RUNTIME-CRASH":
                err = read(os.path.join(work, "logs", pid, "native.err"), 2000).splitlines()[:4]
                if err:
                    w("  ```\n" + "\n".join("  " + l[:160] for l in err) + "\n  ```")
        w("")

    if unsupported:
        w(f"### NativeAOT-unsupported programs ({len(unsupported)})\n")
        for pid in unsupported:
            w(f"- **{pid}** (csharpc {results[pid][0]}): {aot[pid][1][:240]}")
        w("")

    text = "\n".join(out)
    with open(os.path.join(work, "summary.md"), "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
