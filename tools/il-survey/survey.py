#!/usr/bin/env python3
"""IL front-end survey (docs/18-compatibility.md#il-survey): what nuget.org packages need from csharpc.

For each package in packages.txt (id, version), the harness
  1. downloads the .nupkg and its dependency closure from nuget.org's flat container into a fresh
     temporary directory and extracts only lib/<tfm>/*.dll and the .nuspec — no MSBuild, so no
     package build scripts or targets ever run;
  2. picks the assemblies NuGet would pick for net11.0 (nearest compatible lib/ folder; packages
     whose assembly ships in the shared framework are taken from the framework);
  3. compiles a trivial consumer with `csharpc Program.cs -r <dlls> --il-survey -O0 --emit llvm-bc`:
     --il-survey lowers every method of the package that needs no type arguments, reachable or not,
     so a single compile reports everything the package would need;
  4. ranks the diagnostics: library members csharpc has no implementation for (CSLLVM0002) and IL or
     language constructs it does not support yet (CSLLVM0001), by number of packages affected.

usage: tools/il-survey/survey.py [--packages FILE] [--csharpc DLL] [--jobs N] [--timeout SEC]
                                 [--work DIR] [--out REPORT.md] [--only ID[,ID...]]
"""
import argparse
import concurrent.futures
import io
import json
import os
import re
import resource
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
import xml.etree.ElementTree as ET
import threading
import zipfile
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FLAT = "https://api.nuget.org/v3-flatcontainer"

# Target frameworks compatible with net11.0, nearest first (NuGet's precedence for a net11.0 app).
COMPATIBLE = (
    [f"net{v}.0" for v in range(11, 4, -1)]
    + ["netcoreapp3.1", "netcoreapp3.0", "netcoreapp2.2", "netcoreapp2.1", "netcoreapp2.0"]
    + ["netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.5", "netstandard1.4",
       "netstandard1.3", "netstandard1.2", "netstandard1.1", "netstandard1.0"]
)


def normalize_tfm(name):
    """'.NETStandard2.0' / 'netstandard2.0' / 'net8.0-windows' -> 'netstandard2.0' / 'net8.0' / None (platform-specific)."""
    n = name.lower()
    if "-" in n:
        return None  # net8.0-windows etc.: not for a portable net11.0 app
    n = n.replace(".netstandard", "netstandard").replace(".netcoreapp", "netcoreapp")
    m = re.fullmatch(r"(netstandard|netcoreapp|net)(\d+)\.?(\d+)?", n)
    if not m:
        return n
    kind, major, minor = m.group(1), m.group(2), m.group(3) or "0"
    if kind == "net" and len(major) > 1 and minor == "0" and "." not in name:  # net462 -> .NET Framework
        return "netfx" + major
    return f"{kind}{major}.{minor}"


def best_tfm(available):
    by_norm = {normalize_tfm(t): t for t in available if normalize_tfm(t)}
    for tfm in COMPATIBLE:
        if tfm in by_norm:
            return by_norm[tfm]
    return None


def fetch(url, retries=3):
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(url, timeout=120) as r:
                return r.read()
        except Exception:
            if attempt == retries - 1:
                raise
            time.sleep(2 * (attempt + 1))


def lower_bound(version_range):
    """'[1.2.3, )' / '1.2.3' / '(, 2.0]' -> '1.2.3' (NuGet picks the lowest applicable version)."""
    v = version_range.strip()
    if v and v[0] in "[(":
        v = v[1:-1].split(",")[0].strip()
    return v or None


class Package:
    def __init__(self, pid, version, work):
        self.id, self.version = pid, version
        self.dir = os.path.join(work, "pkgs", f"{pid.lower()}.{version.lower()}")
        self.dlls, self.dependencies, self.tfm = [], [], None

    def download(self):
        if not os.path.isdir(self.dir):
            data = fetch(f"{FLAT}/{self.id.lower()}/{self.version.lower()}/{self.id.lower()}.{self.version.lower()}.nupkg")
            tmp = self.dir + ".part"
            os.makedirs(tmp, exist_ok=True)
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                for name in z.namelist():
                    # Only metadata and managed libraries; nothing that could run (tools/, build/, content/).
                    if name.endswith(".nuspec") and "/" not in name or (name.startswith("lib/") and name.endswith(".dll")):
                        target = os.path.normpath(os.path.join(tmp, name))
                        if not target.startswith(os.path.abspath(tmp) + os.sep):
                            continue  # path traversal in a crafted archive
                        os.makedirs(os.path.dirname(target), exist_ok=True)
                        with z.open(name) as src, open(target, "wb") as dst:
                            shutil.copyfileobj(src, dst)
            os.rename(tmp, self.dir)
        lib = os.path.join(self.dir, "lib")
        if os.path.isdir(lib):
            self.tfm = best_tfm(os.listdir(lib))
            if self.tfm:
                folder = os.path.join(lib, self.tfm)
                self.dlls = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".dll"))
        nuspec = next((f for f in os.listdir(self.dir) if f.endswith(".nuspec")), None)
        if nuspec:
            self.dependencies = self.read_dependencies(os.path.join(self.dir, nuspec))

    def read_dependencies(self, path):
        root = ET.parse(path).getroot()
        ns = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
        q = (lambda tag: "n:" + tag) if ns else (lambda tag: tag)
        deps = root.find(f"{q('metadata')}/{q('dependencies')}", ns)
        if deps is None:
            return []
        groups = deps.findall(q("group"), ns)
        if not groups:
            chosen = deps
        else:
            by_tfm = {g.get("targetFramework") or "": g for g in groups}
            name = best_tfm([t for t in by_tfm if t])
            chosen = by_tfm.get(name) if name else by_tfm.get("")
            if chosen is None:
                return []
        result = []
        for d in chosen.findall(q("dependency"), ns):
            exclude = (d.get("exclude") or "").lower()
            if "compile" in exclude and "runtime" in exclude:
                continue
            v = lower_bound(d.get("version") or "")
            if d.get("id") and v:
                result.append((d.get("id"), v))
        return result


