#!/usr/bin/env bash
# csharpc against NativeAOT on this host, for the programs a docs/il-first row measures (ADR 0023, ADR 0025 phase 2, #347).
#
#   tools/il-first/measure-aot.sh [--runs N] [--callgrind] program.cs...
#
# For each program, one at a time: csharpc's default build (-O2, stripped) and `dotnet publish` (file-based apps publish with
# PublishAot by default) from a fresh directory, so NativeAOT's ILCompiler starts from nothing; compile CPU (user + sys of the
# whole process tree), wall time and peak RSS of each; the size of each executable (NativeAOT keeps its symbols in a separate .dbg
# file, which is not counted); the median wall time of N runs of each (default 7), after checking that both print the same; with
# --callgrind, the instructions each executes (valgrind --tool=callgrind), for CPU-bound programs. Prints one Markdown row per
# program. Run it niced on a shared machine: nice -n 19 ionice -c3 tools/il-first/measure-aot.sh ...
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
[ -d "$HOME/.dotnet11" ] && export PATH="$HOME/.dotnet11:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build "$ROOT/src/Csharpc" -c Release -m:1 -v quiet -nologo > /dev/null
exec python3 - "$ROOT" "$@" <<'PY'
import os, resource, shutil, statistics, subprocess, sys, tempfile, time

root, args = sys.argv[1], sys.argv[2:]
runs, callgrind, programs = 7, False, []
i = 0
while i < len(args):
    if args[i] == '--runs': runs = int(args[i + 1]); i += 2
    elif args[i] == '--callgrind': callgrind = True; i += 1
    else: programs.append(os.path.abspath(args[i])); i += 1
csharpc = os.path.join(root, 'src/Csharpc/bin/Release/net11.0/csharpc.dll')

# Each measurement runs in a process of its own, so getrusage's peak RSS is that command's alone (the children's maximum is per process).
HELPER = """
import resource, subprocess, sys, time
start = time.monotonic()
r = subprocess.run(sys.argv[1:], capture_output=True, text=True)
wall = time.monotonic() - start
u = resource.getrusage(resource.RUSAGE_CHILDREN)
if r.returncode != 0: sys.stdout.write(r.stdout[-2000:]); sys.stderr.write(r.stderr[-2000:]); sys.exit(1)
print(u.ru_utime + u.ru_stime, wall, u.ru_maxrss // 1024)
"""

def measured(cmd, cwd):
    """CPU seconds (user + sys) of the whole process tree, wall seconds and peak RSS (MB) of one command."""
    r = subprocess.run([sys.executable, '-c', HELPER, *cmd], cwd=cwd, capture_output=True, text=True)
    if r.returncode != 0: sys.exit(f"{' '.join(cmd)} failed:\n{r.stdout}\n{r.stderr}")
    cpu, wall, rss = r.stdout.split()[-3:]
    return float(cpu), float(wall), int(rss)

# Every program runs (and valgrind with it) inside the program's own temporary directory, deleted afterwards, so a core dump of a
# crashing run never lands in the caller's directory (or a checkout).
def run_median(exe, cwd):
    times, out = [], None
    for _ in range(runs):
        start = time.monotonic()
        r = subprocess.run([exe], cwd=cwd, capture_output=True)
        times.append(time.monotonic() - start)
        out = r.stdout
    return statistics.median(times), out

def instructions(exe, cwd):
    r = subprocess.run(['valgrind', '--tool=callgrind', '--callgrind-out-file=/dev/null', exe], cwd=cwd, capture_output=True, text=True)
    for line in r.stderr.splitlines():
        if 'Collected :' in line: return int(line.split(':')[-1].strip())
    return None

def fmt_size(b): return f"{b:,}".replace(',', ' ')

print('| program | csharpc size (B) | NativeAOT size (B) | csharpc compile cpu / wall / peak | NativeAOT publish cpu / wall / peak | run, csharpc / NativeAOT (median of %d) | ratio |%s' % (runs, ' instructions, csharpc / NativeAOT |' if callgrind else ''))
print('|---|---|---|---|---|---|---|%s' % ('---|' if callgrind else ''))
for program in programs:
    name = os.path.splitext(os.path.basename(program))[0]
    work = tempfile.mkdtemp(prefix=f'measure-aot-{name}-')
    try:
        src = os.path.join(work, name + '.cs')
        shutil.copy(program, src)
        exe = os.path.join(work, name + '.csharpc')
        c_cpu, c_wall, c_rss = measured(['dotnet', csharpc, src, '-o', exe], work)
        aot_dir = os.path.join(work, 'aot')
        a_cpu, a_wall, a_rss = measured(['dotnet', 'publish', src, '-o', aot_dir, '--disable-build-servers', '-p:UseSharedCompilation=false'], work)
        aot = os.path.join(aot_dir, name)
        c_time, c_out = run_median(exe, work)
        a_time, a_out = run_median(aot, work)
        if c_out != a_out: sys.exit(f'{name}: csharpc and NativeAOT print different output')
        row = (f'| {name} | {fmt_size(os.path.getsize(exe))} | {fmt_size(os.path.getsize(aot))} | {c_cpu:.1f} s / {c_wall:.1f} s / {c_rss} MB '
               f'| {a_cpu:.1f} s / {a_wall:.1f} s / {a_rss} MB | {c_time * 1000:.0f} / {a_time * 1000:.0f} ms | {c_time / a_time:.2f}x |')
        if callgrind:
            ci, ai = instructions(exe, work), instructions(aot, work)
            row += f' {fmt_size(ci) if ci else "-"} / {fmt_size(ai) if ai else "-"} |'
        print(row, flush=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
PY
