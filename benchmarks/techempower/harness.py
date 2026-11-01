#!/usr/bin/env python3
"""M7 harness (docs/06-roadmap.md, "M7"): TechEmpower's ASP.NET Core [Platform] entry
(benchmarks/techempower/app) built with NativeAOT, with csharpc and on the JIT, verified with
TechEmpower's own rules and loaded the way TechEmpower loads it; every metric side by side, with a
mechanical csharpc-vs-NativeAOT verdict per metric.

    harness.py build   --app platform --out DIR [--only aot,csharpc,jit] [--images] [--profile]
    harness.py measure --app platform --builds DIR --test json --out DIR [--rounds 3] [--jit-rounds 1]
    harness.py report  --out DIR INPUT_DIR...

build: NativeAOT as the entry's aspnetcore-aot.dockerfile publishes it (PublishAot, OptimizationPreference=
Speed, GarbageCollectionAdaptationMode=0, Npgsql); the JIT as aspnetcore.dockerfile does (framework-dependent,
run with DOTNET_ReadyToRun=0, DOTNET_GCDynamicAdaptationMode=0, DOTNET_HillClimbing_Disable=1); csharpc from
the same IL NativeAOT compiles (`dotnet build` with the AOT properties, then `csharpc Platform.dll`), plus a
check of csharpc's project input (`csharpc Platform.csproj`). Records the build time, the executable size
and, with --images, the size of a minimal runtime image per build (Microsoft's chiseled runtime-deps image for
the native builds, chiseled aspnet for the JIT, at the SDK's runtime version), smoke-tested with one request. When csharpc can't build the app, DIR/csharpc-blockers.md ranks
what stops it. (The entry prints an exception when its database is unreachable, so csharpc's build records
stack traces, through all of its Npgsql and ASP.NET Core frames: docs/adr/0012-stack-traces.md.)

measure: one TechEmpower test type (plaintext, json, db, query, fortune, update, cached-query) or `deploy`
(start-up to the first response, which includes the entry's 10,000-query cache fill, and idle RSS, 5 starts per round; recorded beside them, not judged: the
start-up's CPU time and voluntary context switches, and the idle RSS by mapping). Each round restarts every build (the order
alternates between rounds) and runs TechEmpower's script for the type: primer (5 s, 8 connections), warm-up
(DURATION s at 512 connections), then every level TechEmpower uses, DURATION s each (plaintext 256-16384
connections pipelined x16; json, db, fortune 16-512 connections; query and update 1-20 queries and
cached-query 1-100 at 512 connections). Per level: wrk's requests/s, latency (avg, stdev, max, p50/p75/p90/
p99), socket errors and non-2xx answers; from /proc, the server's peak and steady RSS, threads and CPU time per
request. The first round also verifies every build with TechEmpower's verification code (tfb_verify.py) and
records a normalized transcript of its answers (verify.py), which must be identical across builds.

report: medians of the rounds per metric and build, and the verdict "csharpc better / worse / tie": the
difference between csharpc's and NativeAOT's medians must exceed the larger run-to-run range (max - min over
the rounds; the 10th-90th percentile spread beyond 5 samples) of the two builds and 1 % of NativeAOT's
median (exact metrics such as sizes: any difference; single samples such as the build time: 5 %). Errors are a requirement (zero for csharpc), not a comparison.
Writes DIR/results.json and DIR/results.md, and exits 1 when a required build doesn't build, a build fails
TechEmpower's verification, answers differently from NativeAOT, or its image doesn't serve a request.

Linux only (taskset, /proc). The server gets half the CPUs, wrk the rest (with a database test, PostgreSQL --
TechEmpower's image, from the pinned FrameworkBenchmarks commit -- takes one quarter of them). Environment:
DOTNET (dotnet), DURATION (15), PORT (8080), CSHARPC_FLAGS (-O2), TFB_DIR (a FrameworkBenchmarks checkout;
fetched into ~/.cache/csharpc otherwise), TFB_DATABASE_HOST (use this PostgreSQL instead of starting one).
The csharpc server's own knobs pass through its environment: CSRT_POOL_SPIN_US (the thread pool's longest
poll before a worker sleeps, 100; src/runtime/async.c), CSRT_GC_* (src/runtime/gc.c).
PROFILE=1 / --profile: unstripped builds, and the levels in PROFILE_SCENARIOS ("plaintext,256 json,256"; the
CPU-bound fortunes of a fast database: "fortune,256") are sampled with perf on the server
(DIR/<build>.<test>-<level>.perf.txt and .annotate.txt).
"""
import argparse
import json
import os
import re
import shutil
import signal
import socket
import statistics
import subprocess
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
DOTNET = os.environ.get("DOTNET", "dotnet")
TFB_REPO = "https://github.com/TechEmpower/FrameworkBenchmarks.git"
TFB_COMMIT = "57d92fbec6f8fd7431bc77326dd0484e60c96e20"
BUILDS = ["aot", "csharpc", "jit"]
NAMES = {"aot": "NativeAOT", "csharpc": "csharpc", "jit": "JIT"}
DB_CONTAINER = "m7-tfb-database"

CONCURRENCY = [16, 32, 64, 128, 256, 512]
# TechEmpower's test types (toolset/test_types/*, toolset/wrk/*.sh, run-tests.py's default levels).
TESTS = {
    "plaintext": dict(url="/plaintext", levels=[256, 1024, 4096, 16384], script="pipeline", accept="plaintext", db=False,
                      title="Plaintext (pipelined x16)", level="connections"),
    "json": dict(url="/json", levels=CONCURRENCY, script="concurrency", accept="json", db=False, title="JSON serialization",
                 level="connections"),
    "db": dict(url="/db", levels=CONCURRENCY, script="concurrency", accept="json", db=True, title="Single query", level="connections"),
    "query": dict(url="/queries/", levels=[1, 5, 10, 15, 20], script="query", accept="json", db=True, title="Multiple queries",
                  level="queries"),
    "fortune": dict(url="/fortunes", levels=CONCURRENCY, script="concurrency", accept="json", db=True, title="Fortunes",
                    level="connections"),
    "update": dict(url="/updates/", levels=[1, 5, 10, 15, 20], script="query", accept="json", db=True, title="Data updates",
                   level="queries"),
    "cached-query": dict(url="/cached-worlds/", levels=[1, 10, 20, 50, 100], script="query", accept="json", db=True,
                         title="Cached queries", level="queries"),
}
ACCEPT = {
    "json": "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
    "plaintext": "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
}
JIT_ENV = {"DOTNET_GCDynamicAdaptationMode": "0", "DOTNET_ReadyToRun": "0", "DOTNET_HillClimbing_Disable": "1"}

APPS = {
    "platform": dict(
        title="TechEmpower ASP.NET Core [Platform] entry", dir=HERE / "app", project="Platform.csproj", binary="Platform",
        tests=list(TESTS), msbuild=["-p:DatabaseProvider=Npgsql"],
        aot=["-p:OptimizationPreference=Speed", "-p:GarbageCollectionAdaptationMode=0"],
        settings="appsettings.postgresql.json", database=True, csharpc_input="il", counted=True,
        urls=lambda port: {"URLS": f"http://+:{port}"},
        # csharpc builds the entry: a csharpc build failure fails the run (its blockers are the report's
        # csharpc-blockers.md).
        csharpc_may_fail=False),
    "interim": dict(
        title="Interim plaintext/JSON middleware app (not a TechEmpower entry, not counted for M7)", dir=HERE / "interim",
        project="TechEmpower.csproj", binary="TechEmpower", tests=["plaintext", "json"], msbuild=[], aot=[], settings=None,
        database=False, csharpc_input="project", counted=False,
        urls=lambda port: {"ASPNETCORE_URLS": f"http://127.0.0.1:{port}"}, csharpc_may_fail=False),
}


def log(msg):
    print(msg, flush=True)


