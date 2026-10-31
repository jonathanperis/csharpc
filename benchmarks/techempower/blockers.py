#!/usr/bin/env python3
"""Ranks what stops a csharpc build (benchmarks/techempower/run.sh): the CSLLVM errors of its log,
grouped like the IL survey groups them (tools/il-survey/survey.py) and ranked by the number of methods
each item stops; the CSLLVM0007 warnings (failures in methods the program can't reach, compiled to
stubs) are counted separately.

    blockers.py csharpc.build.log > blockers.md
"""
import os
import re
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "tools", "il-survey"))
import survey  # noqa: E402  (classify, split_site, DIAG)


def rank(log):
    """{"methods": n, "stubs": n, "items": [{"item", "methods", "where"}], "other": [lines]} of a build log."""
    methods, assemblies = defaultdict(set), defaultdict(Counter)
    stubs = defaultdict(set)
    other = []
    for line in log.splitlines():
        m = survey.DIAG.search(line)
        if m and m.group(1) == "warning" and m.group(2) != "CSLLVM0007":
            continue  # informational (e.g. CSLLVM0006, stack-trace frames): not a blocker
        if not m:
            if re.search(r"error (CS\d+|MSB\d+)|internal compiler error|Unhandled exception", line):
                other.append(line.strip())
            continue
        kind, key = survey.classify(m.group(3))
        method, assembly = survey.split_site(m.group(3))
        target = stubs if m.group(2) == "CSLLVM0007" else methods
        target[(kind, key)].add((assembly, method or line))
        if target is methods:
            assemblies[(kind, key)][assembly] += 1
    items = []
    for (kind, key), ms in sorted(methods.items(), key=lambda kv: (-len(kv[1]), kv[0][1])):
        items.append({"item": key if kind == "member" else f"{kind}: {key}", "methods": len(ms),
                      "where": [[a or "(program or corelib)", c] for a, c in assemblies[(kind, key)].most_common(3)]})
    return {"methods": len({method for v in methods.values() for method in v}), "stubs": sum(len(v) for v in stubs.values()),
            "items": items, "other": other[:30]}


def markdown(ranked):
    lines = [f"{ranked['methods']} reachable methods fail to compile, {len(ranked['items'])} distinct items "
             f"({ranked['stubs']} more failures are in methods the program can't reach: compiled to stubs).", ""]
    if ranked["items"]:
        lines += ["| # | Methods | Item | Where |", "|---:|---:|---|---|"]
        for n, item in enumerate(ranked["items"], 1):
            where = ", ".join(f"{a} {c}" for a, c in item["where"])
            lines.append(f"| {n} | {item['methods']} | `{item['item'].replace('|', '¦')}` | {where} |")
    if ranked["other"]:
        lines += ["", "Other errors:", ""] + [f"    {line[:300]}" for line in ranked["other"]]
    return "\n".join(lines)


def main():
    print(markdown(rank(open(sys.argv[1], encoding="utf-8", errors="replace").read())))


if __name__ == "__main__":
    main()