def framework_dir():
    dotnet = shutil.which("dotnet")
    roots = [os.path.join(os.path.dirname(os.path.realpath(dotnet)), "shared", "Microsoft.NETCore.App")] if dotnet else []
    roots.append(os.path.expanduser("~/.dotnet11/shared/Microsoft.NETCore.App"))
    for r in roots:
        if os.path.isdir(r):
            versions = sorted(os.listdir(r))
            if versions:
                return os.path.join(r, versions[-1])
    sys.exit("survey: shared framework (Microsoft.NETCore.App) not found")


def resolve(pid, version, work, framework, cache):
    """The package's dependency closure: {id: Package}; framework-provided packages are skipped."""
    closure, queue = {}, [(pid, version)]
    while queue:
        i, v = queue.pop()
        if os.path.exists(os.path.join(framework, i + ".dll")) or i.lower().startswith(("microsoft.netcore.", "netstandard.library")):
            continue  # in the shared framework: binds to the framework's assembly
        key = i.lower()
        if key in closure and closure[key].version >= v:
            continue
        p = cache.get((key, v))
        if p is None:
            p = Package(i, v, work)
            p.download()
            cache[(key, v)] = p
        closure[key] = p
        queue.extend(p.dependencies)
    return closure


DIAG = re.compile(r"(error|warning) (CSLLVM\d{4}): (.*)$")


def classify(message):
    """(kind, key) for a diagnostic message (CSLLVM0007's 'unreachable stub' suffix removed)."""
    message = message.split(" — the program does not reach")[0]
    site = re.search(r" \[in '(.*?)' \((.*?)\)", message)
    text = message[: site.start()] if site else message
    m = re.match(r"'(.+)' has no implementation in the csharpc runtime yet", text)
    if m:
        return "member", m.group(1)
    m = re.match(r"not supported by csharpc yet: (.*)", text)
    if m:
        detail = m.group(1)
        m2 = re.match(r"'(.+)' has no implementation in the csharpc runtime yet", detail)
        if m2:
            return "member", m2.group(1)
        # Instructions: keep the opcode, drop operands.
        detail = re.sub(r"(IL_[0-9A-F]{4}: \S+).*", r"\1", detail)
        return "construct", detail
    if text.startswith("internal compiler error"):
        return "ice", re.sub(r"'[^']*'", "'…'", text)[:160]
    return "other", text[:160]


RESOLVE_LOCK = threading.Lock()  # downloads share the package cache


def survey_one(pkg_id, version, args, framework, cache):
    started = time.time()
    result = {"id": pkg_id, "version": version, "status": "", "seconds": 0, "assemblies": [], "diagnostics": [], "log": ""}
    try:
        with RESOLVE_LOCK:
            closure = resolve(pkg_id, version, args.work, framework, cache)
    except Exception as e:  # download or nuspec problems
        result["status"] = f"download failed: {e}"
        return result
    root = closure.get(pkg_id.lower())
    if root is None or not root.dlls:
        result["status"] = "no lib/ assembly for net11.0"
        return result
    dlls = [d for p in closure.values() for d in p.dlls]
    result["assemblies"] = [os.path.basename(d) for d in root.dlls]
    result["dependencies"] = sorted(f"{p.id} {p.version}" for k, p in closure.items() if k != pkg_id.lower())
    proj = os.path.join(args.work, "consumers", pkg_id)
    os.makedirs(proj, exist_ok=True)
    with open(os.path.join(proj, "Program.cs"), "w") as f:
        f.write('System.Console.WriteLine("survey");\n')
    cmd = ["dotnet", args.csharpc, os.path.join(proj, "Program.cs"), "--il-survey", "-O0", "--emit", "llvm-bc",
           "-o", os.path.join(proj, "out.bc")]
    for d in dlls:
        cmd += ["-r", d]
    try:
        # A managed heap limit turns a runaway compile into an OutOfMemoryException instead of
        # exhausting the machine (CI runners kill the whole job then).
        # The address-space limit catches native (LLVM) growth too; .NET only reserves address space
        # in proportion to the heap limit, so 3x leaves room for code, mappings and LLVM.
        env = dict(os.environ, DOTNET_GCHeapHardLimit=hex(args.memory_gb << 30))
        limit = 3 * (args.memory_gb << 30)

        def cap():
            resource.setrlimit(resource.RLIMIT_AS, (limit, limit))

        p = subprocess.run(cmd, capture_output=True, text=True, timeout=args.timeout, cwd=proj, env=env, preexec_fn=cap)
        log = p.stdout + p.stderr
        errors = sum(1 for line in log.splitlines() if re.search(r"error (CSLLVM|CS)\d", line))
        result["status"] = "ok" if p.returncode == 0 else ("errors" if errors else f"failed (exit {p.returncode})")
    except subprocess.TimeoutExpired as e:
        log = (e.stdout or b"").decode(errors="replace") + (e.stderr or b"").decode(errors="replace") if isinstance(e.stdout, bytes) else (e.stdout or "") + (e.stderr or "")
        result["status"] = f"timeout ({args.timeout}s)"
    result["log"] = log[-4000:]
    for line in log.splitlines():
        m = DIAG.search(line)
        if m:
            kind, key = classify(m.group(3))
            result["diagnostics"].append({"severity": m.group(1), "id": m.group(2), "kind": kind, "key": key})
    result["seconds"] = round(time.time() - started, 1)
    return result


