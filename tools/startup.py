#!/usr/bin/env python3
"""Process start-up and memory footprint: run a command N times, report the median and best wall
time per run, and the peak resident set size (max RSS) of the child.

    tools/startup.py [-n 200] -- ./hello
    tools/startup.py -n 100 -- dotnet hello.dll

Used for the "Size, start-up and memory" table in benchmarks/RESULTS.md.
"""
import argparse
import os
import statistics
import subprocess
import sys
import time


HERE = os.path.dirname(os.path.abspath(__file__))


def maxrss_kib(cmd):
    """Peak RSS of one run, via the tiny C launcher in tools/maxrss.c (built on first use)."""
    exe = os.path.join(os.environ.get("TMPDIR", "/tmp"), "csharpc-maxrss")
    if not os.path.exists(exe):
        subprocess.run(["cc", "-O2", "-o", exe, os.path.join(HERE, "maxrss.c")], check=True)
    result = subprocess.run([exe, *cmd], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, check=True)
    return int(result.stderr.strip().splitlines()[-1])


def measure(cmd, runs):
    times = []
    for _ in range(runs):
        start = time.perf_counter()
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        times.append(time.perf_counter() - start)
    return statistics.median(times), min(times), maxrss_kib(cmd)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("-n", "--runs", type=int, default=200)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    cmd = [c for c in args.command if c != "--"]
    if not cmd:
        parser.error("missing command")
    measure(cmd, min(5, args.runs))  # warm the page cache
    median, best, rss = measure(cmd, args.runs)
    size = os.path.getsize(cmd[0]) if os.path.exists(cmd[0]) else 0
    print(f"{' '.join(cmd)}: median {median * 1000:.2f} ms, best {best * 1000:.2f} ms, "
          f"max RSS {rss / 1024:.1f} MiB, file {size / 1024:.1f} KiB")


if __name__ == "__main__":
    sys.exit(main())
