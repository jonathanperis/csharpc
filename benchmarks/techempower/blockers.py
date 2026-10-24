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


def main():
    log = open(sys.argv[1], encoding="utf-8", errors="replace").read()
    methods, assemblies = defaultdict(set), defaultdict(Counter)
    stubs = defaultdict(set)
    other = []
    for line in log.splitlines():
        m = survey.DIAG.search(line)
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
    blocked = {method for v in methods.values() for method in v}
    print(f"{len(blocked)} reachable methods fail to compile, {len(methods)} distinct items "
          f"({sum(len(v) for v in stubs.values())} more failures are in methods the program can't reach: compiled to stubs).")
    print()
    if methods:
        print("| # | Methods | Item | Where |")
        print("|---:|---:|---|---|")
        ranked = sorted(methods.items(), key=lambda kv: (-len(kv[1]), kv[0][1]))
        for n, ((kind, key), ms) in enumerate(ranked, 1):
            where = ", ".join(f"{a} {c}" for a, c in assemblies[(kind, key)].most_common(3))
            label = key if kind == "member" else f"{kind}: {key}"
            print(f"| {n} | {len(ms)} | `{label.replace('|', '¦')}` | {where} |")
    if other:
        print()
        print("Other errors:")
        print()
        for line in other[:30]:
            print(f"    {line[:300]}")


if __name__ == "__main__":
    main()
