#!/usr/bin/env python3
"""A/B the cost of --hardening levels on benchmarks/programs/*.cs.

    tools/hardening-bench.py [filter] [--runs N]

Compiles every benchmark at -O2 with --hardening none|default|max, checks the output matches the
recorded .NET output, and reports best-of-N CPU time (user+sys, from wait4) plus the relative cost.
CPU time is used because wall time is too noisy on shared machines.
"""
import os, subprocess, sys, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROGRAMS = os.path.join(ROOT, "benchmarks", "programs")
CSHARPC = ["dotnet", os.path.join(ROOT, "src/Csharpc/bin/Release/net11.0/csharpc.dll")]
LEVELS = ["none", "default", "max"]

def cpu_time(exe, expected):
    with open(os.devnull, "w") as devnull, tempfile.TemporaryFile() as out:
        proc = subprocess.Popen([exe], stdout=out, stderr=devnull)
        _, status, usage = os.wait4(proc.pid, 0)
        out.seek(0)
        ok = out.read() == expected and os.waitstatus_to_exitcode(status) == 0
    return usage.ru_utime + usage.ru_stime, ok

def main():
    args = sys.argv[1:]
    runs = 5
    if "--runs" in args:
        i = args.index("--runs"); runs = int(args[i + 1]); del args[i:i + 2]
    flt = args[0] if args else ""
    work = tempfile.mkdtemp()
    print(f"| benchmark | {' | '.join(LEVELS)} | default cost | max cost |")
    print("|---|" + "---:|" * (len(LEVELS) + 2))
    totals = {l: 0.0 for l in LEVELS}
    for name in sorted(f[:-3] for f in os.listdir(PROGRAMS) if f.endswith(".cs")):
        if flt and flt not in name: continue
        expected = open(os.path.join(PROGRAMS, name + ".expected"), "rb").read()
        best = {}
        for level in LEVELS:
            exe = os.path.join(work, f"{name}-{level}")
            r = subprocess.run(CSHARPC + ["-O2", f"--hardening={level}", os.path.join(PROGRAMS, name + ".cs"), "-o", exe],
                               capture_output=True, text=True)
            if r.returncode != 0:
                best[level] = None; print(r.stderr, file=sys.stderr); continue
            times = []
            for _ in range(runs):
                t, ok = cpu_time(exe, expected)
                if not ok: times = None; break
                times.append(t)
            best[level] = min(times) if times else None
        cells = [f"{best[l]*1000:.0f}" if best[l] else "FAIL" for l in LEVELS]
        def cost(l):
            return f"{(best[l] / best['none'] - 1) * 100:+.1f}%" if best[l] and best["none"] else "-"
        print(f"| {name} | {' | '.join(cells)} | {cost('default')} | {cost('max')} |", flush=True)
        if all(best.values()):
            for l in LEVELS: totals[l] += best[l]
    if totals["none"]:
        print(f"| **total** | {' | '.join(f'{totals[l]*1000:.0f}' for l in LEVELS)} | "
              f"{(totals['default']/totals['none']-1)*100:+.1f}% | {(totals['max']/totals['none']-1)*100:+.1f}% |")

if __name__ == "__main__":
    main()
