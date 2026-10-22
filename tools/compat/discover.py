#!/usr/bin/env python3
"""Select the compatibility corpus from the fetched repositories (run with `python3 -I`).

Walks the extracted trees (see fetch.sh), statically filters project files and writes
programs.tsv: one line per candidate console program with its source URL and commit, plus
excluded.tsv with the reason every other project was left out. Only reads files; never executes
anything from the corpus.

    python3 -I tools/compat/discover.py CORPUS_DIR OUT_DIR [TREE_DIR TFM]

With TREE_DIR, also writes a copy of the corpus there with every project retargeted to TFM (the
only installed runtime is .NET 11; a global -p:TargetFramework breaks restore).
"""
import os
import re
import shutil
import sys

REPOS = {  # directory name in CORPUS_DIR -> (owner/repo, roots searched)
    "samples": ("dotnet/samples", ["core/console-apps", "core/linq", "core/tutorials", "csharp"]),
    "docs": ("dotnet/docs", ["docs/csharp", "docs/standard", "docs/fundamentals", "docs/core"]),
}

# Programs that need something a deterministic, offline, headless comparison can't provide.
NETWORK = re.compile(r"\b(HttpClient|WebClient|WebRequest|TcpClient|TcpListener|UdpClient|Socket\b|"
                     r"Dns\.|NetworkStream|SmtpClient|HttpListener|ClientWebSocket|Ping\b)")
GUI_OR_WINDOWS = re.compile(r"\b(System\.Windows\.Forms|System\.Windows\.Controls|Microsoft\.Win32\.Registry|"
                            r"Registry\.|WindowsIdentity|EventLog\b|PerformanceCounter|System\.Drawing\b|"
                            r"Microsoft\.Office|Excel\.|Word\.Application|\bdynamic\b.*COM)")
SERVICE = re.compile(r"\b(Microsoft\.Extensions\.Hosting|IHost\b|WebApplication\b|Microsoft\.AspNetCore)")


def read(path):
    try:
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def sources_of(proj_dir):
    """The .cs files the SDK's default glob would compile (ignoring bin/obj and nested projects)."""
    out = []
    for dirpath, dirnames, filenames in os.walk(proj_dir):
        dirnames[:] = sorted(d for d in dirnames if d.lower() not in ("bin", "obj"))
        if dirpath != proj_dir and any(f.endswith(".csproj") for f in filenames):
            dirnames[:] = []  # a nested project owns this subtree
            continue
        out += [os.path.join(dirpath, f) for f in sorted(filenames) if f.endswith(".cs")]
    return out


def classify(csproj, text, src):
    sdk = re.search(r'<Project\s+Sdk="([^"]+)"', text)
    if not sdk:
        return "not an SDK-style project"
    if sdk.group(1) != "Microsoft.NET.Sdk":
        return f"SDK {sdk.group(1)}"
    if not re.search(r"<OutputType>\s*(Exe|WinExe)\s*</OutputType>", text, re.I):
        return "not an executable"
    if re.search(r"<OutputType>\s*WinExe", text, re.I) or re.search(r"<Use(WPF|WindowsForms|Maui)>\s*true", text, re.I):
        return "GUI"
    tfm = re.search(r"<TargetFrameworks?>([^<]+)</TargetFrameworks?>", text)
    if tfm and not re.search(r"net(\d{2}|[5-9]\.|[5-9]\b|coreapp)|\$\(", tfm.group(1)):
        return f"target framework {tfm.group(1).strip()}"
    if re.search(r"-windows", tfm.group(1) if tfm else ""):
        return "Windows-only target framework"
    if "<PackageReference" in text:
        return "NuGet package reference"
    if re.search(r"<(COMReference|Reference Include)", text):
        return "assembly/COM reference"
    if not src:
        return "no sources"
    code = "\n".join(read(s) for s in src)
    if NETWORK.search(code):
        return "network"
    if GUI_OR_WINDOWS.search(code):
        return "GUI/Windows API"
    if SERVICE.search(code):
        return "hosting/ASP.NET"
    if re.search(r"\b(Process\.Start|AppDomain\.CreateDomain|Assembly\.Load(From|File)?\()", code):
        return "starts processes/loads assemblies"
    return None


def retarget(corpus, tree, tfm):
    shutil.rmtree(tree, ignore_errors=True)
    for name in REPOS:
        shutil.copytree(os.path.join(corpus, name), os.path.join(tree, name), symlinks=True,
                        ignore=shutil.ignore_patterns("bin", "obj"))
    for dirpath, _, filenames in os.walk(tree):
        for f in filenames:
            if f.endswith(".csproj") or f in ("Directory.Build.props", "Directory.Build.targets"):
                p = os.path.join(dirpath, f)
                s = read(p)
                t = re.sub(r"<TargetFrameworks?>[^<]*</TargetFrameworks?>", f"<TargetFramework>{tfm}</TargetFramework>", s)
                if t != s:
                    with open(p, "w", encoding="utf-8") as out:
                        out.write(t)


def main():
    corpus, out_dir = sys.argv[1], sys.argv[2]
    if len(sys.argv) > 4:
        retarget(corpus, sys.argv[3], sys.argv[4])
    rows, excluded = [], []
    for name, (repo, roots) in REPOS.items():
        base = os.path.join(corpus, name)
        sha = read(os.path.join(base, ".sha")).strip()
        for root in roots:
            for dirpath, dirnames, filenames in os.walk(os.path.join(base, root)):
                dirnames.sort()
                for f in sorted(filenames):
                    if not f.endswith(".csproj"):
                        continue
                    csproj = os.path.join(dirpath, f)
                    rel = os.path.relpath(csproj, base)
                    reason = classify(csproj, read(csproj), sources_of(dirpath))
                    url = f"https://github.com/{repo}/tree/{sha}/{os.path.dirname(rel)}"
                    if reason:
                        excluded.append((repo, rel, reason))
                    else:
                        rows.append((repo, sha, rel, url))
    # Stable ids derived from the path keep logs and reports comparable across runs.
    seen = set()
    with open(os.path.join(out_dir, "programs.tsv"), "w") as f:
        f.write("# id\trepo\tcommit\tproject\turl\n")
        for repo, sha, rel, url in rows:
            pid = re.sub(r"[^A-Za-z0-9]+", "-", f"{repo.split('/')[1]}/{rel[:-len('.csproj')]}").strip("-")
            pid = re.sub(r"-(snippets|csharp|docs)-", "-", pid)
            while pid in seen:
                pid += "_"
            seen.add(pid)
            f.write(f"{pid}\t{repo}\t{sha}\t{rel}\t{url}\n")
    with open(os.path.join(out_dir, "excluded.tsv"), "w") as f:
        f.write("# repo\tproject\treason\n")
        for e in excluded:
            f.write("\t".join(e) + "\n")
    counts = {}
    for e in excluded:
        counts[e[2]] = counts.get(e[2], 0) + 1
    print(f"{len(rows)} candidates, {len(excluded)} excluded")
    for k, v in sorted(counts.items(), key=lambda kv: -kv[1])[:25]:
        print(f"  {v:5d} {k}")


if __name__ == "__main__":
    main()