def report(results, out):
    members, constructs, ices = defaultdict(set), defaultdict(set), defaultdict(set)
    sites = Counter()
    for r in results:
        for d in r["diagnostics"]:
            table = {"member": members, "construct": constructs, "ice": ices}.get(d["kind"], constructs)
            table[d["key"]].add(r["id"])
            sites[(d["kind"], d["key"])] += 1
    lines = ["| Package | Version | Result | Diagnostics | Missing members | Time (s) |", "|---|---|---|---|---|---|"]
    for r in sorted(results, key=lambda r: r["id"].lower()):
        missing = len({d["key"] for d in r["diagnostics"] if d["kind"] == "member"})
        lines.append(f"| {r['id']} | {r['version']} | {r['status']} | {len(r['diagnostics'])} | {missing} | {r['seconds']} |")

    def ranking(title, table, kind, limit):
        rows = sorted(table.items(), key=lambda kv: (-len(kv[1]), -sites[(kind, kv[0])], kv[0]))[:limit]
        if not rows:
            return []
        out = [f"### {title}", "", "| # | Packages | Sites | Item |", "|---|---|---|---|"]
        for n, (key, pkgs) in enumerate(rows, 1):
            out.append(f"| {n} | {len(pkgs)} | {sites[(kind, key)]} | `{key.replace('|', '¦')}` |")
        return out + [""]

    text = "\n".join(
        ["## Packages", ""] + lines + [""]
        + ranking("Missing library members", members, "member", 60)
        + ranking("Unsupported constructs", constructs, "construct", 40)
        + ranking("Internal compiler errors", ices, "ice", 20))
    with open(out, "w") as f:
        f.write(text + "\n")
    return text


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--packages", default=os.path.join(ROOT, "tools", "il-survey", "packages.txt"))
    ap.add_argument("--csharpc", default=os.path.join(ROOT, "src", "Csharpc", "bin", "Release", "net11.0", "csharpc.dll"))
    ap.add_argument("--jobs", type=int, default=2)
    ap.add_argument("--timeout", type=int, default=1200)
    ap.add_argument("--work", default=None, help="download/compile directory (default: a fresh one under /tmp)")
    ap.add_argument("--out", default="il-survey.md")
    ap.add_argument("--only", default=None)
    ap.add_argument("--memory-gb", type=int, default=4, help="managed heap limit per compile (DOTNET_GCHeapHardLimit); address space: 3x")
    args = ap.parse_args()
    args.work = args.work or tempfile.mkdtemp(prefix="il-survey-")
    if not os.path.exists(args.csharpc):
        sys.exit(f"survey: {args.csharpc} not found (dotnet build src/Csharpc -c Release)")
    packages = []
    with open(args.packages) as f:
        for line in f:
            line = line.split("#")[0].strip()
            if line:
                pid, version = line.split()[:2]
                packages.append((pid, version))
    if args.only:
        wanted = {x.lower() for x in args.only.split(",")}
        packages = [p for p in packages if p[0].lower() in wanted]
    framework = framework_dir()
    print(f"survey: {len(packages)} packages, work dir {args.work}, framework {framework}", flush=True)
    cache, results = {}, []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {pool.submit(survey_one, pid, v, args, framework, cache): pid for pid, v in packages}
        for fut in concurrent.futures.as_completed(futures):
            r = fut.result()
            results.append(r)
            print(f"  {r['id']:45} {r['status']:24} {len(r['diagnostics']):5} diagnostics  {r['seconds']:7}s", flush=True)
            # Written after every package: a partial survey still leaves a report.
            with open(os.path.join(args.work, "results.json"), "w") as f:
                json.dump(results, f, indent=1)
            report(results, args.out)
    print(f"survey: report {args.out}, raw results {os.path.join(args.work, 'results.json')}")


if __name__ == "__main__":
    main()
