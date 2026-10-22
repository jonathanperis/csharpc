#!/usr/bin/env python3
"""Aggregate a compatibility run (run with `python3 -I`).

    python3 -I tools/compat/analyze.py WORK_DIR [--markdown]

Reads WORK_DIR/selected.tsv, results/<id>.tsv and logs/<id>/csharpc.log; prints per-category
counts, the unsupported constructs/APIs ranked by how many programs each blocks, and every bug
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


def main():
    work = sys.argv[1]
    urls = {}
    for line in read(os.path.join(work, "programs.tsv")).splitlines():
        if line.startswith("#") or not line.strip():
            continue
        f = line.split("\t")
        urls[f[0]] = f[4]
    ids = [l.split("\t")[0] for l in read(os.path.join(work, "selected.tsv")).splitlines() if l.strip()]
    results = {}
    for pid in ids:
        r = read(os.path.join(work, "results", pid + ".tsv")).rstrip("\n").split("\t")
        if len(r) >= 2:
            results[pid] = (r[1], r[2] if len(r) > 2 else "")
    counts = collections.Counter(c for c, _ in results.values())
    attempted = len(results) - counts["SKIPPED"]

    out = []
    w = out.append
    w(f"## Results ({len(results)} of {len(ids)} programs finished)\n")
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
    w("| corpus | programs | skipped | compared | pass | pass rate |\n|---|---:|---:|---:|---:|---:|")
    for repo, cnt in sorted(repos.items()):
        n, sk = sum(cnt.values()), cnt["SKIPPED"]
        w(f"| dotnet/{repo} | {n} | {sk} | {n - sk} | {cnt['PASS']} | {100 * cnt['PASS'] / max(n - sk, 1):.1f} % |")
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



    # Blockers: every unsupported diagnostic of every program that failed to compile with one.
    by_kind, by_owner, by_area, by_member = (collections.defaultdict(set) for _ in range(4))
    owners_of = {}
    for pid, (c, _) in results.items():
        if c != "COMPILE-UNSUPPORTED":
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

    w(f"{blocked} programs fail with explicit csharpc diagnostics. A program counts once per row even")
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

    for cat in ("COMPILE-CRASH", "RUNTIME-CRASH", "RUNTIME-DIFF", "COMPILE-ERROR"):
        bugs = sorted(pid for pid, (c, _) in results.items() if c == cat)
        if not bugs:
            continue
        w(f"### {cat} ({len(bugs)})\n")
        for pid in bugs:
            w(f"- **{pid}** ({urls.get(pid, '')}): {results[pid][1][:300]}")
            if cat == "RUNTIME-DIFF":
                diff = read(os.path.join(work, "logs", pid, "diff"), 4000).splitlines()[2:14]
                if diff:
                    w("  ```diff\n" + "\n".join("  " + l[:160] for l in diff) + "\n  ```")
            if cat == "RUNTIME-CRASH":
                err = read(os.path.join(work, "logs", pid, "native.err"), 2000).splitlines()[:4]
                if err:
                    w("  ```\n" + "\n".join("  " + l[:160] for l in err) + "\n  ```")
        w("")

    text = "\n".join(out)
    with open(os.path.join(work, "summary.md"), "w") as f:
        f.write(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