def run(cmd, logfile=None, env=None, cwd=None, timeout=None):
    """Runs cmd (output to logfile, appended); returns (exit code, seconds)."""
    t0 = time.monotonic()
    out = open(logfile, "a") if logfile else subprocess.DEVNULL
    try:
        if logfile:
            out.write(f"$ {' '.join(map(str, cmd))}\n")
            out.flush()
        rc = subprocess.run(list(map(str, cmd)), stdout=out, stderr=subprocess.STDOUT, env=env, cwd=cwd, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        rc = 124
    finally:
        if logfile:
            out.close()
    return rc, time.monotonic() - t0


def tail(path, n=30):
    try:
        return "".join(open(path, errors="replace").readlines()[-n:])
    except OSError:
        return ""


def ensure_tfb():
    """A FrameworkBenchmarks checkout at TFB_COMMIT (toolset and the aspnetcore entry only)."""
    if os.environ.get("TFB_DIR"):
        return Path(os.environ["TFB_DIR"])
    d = Path.home() / ".cache" / "csharpc" / f"tfb-{TFB_COMMIT[:12]}"
    if (d / "toolset" / "test_types").is_dir():
        return d
    shutil.rmtree(d, ignore_errors=True)
    d.mkdir(parents=True)
    for cmd in (["git", "init", "-q"], ["git", "remote", "add", "origin", TFB_REPO],
                ["git", "sparse-checkout", "set", "toolset", "frameworks/CSharp/aspnetcore"],
                ["git", "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", TFB_COMMIT],
                ["git", "checkout", "-q", "FETCH_HEAD"]):
        subprocess.run(cmd, cwd=d, check=True)
    return d


def docker_address():
    """Where a --network host container listens: this machine, or a remote daemon's (DOCKER_HOST=tcp://host:port)."""
    m = re.match(r"tcp://([^:/]+)", os.environ.get("DOCKER_HOST", ""))
    return socket.gethostbyname(m.group(1)) if m else "127.0.0.1"


def cpu_layout(database):
    """(server, client, database) CPU lists for taskset / docker --cpuset-cpus."""
    n = os.cpu_count() or 1
    span = lambda a, b: str(a) if a == b else f"{a}-{b}"
    if n < 4:
        every = span(0, n - 1)
        return every, every, every
    half = n // 2
    if not database:
        return span(0, half - 1), span(half, n - 1), span(half, n - 1)
    quarter = max(1, n // 4)
    return span(0, half - 1), span(half + quarter, n - 1), span(half, half + quarter - 1)


def cpu_count(cpus):
    total = 0
    for part in cpus.split(","):
        a, _, b = part.partition("-")
        total += int(b or a) - int(a) + 1
    return total


def environment():
    def cmd(c):
        try:
            return subprocess.run(c, capture_output=True, text=True, timeout=30).stdout.strip()
        except (OSError, subprocess.TimeoutExpired):
            return ""
    model = ""
    try:
        model = next((line.split(":", 1)[1].strip() for line in open("/proc/cpuinfo") if line.startswith("model name")), "")
    except OSError:
        pass
    return {"date": time.strftime("%Y-%m-%d", time.gmtime()), "machine": os.uname().machine, "cpus": os.cpu_count(),
            "cpu_model": model, "dotnet": cmd([DOTNET, "--version"]), "commit": cmd(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"]),
            "tfb_commit": TFB_COMMIT, "runner": os.environ.get("RUNNER_NAME", "")}


# ---- build ----------------------------------------------------------------------------------------

def dir_size(path):
    return sum(f.stat().st_size for f in Path(path).rglob("*") if f.is_file())


def build(args):
    app = APPS[args.app]
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    work = out / "work"
    shutil.rmtree(work, ignore_errors=True)
    only = args.only.split(",")
    profile = args.profile or os.environ.get("PROFILE") == "1"
    csharpc_flags = os.environ.get("CSHARPC_FLAGS", "-O2").split()
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    aot_props = ["-p:PublishAot=true"] + app["msbuild"] + app["aot"] + (["-p:StripSymbols=false"] if profile else [])
    if profile:
        csharpc_flags.append("--no-strip")
        env["CSHARPC_RUNTIME_CFLAGS"] = "-DCSRT_ALLOC_PROFILE"
    result = {"app": args.app, "env": environment(), "builds": {}, "profile": profile}

    def sources(build, props):
        """A copy of the app per build (no obj/ shared between the timed builds), restored outside the timed
        build: build time is the compiler's, not NuGet's."""
        dst = work / f"src-{build}"
        shutil.copytree(app["dir"], dst, ignore=shutil.ignore_patterns("bin", "obj"))
        run([DOTNET, "restore", dst / app["project"]] + props, out / "restore.log", env=env)
        return dst / app["project"]

    def finish(build, ok, seconds, binary, status, extra=None):
        entry = {"ok": ok, "status": status, "build_seconds": round(seconds, 1) if ok else None}
        if ok:
            entry["dir"] = str(binary.parent.relative_to(out))
            entry["binary"] = binary.name
            entry["exe_bytes"] = dir_size(binary.parent) if build == "jit" else binary.stat().st_size
            if app["settings"]:
                shutil.copy(app["dir"] / app["settings"], binary.parent / "appsettings.json")
        entry.update(extra or {})
        result["builds"][build] = entry
        log(f"== {build}: {status}")

    if "aot" in only:
        log("== NativeAOT publish")
        project = sources("aot", aot_props)
        rc, s = run([DOTNET, "publish", project, "-c", "Release", "-o", out / "aot"] + aot_props, out / "aot.build.log", env=env)
        finish("aot", rc == 0, s, out / "aot" / app["binary"], f"built in {s:.0f} s" if rc == 0 else "build failed")
        if rc:
            log(tail(out / "aot.build.log"))
    if "jit" in only:
        log("== JIT publish (framework-dependent)")
        jit_props = app["msbuild"] + ["-p:PublishAot=false"]  # the interim project sets PublishAot itself
        project = sources("jit", jit_props)
        rc, s = run([DOTNET, "publish", project, "-c", "Release", "-o", out / "jit"] + jit_props, out / "jit.build.log", env=env)
        finish("jit", rc == 0, s, out / "jit" / (app["binary"] + ".dll"), f"built in {s:.0f} s" if rc == 0 else "build failed")
        if rc:
            log(tail(out / "jit.build.log"))
    if "csharpc" in only:
        build_csharpc(app, sources("csharpc", aot_props), out, work, aot_props, csharpc_flags, env, finish)

    if args.images:
        build_images(app, out, result)
    (out / "build.json").write_text(json.dumps(result, indent=1))
    shutil.rmtree(work, ignore_errors=True)


def build_csharpc(app, project, out, work, aot_props, flags, env, finish):
    sys.path.insert(0, str(HERE))
    import blockers
    log("== csharpc compiler build")
    rc, _ = run([DOTNET, "build", ROOT / "src/Csharpc", "-c", "Release", "-v", "quiet", "-nologo"], out / "csharpc-compiler.build.log", env=env)
    if rc:
        log(tail(out / "csharpc-compiler.build.log"))
        finish("csharpc", False, 0, None, "the compiler doesn't build")
        return
    csharpc = [DOTNET, ROOT / "src/Csharpc/bin/Release/net11.0/csharpc.dll"]
    # Warms csharpc's caches (the C runtime and the corelib, built once per flags in ~/.cache/csharpc), as
    # NativeAOT's are restored before its timed build: build time is the program's compilation.
    (work / "warm").mkdir(parents=True, exist_ok=True)
    (work / "warm" / "hello.cs").write_text('System.Console.WriteLine("hello");\n')
    run(csharpc + [work / "warm" / "hello.cs"] + flags + ["-o", work / "warm" / "hello"], out / "csharpc-warmup.log", env=env)

    log_path = out / "csharpc.build.log"
    binary = out / "csharpc" / app["binary"]
    if app["csharpc_input"] == "il":
        # NativeAOT's pipeline: csc builds the IL with the AOT properties (configuration binding and JSON
        # source generators, trimming feature switches), then the AOT compiler compiles it; csharpc does
        # the second step, with the shared frameworks Platform.runtimeconfig.json lists (as `dotnet Platform.dll`
        # would load them). -v: the framework assemblies taken from IL and each phase's time and memory.
        rc, s1 = run([DOTNET, "build", project, "-c", "Release", "-o", work / "il"] + aot_props, log_path, env=env)
        s2 = 0
        if rc == 0:
            rc, s2 = run(csharpc + [work / "il" / (app["binary"] + ".dll")] + flags
                         + ["-v", "-o", binary], log_path, env=env)
        seconds = s1 + s2
    else:
        rc, seconds = run(csharpc + [project, "-c", "Release"] + flags + ["-v", "-o", binary], log_path, env=env)
    extra = {"input": "IL (dotnet build, then csharpc Platform.dll)" if app["csharpc_input"] == "il" else "project (csharpc TechEmpower.csproj)"}
    if rc == 0:
        status = f"built in {seconds:.0f} s"
    else:
        ranked = blockers.rank(open(log_path, errors="replace").read())
        (out / "csharpc-blockers.md").write_text(blockers.markdown(ranked) + "\n")
        (out / "csharpc-blockers.json").write_text(json.dumps(ranked, indent=1))
        extra["blockers"] = ranked
        status = f"does not compile yet ({ranked['methods']} methods fail, {len(ranked['items'])} items)" if ranked["items"] \
            else "does not compile yet (" + (ranked["other"][0][-160:] if ranked["other"] else "see csharpc.build.log") + ")"
        log(blockers.markdown(ranked)[:4000])
    if app["csharpc_input"] == "il":
        # csharpc's drop-in input, the project itself, reported alongside (it isn't what's measured).
        plog = out / "csharpc-project.build.log"
        prc, ps = run(csharpc + [project, "-c", "Release"] + aot_props + flags + ["-o", work / "project" / app["binary"]], plog, env=env)
        pranked = blockers.rank(open(plog, errors="replace").read())
        extra["project_input"] = {"ok": prc == 0, "seconds": round(ps, 1), "blockers": None if prc == 0 else pranked}
        if prc:
            (out / "csharpc-project-blockers.md").write_text(blockers.markdown(pranked) + "\n")
    finish("csharpc", rc == 0, seconds, binary, status, extra)


def build_images(app, out, result):
    """A minimal runtime image per build, as TechEmpower deploys an entry (its dockerfiles: the published
    output on a runtime image, URLS=http://+:8080), on Microsoft's smallest bases: chiseled runtime-deps for the
    native builds, chiseled aspnet for the JIT."""
    if not shutil.which("docker"):
        log("== images: no docker")
        return
    runtimes = subprocess.run([DOTNET, "--list-runtimes"], capture_output=True, text=True).stdout
    version = re.search(r"Microsoft\.AspNetCore\.App (\d+\.\d+\.\d+(?:-[a-z]+\.\d+)?)", runtimes).group(1)
    for n, (build, entry) in enumerate(result["builds"].items()):
        if not entry["ok"]:
            continue
        ctx = out / "work" / f"image-{build}"
        shutil.rmtree(ctx, ignore_errors=True)
        src = out / entry["dir"]
        if build == "jit":
            shutil.copytree(src, ctx)
            base = f"mcr.microsoft.com/dotnet/aspnet:{version}-resolute-chiseled"
            entrypoint = f'["dotnet", "{entry["binary"]}"]'
            envs = "".join(f"ENV {k}={v}\n" for k, v in JIT_ENV.items())
        else:
            ctx.mkdir(parents=True)
            shutil.copy(src / entry["binary"], ctx / entry["binary"])
            if (src / "appsettings.json").exists():
                shutil.copy(src / "appsettings.json", ctx / "appsettings.json")
            base = f"mcr.microsoft.com/dotnet/runtime-deps:{version}-resolute-chiseled"
            entrypoint = f'["./{entry["binary"]}"]'
            envs = ""
        (ctx / "Dockerfile").write_text(f"FROM {base}\nENV URLS=http://+:8080\n{envs}WORKDIR /app\nCOPY . ./\nEXPOSE 8080\nENTRYPOINT {entrypoint}\n")
        tag = f"m7-{result['app']}-{build}"
        rc, _ = run(["docker", "build", "-q", "-t", tag, ctx], out / "images.log")
        if rc:
            entry["image"] = {"ok": False, "status": "image build failed"}
            continue
        size = int(subprocess.run(["docker", "image", "inspect", "-f", "{{.Size}}", tag], capture_output=True, text=True).stdout.strip())
        # One request through the image. No database: tfb-database is localhost, where nothing answers, so the
        # entry logs at once that its cache can't be filled, and serves.
        port = 18080 + n  # its own port: the previous image's closed connections may sit in TIME_WAIT on its port
        subprocess.run(["docker", "rm", "-f", tag], capture_output=True)
        run(["docker", "run", "-d", "--name", tag, "--network", "host", "--add-host", "tfb-database:127.0.0.2", "-e", f"URLS=http://+:{port}",
             "-e", f"ASPNETCORE_URLS=http://127.0.0.1:{port}", tag], out / "images.log")
        ok = False
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            if http_status(docker_address(), port, "/plaintext") == 200:
                ok = True
                break
            time.sleep(0.1)
        with open(out / "images.log", "a") as f:
            logs = subprocess.run(["docker", "logs", tag], capture_output=True, text=True)
            f.write((logs.stdout + logs.stderr)[-3000:])
        subprocess.run(["docker", "rm", "-f", tag], capture_output=True)
        entry["image"] = {"ok": ok, "bytes": size, "base": base, "status": "serves /plaintext" if ok else "no answer from the image"}
        log(f"== image {build}: {size / 1048576:.1f} MiB, {'ok' if ok else 'NO ANSWER'}")


# ---- measure --------------------------------------------------------------------------------------

def http_status(host, port, path, timeout=1.0):
    try:
        with socket.create_connection((host, port), timeout=timeout) as s:
            s.sendall(f"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n".encode())
            line = s.makefile("rb").readline().decode("latin-1")
            return int(line.split()[1]) if line.startswith("HTTP/") else None
    except (OSError, ValueError, IndexError):
        return None


def proc_status(pid):
    """(VmRSS KiB, VmHWM KiB, threads)"""
    rss = hwm = threads = None
    try:
        for line in open(f"/proc/{pid}/status"):
            if line.startswith("VmRSS:"):
                rss = int(line.split()[1])
            elif line.startswith("VmHWM:"):
                hwm = int(line.split()[1])
            elif line.startswith("Threads:"):
                threads = int(line.split()[1])
    except OSError:
        pass
    return rss, hwm, threads


def rss_breakdown(pid):
    """Idle RSS by mapping, KiB, from /proc/PID/smaps: anonymous (the GC heap, malloc, stacks; in mappings of at
    least 1 MiB or smaller) and file-backed (the executable, shared libraries). Informational: the server log and
    the measure JSON, outside the verdict."""
    out = {"anon": 0, "file": 0, "binary": 0, "libraries": 0, "anon_large": 0, "anon_small": 0}
    try:
        exe = os.path.realpath(f"/proc/{pid}/exe")
        name = size = None
        for line in open(f"/proc/{pid}/smaps"):
            head = line.split()
            if "-" in head[0] and len(head) >= 5 and not head[0].endswith(":"):
                lo, hi = (int(x, 16) for x in head[0].split("-"))
                size, name = hi - lo, (head[5] if len(head) > 5 else "")
            elif head[0] == "Rss:":
                kib = int(head[1])
                if not name or name.startswith("[heap]") or name.startswith("[stack"):
                    out["anon"] += kib
                    out["anon_large" if size >= 1 << 20 else "anon_small"] += kib
                else:
                    out["file"] += kib
                    out["binary" if name == exe else "libraries"] += kib
    except (OSError, ValueError):
        pass
    return out


def context_switches(pid):
    """Voluntary context switches of all the server's threads so far: a latency-bound start-up's thread hand-offs
    (a thread that blocks to wait for another one)."""
    total = 0
    try:
        for tid in os.listdir(f"/proc/{pid}/task"):
            try:
                for line in open(f"/proc/{pid}/task/{tid}/status"):
                    if line.startswith("voluntary_ctxt_switches:"):
                        total += int(line.split()[1])
            except OSError:
                pass  # the thread exited
    except OSError:
        return None
    return total


def cpu_ticks(pid):
    try:
        fields = open(f"/proc/{pid}/stat").read().rsplit(")", 1)[1].split()
        return int(fields[11]) + int(fields[12])  # utime + stime, all threads
    except (OSError, IndexError, ValueError):
        return None


class Sampler(threading.Thread):
    """RSS and thread count of the server every 200 ms."""

    def __init__(self, pid):
        super().__init__(daemon=True)
        self.pid, self.samples, self.stop = pid, [], threading.Event()

    def run(self):
        while not self.stop.is_set():
            rss, _, threads = proc_status(self.pid)
            if rss is not None:
                self.samples.append((rss, threads))
            self.stop.wait(0.2)

    def finish(self):
        self.stop.set()
        self.join()
        if not self.samples:
            return {}
        rss = [s[0] for s in self.samples]
        second_half = rss[len(rss) // 2:]
        return {"rss_peak_kib": max(rss), "rss_steady_kib": statistics.median(second_half),
                "threads": max(s[1] for s in self.samples)}


def parse_wrk(text):
    def ms(value):
        m = re.match(r"([\d.]+)(us|ms|s|m)$", value)
        if not m:
            return None
        return float(m.group(1)) * {"us": 0.001, "ms": 1, "s": 1000, "m": 60000}[m.group(2)]
    r = {}
    m = re.search(r"^\s+Latency\s+(\S+)\s+(\S+)\s+(\S+)", text, re.M)
    if m:
        r["lat_avg"], r["lat_stdev"], r["lat_max"] = (ms(v) for v in m.groups())
    for p in ("50", "75", "90", "99"):
        m = re.search(rf"^\s+{p}%\s+(\S+)", text, re.M)
        if m:
            r[f"lat_p{p}"] = ms(m.group(1))
    m = re.search(r"^\s+(\d+) requests in ([\d.]+)(\w+)", text, re.M)
    if m:
        r["requests"] = int(m.group(1))
    m = re.search(r"Socket errors: connect (\d+), read (\d+), write (\d+), timeout (\d+)", text)
    r["socket_errors"] = sum(map(int, m.groups())) if m else 0
    m = re.search(r"Non-2xx or 3xx responses: (\d+)", text)
    r["non_2xx"] = int(m.group(1)) if m else 0
    m = re.search(r"^Requests/sec:\s+([\d.]+)", text, re.M)
    if m:
        r["rps"] = float(m.group(1))
    # Pipelined, wrk records the latency of only some batches (most percentiles 0.00us): not a measurement.
    if any(r.get(k) == 0 for k in ("lat_max", "lat_p50", "lat_p75", "lat_p90", "lat_p99")):
        for k in [k for k in r if k.startswith("lat_")]:
            r[k] = None
    return r


class Server:
    def __init__(self, ctx, build):
        self.ctx, self.build = ctx, build
        self.proc = None

    def start(self):
        """Starts the build pinned to the server CPUs; returns ms to the first answer, or raises."""
        ctx, entry = self.ctx, self.ctx.builds[self.build]
        # A port per start: connections the previous server closed sit in TIME_WAIT on its port, and a
        # restart on it then depends on SO_REUSEADDR (which .NET sets before bind, and csharpc too since
        # sample 840): a port per start keeps the rounds independent of it.
        ctx.port = ctx.base_port + ctx.starts % 200
        ctx.starts += 1
        self.port = ctx.port
        cwd = ctx.builds_dir / entry["dir"]
        cmd = [DOTNET, entry["binary"]] if self.build == "jit" else ["./" + entry["binary"]]
        env = dict(os.environ)
        env.update(ctx.app["urls"](ctx.port))
        if self.build == "jit":
            env.update(JIT_ENV)
        if self.build == "csharpc":
            env["CSRT_GC_STATS"] = "1"  # prints the collector's totals (pause time included) at exit
        if ctx.connection_string:
            env["ConnectionString"] = ctx.connection_string
        self.log = ctx.out / f"{ctx.stem}.{self.build}.server.log"
        logf = open(self.log, "a")
        logf.write(f"==== start {time.strftime('%H:%M:%S')}\n")
        logf.flush()
        t0 = time.monotonic()
        self.proc = subprocess.Popen(["taskset", "-c", ctx.server_cpus] + cmd, cwd=cwd, env=env, stdout=logf, stderr=subprocess.STDOUT)
        logf.close()
        deadline = t0 + 120
        while time.monotonic() < deadline:
            if http_status("127.0.0.1", ctx.port, "/plaintext", timeout=0.5) is not None:
                return (time.monotonic() - t0) * 1000
            if self.proc.poll() is not None:
                raise RuntimeError(f"the server exited ({self.proc.returncode}): {tail(self.log, 5)!r}")
            time.sleep(0.005)
        raise RuntimeError("no answer after 120 s")

    def stop(self):
        if not self.proc:
            return None
        if self.proc.poll() is None:
            self.proc.send_signal(signal.SIGTERM)
            try:
                self.proc.wait(15)
            except subprocess.TimeoutExpired:
                self.proc.kill()
                self.proc.wait()
        self.proc = None
        for _ in range(100):  # the port is free again
            if http_status("127.0.0.1", self.port, "/plaintext", timeout=0.2) is None:
                break
            time.sleep(0.1)
        m = None
        for m in re.finditer(r"csrt gc: (\d+) collections.*?, ([\d.]+) ms total", open(self.log, errors="replace").read()):
            pass
        return {"collections": int(m.group(1)), "pause_ms": float(m.group(2))} if m else None


class Context:
    pass


def start_database(tfb, cpus, out):
    if os.environ.get("TFB_DATABASE_HOST"):
        return os.environ["TFB_DATABASE_HOST"]
    running = subprocess.run(["docker", "ps", "-q", "-f", f"name=^{DB_CONTAINER}$"], capture_output=True, text=True).stdout.strip()
    if not running:
        log("== PostgreSQL (TechEmpower's image)")
        rc, _ = run(["docker", "build", "-q", "-t", "m7-tfb-postgres", "-f", "postgres.dockerfile", "."], out / "database.log",
                    cwd=tfb / "toolset" / "databases" / "postgres")
        if rc:
            raise RuntimeError(f"the database image doesn't build: {tail(out / 'database.log')}")
        subprocess.run(["docker", "rm", "-f", DB_CONTAINER], capture_output=True)
        # TechEmpower's settings for the database container (docker_helper.start_database, host network mode).
        rc, _ = run(["docker", "run", "-d", "--name", DB_CONTAINER, "--network", "host", "--ulimit", "nofile=65535:65535",
                     "--sysctl", "kernel.sem=250 32000 256 512", "--cpuset-cpus", cpus, "m7-tfb-postgres"], out / "database.log")
        if rc:
            raise RuntimeError(f"the database doesn't start: {tail(out / 'database.log')}")
        for _ in range(300):
            ok = subprocess.run(["docker", "exec", DB_CONTAINER, "psql", "-h", "127.0.0.1", "-U", "benchmarkdbuser", "-d", "hello_world",
                                 "-tAc", "SELECT count(*) FROM world"], capture_output=True, text=True)
            if ok.returncode == 0 and ok.stdout.strip() == "10000":
                break
            time.sleep(1)
        else:
            raise RuntimeError("the database isn't ready after 300 s")
    subprocess.run(["docker", "update", "--cpuset-cpus", cpus, DB_CONTAINER], capture_output=True)
    return docker_address()


def wrk(ctx, connections, threads, url, pipeline=False, duration=None):
    cmd = ["taskset", "-c", ctx.client_cpus, "wrk", "-H", "Host: localhost", "-H", f"Accept: {ACCEPT[ctx.test['accept']]}",
           "-H", "Connection: keep-alive", "--latency", "-d", str(duration or ctx.duration), "-c", str(connections), "--timeout", "8",
           "-t", str(threads), url]
    if pipeline:
        cmd += ["-s", str(ctx.tfb / "toolset" / "wrk" / "pipeline.lua"), "--", "16"]
    return subprocess.run(cmd, capture_output=True, text=True).stdout


def load_test(ctx, server, build, round_no):
    """TechEmpower's script for the test type (toolset/wrk/{concurrency,pipeline,query}.sh); per level, wrk's
    numbers and the server's resources."""
    test, base = ctx.test, f"http://127.0.0.1:{ctx.port}{ctx.test['url']}"
    max_threads = cpu_count(ctx.client_cpus)
    pid = server.proc.pid
    primer = base + "2" if test["script"] == "query" else base
    transcript = open(ctx.out / f"{ctx.stem}.{build}.wrk.txt", "a")
    transcript.write(f"#### round {round_no}\n### primer\n{wrk(ctx, 8, 8, primer, duration=5)}")
    time.sleep(5)
    transcript.write(f"### warm-up\n{wrk(ctx, max(CONCURRENCY), max_threads, primer)}")
    time.sleep(5)
    levels = []
    for level in test["levels"]:
        if test["script"] == "query":
            connections, url = max(CONCURRENCY), base + str(level)
        else:
            connections, url = level, base
        sampler = Sampler(pid)
        sampler.start()
        ticks0 = cpu_ticks(pid)
        stem = None
        if ctx.profile and build in ("aot", "csharpc") and round_no == 1 and f"{ctx.test_name},{level}" in ctx.profile_scenarios:
            stem = ctx.out / f"{build}.{ctx.test_name}-{level}"
            perf = profile_start(pid, stem, ctx.duration)
        text = wrk(ctx, connections, min(connections, max_threads), url, pipeline=test["script"] == "pipeline")
        if stem:
            profile_finish(perf, stem)
        ticks1 = cpu_ticks(pid)
        r = parse_wrk(text)
        r.update(sampler.finish())
        r["level"] = level
        if ticks0 is not None and ticks1 is not None and r.get("requests"):
            r["cpu_us_per_req"] = (ticks1 - ticks0) / os.sysconf("SC_CLK_TCK") / r["requests"] * 1e6
        levels.append(r)
        transcript.write(f"### {test['level']} {level}\n{text}\n")
        log(f"   {build} round {round_no} {ctx.test_name} {level}: {r.get('rps', 0):.0f} req/s, p99 {r.get('lat_p99')} ms, "
            f"{r.get('socket_errors', 0)} socket errors, {r.get('non_2xx', 0)} non-2xx")
        time.sleep(2)
    transcript.close()
    return levels


def profile_start(pid, stem, duration):
    record = subprocess.Popen(["perf", "record", "-F", "499", "-g", "--call-graph", "dwarf,16384", "-p", str(pid), "-o", f"{stem}.data",
                               "--", "sleep", str(duration)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    stat = subprocess.Popen(["perf", "stat", "-e", "task-clock,context-switches,cpu-migrations,page-faults,cycles,instructions", "-p", str(pid),
                             "-o", f"{stem}.stat", "--", "sleep", str(duration)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return [record, stat]


def profile_finish(procs, stem):
    for p in procs:
        p.wait()
    data = f"{stem}.data"
    sh = lambda c: subprocess.run(c, shell=True, capture_output=True, text=True).stdout
    report = [
        "## perf stat", open(f"{stem}.stat").read() if os.path.exists(f"{stem}.stat") else "",
        "## flat profile (self)", sh(f"perf report -i {data} --no-children --sort dso,sym --percent-limit 0.2 --stdio -g none 2>/dev/null | grep -v '^$' | head -150"),
        "## by thread", sh(f"perf report -i {data} --no-children --sort comm --stdio -g none 2>/dev/null | grep -v '^#' | grep -v '^$' | head -30"),
        "## inclusive (children)", sh(f"perf report -i {data} --children --sort sym --percent-limit 1 --stdio -g none 2>/dev/null | grep -v '^$' | head -140"),
        "## callers of the hottest symbols",
        sh(f"perf report -i {data} --no-children --sort sym --percent-limit 1.5 --stdio -g caller,0.5,callee,function,percent --max-stack 14 2>/dev/null | grep -v '^$' | head -2000"),
    ]
    Path(f"{stem}.perf.txt").write_text("\n".join(report))
    # The hottest functions of the program itself, instruction by instruction.
    top = sh(f"perf report -i {data} --no-children --sort dso,sym --stdio -g none 2>/dev/null"
             " | awk '($2 == \"Platform\" || $2 == \"TechEmpower\") && $3 == \"[.]\" {print $4}' | head -4").split()
    Path(f"{stem}.annotate.txt").write_text("".join(
        f"## annotate {sym}\n" + sh(f"perf annotate -i {data} --stdio -s '{sym}' 2>/dev/null | grep -v '^\\s*0.00 *:' | head -400") for sym in top))
    for f in (data, data + ".old", f"{stem}.stat"):
        if os.path.exists(f):
            os.remove(f)


def verify(ctx, build):
    """TechEmpower's verification and the normalized transcript; returns {"tfb": ok, "transcript": ok, ...}."""
    result = {}
    tests = ctx.app["tests"] if ctx.test_name == "deploy" else [ctx.test_name]
    transcript = ctx.out / f"{ctx.stem}.{build}.transcript.txt"
    with open(transcript, "w") as f, open(ctx.out / f"{ctx.stem}.{build}.transcript.err", "w") as err:
        rc = subprocess.run([sys.executable, "-I", HERE / "verify.py", f"127.0.0.1:{ctx.port}"] + [a for t in tests for a in ("--test", t)],
                            stdout=f, stderr=err).returncode
    result["transcript"] = rc == 0
    if rc:
        result["transcript_errors"] = tail(ctx.out / f"{ctx.stem}.{build}.transcript.err", 5)
    if ctx.test_name != "deploy" and not ctx.no_tfb_verify:
        logfile = ctx.out / f"{ctx.stem}.{build}.tfb-verify.txt"
        with open(logfile, "w") as f:
            rc = subprocess.run([sys.executable, HERE / "tfb_verify.py", "--tfb", ctx.tfb, "--test", ctx.test_name,
                                 "--url", f"http://127.0.0.1:{ctx.port}", "--database-host", ctx.database_host or "127.0.0.1"],
                                stdout=f, stderr=subprocess.STDOUT, timeout=900).returncode
        text = open(logfile, errors="replace").read()
        result["tfb"] = rc == 0
        result["tfb_results"] = [line for line in text.splitlines() if re.match(r"[\w-]+: (PASS|WARN|FAIL) ", line)]
        if rc and not result["tfb_results"]:
            result["tfb_results"] = text.splitlines()[-10:]
    log(f"   {build} verification: transcript {'ok' if result['transcript'] else 'FAILED'}"
        + ("" if "tfb" not in result else f", TechEmpower {'pass' if result['tfb'] else 'FAIL'}"))
    return result


def measure(args):
    ctx = Context()
    ctx.app = APPS[args.app]
    ctx.builds_dir = Path(args.builds).resolve()
    ctx.out = Path(args.out).resolve()
    ctx.out.mkdir(parents=True, exist_ok=True)
    ctx.test_name = args.test
    ctx.test = TESTS.get(args.test, TESTS["plaintext"])
    ctx.stem = f"{args.app}.{args.test}"
    ctx.duration = int(os.environ.get("DURATION", "15"))
    ctx.base_port = ctx.port = int(os.environ.get("PORT", "8080"))
    ctx.starts = 0
    ctx.profile = args.profile or os.environ.get("PROFILE") == "1"
    ctx.profile_scenarios = os.environ.get("PROFILE_SCENARIOS", "plaintext,256 json,256").split()
    ctx.no_tfb_verify = args.no_tfb_verify
    ctx.tfb = ensure_tfb()
    built = json.loads((ctx.builds_dir / "build.json").read_text())
    ctx.builds = {b: e for b, e in built["builds"].items() if e["ok"]}
    uses_db = ctx.app["database"]
    ctx.server_cpus, ctx.client_cpus, db_cpus = cpu_layout(uses_db and ctx.test["db"] and args.test != "deploy")
    os.sched_setaffinity(0, {c for part in ctx.client_cpus.split(",") for c in range(int(part.split("-")[0]), int(part.split("-")[-1]) + 1)})
    try:  # wrk opens up to 16384 connections; the server accepts them
        import resource
        _, hard = resource.getrlimit(resource.RLIMIT_NOFILE)
        resource.setrlimit(resource.RLIMIT_NOFILE, (hard, hard))
    except (ValueError, OSError):
        pass
    ctx.database_host = ctx.connection_string = None
    if uses_db:
        ctx.database_host = start_database(ctx.tfb, db_cpus, ctx.out)
        settings = json.loads((ctx.app["dir"] / ctx.app["settings"]).read_text(encoding="utf-8-sig"))
        ctx.connection_string = settings["ConnectionString"].replace("Server=tfb-database", f"Server={ctx.database_host}")

    result = {"app": args.app, "test": args.test, "env": environment(), "duration": ctx.duration, "rounds": [],
              "cpus": {"server": ctx.server_cpus, "client": ctx.client_cpus, "database": db_cpus if uses_db else None},
              "verification": {}, "failures": {}}
    order = [b for b in ("aot", "csharpc") if b in ctx.builds]
    path = ctx.out / f"measure-{args.app}-{args.test}.json"
    for round_no in range(1, args.rounds + 1):
        builds = order if round_no % 2 else order[::-1]
        if "jit" in ctx.builds and round_no <= args.jit_rounds:
            builds = builds + ["jit"]
        for build in builds:
            if build in result["failures"]:
                continue
            log(f"== {args.app} {args.test}: {build}, round {round_no}")
            entry = {"build": build, "round": round_no}
            server = Server(ctx, build)
            try:
                if args.test == "deploy":
                    entry["startup_ms"], entry["idle_rss_kib"] = [], []
                    for _ in range(5):
                        entry["startup_ms"].append(server.start())
                        # CPU the start-up took, all threads (informational: what the start-up costs beside waiting
                        # for the database's 10,000 answers).
                        ticks = cpu_ticks(server.proc.pid)
                        entry.setdefault("startup_cpu_ms", []).append(ticks * 1000 / os.sysconf("SC_CLK_TCK") if ticks is not None else None)
                        entry.setdefault("startup_context_switches", []).append(context_switches(server.proc.pid))
                        time.sleep(2)
                        entry["idle_rss_kib"].append(proc_status(server.proc.pid)[0])
                        entry.setdefault("idle_rss_split_kib", []).append(rss_breakdown(server.proc.pid))
                        if round_no == 1 and not entry.get("verified"):
                            result["verification"][build] = verify(ctx, build)
                            entry["verified"] = True
                        server.stop()
                    log(f"   start-up {min(entry['startup_ms']):.0f}-{max(entry['startup_ms']):.0f} ms "
                        f"(CPU {', '.join(f'{t:.0f}' for t in entry['startup_cpu_ms'] if t is not None)} ms, voluntary context switches "
                        f"{', '.join(str(c) for c in entry['startup_context_switches'] if c is not None)}), idle RSS "
                        f"{statistics.median(entry['idle_rss_kib']) / 1024:.1f} MiB "
                        f"({', '.join(f'{k} {v / 1024:.1f}' for k, v in entry['idle_rss_split_kib'][-1].items())} MiB)")
                else:
                    entry["startup_ms"] = server.start()
                    if round_no == 1:
                        v = result["verification"][build] = verify(ctx, build)
                        if not v["transcript"] or v.get("tfb") is False:
                            # TechEmpower doesn't benchmark a test that fails verification; neither does this.
                            result["failures"][build] = "fails verification: not loaded"
                            server.stop()
                            path.write_text(json.dumps(result, indent=1))
                            continue
                    if not args.no_load:
                        entry["levels"] = load_test(ctx, server, build, round_no)
                        entry["peak_rss_kib"] = proc_status(server.proc.pid)[1]
                    entry["gc"] = server.stop()
            except Exception as e:  # noqa: BLE001 -- a build that doesn't start or dies is a result, not a crash
                result["failures"][build] = f"{type(e).__name__}: {e}"
                log(f"   {build}: {e}")
                server.stop()
                continue
            result["rounds"].append(entry)
            path.write_text(json.dumps(result, indent=1))
    path.write_text(json.dumps(result, indent=1))


# ---- report ---------------------------------------------------------------------------------------

LOWER, HIGHER = "lower", "higher"
LEVEL_METRICS = [  # key, label, unit, better
    ("rps", "requests/s", "req/s", HIGHER),
    ("lat_avg", "latency avg", "ms", LOWER), ("lat_stdev", "latency stdev", "ms", LOWER), ("lat_max", "latency max", "ms", LOWER),
    ("lat_p50", "latency p50", "ms", LOWER), ("lat_p75", "latency p75", "ms", LOWER), ("lat_p90", "latency p90", "ms", LOWER),
    ("lat_p99", "latency p99", "ms", LOWER),
    ("cpu_us_per_req", "CPU time per request", "us", LOWER),
    ("rss_peak_kib", "peak RSS", "KiB", LOWER), ("rss_steady_kib", "steady RSS", "KiB", LOWER),
    ("threads", "threads", "", LOWER),
]


def spread(values):
    """Run-to-run variation: max - min of the rounds; with more than 5 samples (start-up: 5 starts per round),
    the 10th-90th percentile spread, so that one outlier start doesn't hide a 2x difference."""
    if len(values) <= 5:
        return max(values) - min(values)
    q = statistics.quantiles(values, n=10, method="inclusive")
    return q[-1] - q[0]


def verdict(metric):
    """Fills metric["median"], ["range"], ["noise"], ["verdict"] from metric["samples"]."""
    s = metric["samples"]
    med = {b: statistics.median(v) for b, v in s.items() if v}
    metric["median"] = med
    metric["range"] = {b: spread(v) for b, v in s.items() if v}
    if metric.get("requirement"):
        c = s.get("csharpc")
        metric["verdict"] = "no result" if not c else "met" if max(c) == 0 else "not met"
        return
    if "aot" not in med or metric.get("unmeasured"):
        metric["verdict"] = "n/a"
        return
    if "csharpc" not in med:
        metric["verdict"] = "no result"
        return
    a, c = med["aot"], med["csharpc"]
    n = min(len(s["aot"]), len(s["csharpc"]))
    floor = 0.0 if metric.get("exact") else 0.05 if n == 1 else 0.01
    noise = max(metric["range"]["aot"], metric["range"]["csharpc"], floor * abs(a))
    metric["noise"] = noise
    if abs(c - a) <= noise:
        metric["verdict"] = "tie"
    else:
        metric["verdict"] = "better" if (c > a) == (metric["better"] == HIGHER) else "worse"


def collect(inputs):
    builds, measures = {}, {}
    for d in inputs:
        for f in Path(d).rglob("build.json"):
            b = json.loads(f.read_text())
            b["path"] = str(f.parent)
            builds[b["app"]] = b
        for f in Path(d).rglob("measure-*.json"):
            m = json.loads(f.read_text())
            m["path"] = str(f.parent)
            measures[(m["app"], m["test"])] = m
    return builds, measures


def app_metrics(app, built, measures):
    metrics = []

    def add(group, test, level, key, label, unit, better, samples, **kw):
        m = dict(app=app, group=group, test=test, level=level, metric=key, label=label, unit=unit, better=better,
                 samples={b: [x for x in v if x is not None] for b, v in samples.items()}, **kw)
        m["samples"] = {b: v for b, v in m["samples"].items() if v}
        verdict(m)
        metrics.append(m)

    bs = (built or {}).get("builds", {})
    one = lambda key: {b: [e[key]] for b, e in bs.items() if e.get("ok") and e.get(key) is not None}
    deploy = measures.get((app, "deploy"))
    if deploy:
        per = lambda key: {b: [x for r in deploy["rounds"] if r["build"] == b for x in r.get(key, [])] for b in BUILDS}
        add("deployment", None, None, "startup_ms", "start-up to the first response", "ms", LOWER, per("startup_ms"))
        add("deployment", None, None, "idle_rss_kib", "idle RSS", "KiB", LOWER, per("idle_rss_kib"))
    add("deployment", None, None, "exe_bytes", "executable size", "bytes", LOWER, one("exe_bytes"), exact=True,
        note="JIT: the framework-dependent publish directory, without the shared runtime")
    add("deployment", None, None, "build_seconds", "build time", "s", LOWER, one("build_seconds"))
    add("deployment", None, None, "image_bytes", "container image size", "bytes", LOWER,
        {b: [e["image"]["bytes"]] for b, e in bs.items() if e.get("ok") and e.get("image", {}).get("bytes")}, exact=True)
    for test in APPS[app]["tests"]:
        m = measures.get((app, test))
        if not m:
            continue
        spec = TESTS[test]
        rounds = [r for r in m["rounds"] if "levels" in r]
        for level in spec["levels"]:
            def samples(key):
                return {b: [lv.get(key) for r in rounds if r["build"] == b for lv in r["levels"] if lv["level"] == level] for b in BUILDS}
            served = {b for b, v in samples("rps").items() if b != "jit" and any(x for x in v if x)}
            for key, label, unit, better in LEVEL_METRICS:
                kw = {}
                if key.startswith("lat_") and not {b for b, v in samples(key).items() if any(x is not None for x in v)} >= served:
                    # A build served the level but wrk recorded no latency for it (pipelined batches): unmeasured.
                    kw["unmeasured"] = True
                add(test, test, level, key, label, unit, better, samples(key), **kw)
            errs = {b: [(lv.get("socket_errors") or 0) + (lv.get("non_2xx") or 0) for r in rounds if r["build"] == b
                        for lv in r["levels"] if lv["level"] == level] for b in BUILDS}
            add(test, test, level, "errors", "errors (socket + non-2xx)", "", LOWER, errs, requirement=True)
        add(test, test, None, "peak_rss_kib", "peak RSS over the run", "KiB", LOWER,
            {b: [r.get("peak_rss_kib") for r in rounds if r["build"] == b] for b in BUILDS})
        gc = {b: [r["gc"]["pause_ms"] for r in rounds if r["build"] == b and r.get("gc")] for b in BUILDS}
        add(test, test, None, "gc_pause_ms", "GC pause total over the run", "ms", LOWER, gc, informational=True)
    return metrics


def fmt(value, unit):
    if value is None:
        return "—"
    if unit == "bytes":
        return f"{value / 1048576:.2f} MiB"
    if unit == "KiB":
        return f"{value / 1024:.1f} MiB"
    if unit == "req/s":
        return f"{value:,.0f}"
    if unit == "ms":
        return f"{value:.2f} ms" if value < 100 else f"{value:.0f} ms"
    if unit == "us":
        return f"{value:.1f} µs"
    if unit == "s":
        return f"{value:.0f} s"
    return f"{value:g}"


def cell(m, build):
    v = m["median"].get(build)
    if v is None:
        return "—"
    text = fmt(v, m["unit"])
    n = len(m["samples"].get(build, []))
    if n > 1 and m["range"].get(build) and not m.get("exact"):
        lo, hi = min(m["samples"][build]), max(m["samples"][build])
        text += f" ({fmt(lo, m['unit'])}–{fmt(hi, m['unit'])})".replace(" ms–", "–").replace(" MiB–", "–").replace(" µs–", "–")
    return text


VERDICT_TEXT = {"better": "**csharpc better**", "worse": "csharpc worse", "tie": "tie", "no result": "no csharpc result",
                "n/a": "n/a", "met": "zero errors", "not met": "**errors**"}


def summarize(metrics):
    compared = [m for m in metrics if not m.get("requirement") and not m.get("informational") and m["verdict"] != "n/a"]
    count = lambda v: sum(1 for m in compared if m["verdict"] == v)
    reqs = [m for m in metrics if m.get("requirement")]
    return {"metrics": len(compared), "better": count("better"), "tie": count("tie"), "worse": count("worse"),
            "no_result": count("no result"), "zero_error_levels": sum(1 for m in reqs if m["verdict"] == "met"), "levels": len(reqs)}


def deploy_detail(measure):
    """Per build, medians over every start of the deploy test: start-up CPU time (ms), voluntary context switches at
    the first answer, and the idle RSS by mapping (MiB). Informational: what a start-up or idle RSS is made of."""
    if not measure:
        return {}
    detail = {}
    for b in BUILDS:
        rounds = [r for r in measure["rounds"] if r["build"] == b]
        cpu = [x for r in rounds for x in r.get("startup_cpu_ms", []) if x is not None]
        switches = [x for r in rounds for x in r.get("startup_context_switches", []) if x is not None]
        splits = [x for r in rounds for x in r.get("idle_rss_split_kib", [])]
        if not cpu and not splits:
            continue
        detail[b] = {"startup_cpu_ms": statistics.median(cpu) if cpu else None,
                     "startup_context_switches": statistics.median(switches) if switches else None,
                     "idle_rss_mib": {k: round(statistics.median(x[k] for x in splits) / 1024, 1) for k in splits[0]} if splits else {}}
    return detail


def report(args):
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    builds, measures = collect(args.inputs)
    failures, sections, apps = [], [], {}
    for app in [a for a in APPS if a in builds or any(k[0] == a for k in measures)]:
        built = builds.get(app)
        metrics = app_metrics(app, built, measures)
        summary = summarize(metrics)
        verification = {}
        for (a, test), m in measures.items():
            if a != app:
                continue
            for build, v in m.get("verification", {}).items():
                verification.setdefault(build, {})[test] = v
                if not v.get("transcript") or v.get("tfb") is False:
                    failures.append(f"{app}: {NAMES[build]} fails verification ({test})")
            for build, why in m.get("failures", {}).items():
                if not why.startswith("fails verification"):  # reported above
                    failures.append(f"{app} {test}: {NAMES[build]} doesn't run: {why}")
        # Answers must be identical to NativeAOT's and the JIT's (normalized transcripts).
        same = {}
        for (a, test), m in measures.items():
            if a != app:
                continue
            files = {b: Path(m["path"]) / f"{app}.{test}.{b}.transcript.txt" for b in BUILDS}
            for b, other in (("csharpc", "aot"), ("csharpc", "jit"), ("aot", "jit")):
                if files[b].exists() and files[other].exists():
                    same[(b, other, test)] = files[b].read_text() == files[other].read_text()
                    if not same[(b, other, test)] and b == "csharpc":  # NativeAOT vs JIT is only reported
                        failures.append(f"{app} {test}: {NAMES[b]} answers differently from {NAMES[other]}")
        for build, e in (built or {}).get("builds", {}).items():
            if not e["ok"] and not (build == "csharpc" and APPS[app]["csharpc_may_fail"]):
                failures.append(f"{app}: {NAMES[build]} {e['status']}")
            if e.get("image") and not e["image"]["ok"]:
                failures.append(f"{app}: the {NAMES[build]} image: {e['image']['status']}")
        apps[app] = {"title": APPS[app]["title"], "counted_for_m7": APPS[app]["counted"], "summary": summary,
                     "builds": (built or {}).get("builds", {}), "verification": verification,
                     "identical_answers": {f"{a} vs {b} ({t})": v for (a, b, t), v in same.items()}, "metrics": metrics,
                     "env": (built or {}).get("env") or next((m["env"] for (a, _), m in measures.items() if a == app), {}),
                     "duration": next((m["duration"] for (a, _), m in measures.items() if a == app), None),
                     "rounds": max((r["round"] for (a, _), m in measures.items() if a == app for r in m["rounds"]), default=0),
                     "deploy_detail": deploy_detail(measures.get((app, "deploy")))}
        sections.append(markdown_app(app, apps[app]))
    result = {"apps": apps, "failures": failures, "noise_rule": __doc__.split("report: ", 1)[1].split("\n\n")[0].replace("\n", " ")}
    (out / "results.json").write_text(json.dumps(result, indent=1))
    md = "\n\n".join(sections)
    if failures:
        md += "\n\n### Failures\n\n" + "\n".join(f"- {f}" for f in failures)
    (out / "results.md").write_text(md + "\n")
    print(md)
    if failures:
        print("\n".join(f"FAILED {f}" for f in failures), file=sys.stderr)
        sys.exit(1)


def markdown_app(app, data):
    s, env, builds = data["summary"], data["env"], data["builds"]
    lines = [f"## {data['title']}", ""]
    lines.append(f"{env.get('date', '')} · {env.get('machine', '')} · {env.get('cpus', '?')} CPUs ({env.get('cpu_model', '')}) · "
                 f".NET {env.get('dotnet', '')} · csharpc {env.get('commit', '')} · TechEmpower {TFB_COMMIT[:10]} · "
                 f"{data['rounds']} rounds, wrk {data['duration']} s per level")
    lines.append("")
    head = "M7 finish line" if data["counted_for_m7"] else "Interim app (not counted for M7)"
    lines.append(f"**{head}: {s['better']} of {s['metrics']} metrics beat NativeAOT** ({s['tie']} ties, {s['worse']} worse, "
                 f"{s['no_result']} without a csharpc result; csharpc error-free at {s['zero_error_levels']} of {s['levels']} load levels)")
    lines.append("")
    lines.append("Verdict: csharpc's median against NativeAOT's over the rounds; better or worse only when the difference exceeds both "
                 "builds' run-to-run range (max - min; 10th-90th percentile beyond 5 samples) and 1 % (exact metrics: any difference; single samples: 5 %).")
    lines.append("")
    lines += ["| | NativeAOT | csharpc | JIT |", "|---|---|---|---|"]
    status = lambda b: builds.get(b, {}).get("status", "not built")
    lines.append(f"| build | {status('aot')} | {status('csharpc')}"
                 f"{' — input: ' + builds['csharpc']['input'] if builds.get('csharpc', {}).get('input') else ''} | {status('jit')} |")
    proj = builds.get("csharpc", {}).get("project_input")
    if proj:
        why = "builds" if proj["ok"] else "does not compile: " + "; ".join(
            [i["item"] for i in (proj["blockers"] or {}).get("items", [])[:3]] + [o.split(": error ", 1)[-1][:120] for o in (proj["blockers"] or {}).get("other", [])[:2]])
        lines.append(f"| csharpc project input (`csharpc {APPS[app]['project']}`) | | {why} | |")
    v = data["verification"]

    def ver(b):
        if b not in v:
            return "—"
        return " ".join(f"{t} {'✓' if r.get('transcript') and r.get('tfb', True) else '✗'}" for t, r in sorted(v[b].items()))
    lines.append(f"| verification (TechEmpower's rules + transcript) | {ver('aot')} | {ver('csharpc')} | {ver('jit')} |")
    lines.append("")
    groups = ["deployment"] + [t for t in APPS[app]["tests"]]
    lines += ["| group | metrics | csharpc better | tie | worse | no result |", "|---|---:|---:|---:|---:|---:|"]
    for g in groups:
        gm = [m for m in data["metrics"] if m["group"] == g]
        if not gm:
            continue
        gs = summarize(gm)
        title = "Deployment" if g == "deployment" else TESTS[g]["title"]
        lines.append(f"| {title} | {gs['metrics']} | {gs['better']} | {gs['tie']} | {gs['worse']} | {gs['no_result']} |")
    lines.append("")
    dep = [m for m in data["metrics"] if m["group"] == "deployment"]
    lines += ["### Deployment", "", "| metric | NativeAOT | csharpc | JIT | verdict |", "|---|---:|---:|---:|---|"]
    for m in dep:
        lines.append(f"| {m['label']} | {cell(m, 'aot')} | {cell(m, 'csharpc')} | {cell(m, 'jit')} | {VERDICT_TEXT[m['verdict']]} |")
    detail = data.get("deploy_detail") or {}
    if detail:
        def row(label, f):
            return f"| {label} | " + " | ".join(f(detail[b]) if b in detail else "—" for b in ("aot", "csharpc", "jit")) + " | not judged |"
        lines.append(row("start-up CPU time (all threads)", lambda d: f"{d['startup_cpu_ms']:.0f} ms" if d["startup_cpu_ms"] is not None else "—"))
        lines.append(row("voluntary context switches to the first response",
                         lambda d: f"{d['startup_context_switches']:,.0f}" if d["startup_context_switches"] is not None else "—"))
        lines.append(row("idle RSS: anonymous / executable / libraries",
                         lambda d: f"{d['idle_rss_mib'].get('anon', 0)} / {d['idle_rss_mib'].get('binary', 0)} / {d['idle_rss_mib'].get('libraries', 0)} MiB"))
    for t in APPS[app]["tests"]:
        tm = [m for m in data["metrics"] if m["group"] == t]
        if not tm:
            continue
        spec = TESTS[t]
        lines += ["", f"### {spec['title']} ({spec['url']})", ""]
        lines += [f"| {spec['level']} | NativeAOT req/s | csharpc req/s | JIT req/s | csharpc / NativeAOT | verdict | csharpc p99 vs NativeAOT | errors |",
                  "|---:|---:|---:|---:|---:|---|---|---|"]
        for level in spec["levels"]:
            get = lambda key: next((m for m in tm if m["level"] == level and m["metric"] == key), None)
            rps, p99, err = get("rps"), get("lat_p99"), get("errors")
            ratio = f"{rps['median']['csharpc'] / rps['median']['aot']:.2f}" if rps and "csharpc" in rps["median"] and rps["median"].get("aot") else "—"
            p99_text = "—" if not p99 or p99["verdict"] == "n/a" else f"{fmt(p99['median'].get('csharpc'), 'ms')} vs {fmt(p99['median'].get('aot'), 'ms')}"
            lines.append(f"| {level} | {cell(rps, 'aot')} | {cell(rps, 'csharpc')} | {cell(rps, 'jit')} | {ratio} | {VERDICT_TEXT[rps['verdict']]} | "
                         f"{p99_text} | {VERDICT_TEXT[err['verdict']]} |")
        lines += ["", "<details><summary>every metric</summary>", "", f"| {spec['level']} | metric | NativeAOT | csharpc | JIT | verdict |",
                  "|---:|---|---:|---:|---:|---|"]
        for m in tm:
            if m["verdict"] == "n/a" and not m["samples"]:
                continue
            lines.append(f"| {m['level'] if m['level'] is not None else 'run'} | {m['label']} | {cell(m, 'aot')} | {cell(m, 'csharpc')} | "
                         f"{cell(m, 'jit')} | {'(informational)' if m.get('informational') else VERDICT_TEXT[m['verdict']]} |")
        lines += ["", "</details>"]
    blockers = builds.get("csharpc", {}).get("blockers")
    if blockers:
        sys.path.insert(0, str(HERE))
        import blockers as b
        lines += ["", "### What stops the csharpc build", "", b.markdown(blockers)]
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("build")
    p.add_argument("--app", default="platform", choices=APPS)
    p.add_argument("--out", required=True)
    p.add_argument("--only", default="aot,csharpc,jit")
    p.add_argument("--images", action="store_true")
    p.add_argument("--profile", action="store_true")
    p = sub.add_parser("measure")
    p.add_argument("--app", default="platform", choices=APPS)
    p.add_argument("--builds", required=True)
    p.add_argument("--test", required=True, choices=list(TESTS) + ["deploy"])
    p.add_argument("--out", required=True)
    p.add_argument("--rounds", type=int, default=3)
    p.add_argument("--jit-rounds", type=int, default=1)
    p.add_argument("--no-load", action="store_true")
    p.add_argument("--no-tfb-verify", action="store_true", help="skip TechEmpower's verification (needs siege, requests, psycopg2)")
    p.add_argument("--profile", action="store_true")
    p = sub.add_parser("report")
    p.add_argument("--out", required=True)
    p.add_argument("inputs", nargs="+")
    args = ap.parse_args()
    {"build": build, "measure": measure, "report": report}[args.cmd](args)


if __name__ == "__main__":
    main()
