using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Csharpc.Frontend;

namespace Csharpc.Driver;

/// <summary>An evaluated .csproj: what the SDK would hand to csc, gathered for csharpc.</summary>
public sealed record ProjectInfo
{
    public required string ProjectPath { get; init; }
    public required string AssemblyName { get; init; }
    public required string OutputType { get; init; }
    public List<string> Files { get; } = [];
    public List<string> Defines { get; } = [];
    public string? LanguageVersion { get; init; }
    public string? Nullable { get; init; }
    public bool AllowUnsafe { get; init; }
    public bool ImplicitUsings { get; init; }
    public List<string> GlobalUsings { get; } = [];
    public List<string> ProjectReferences { get; } = [];
    public List<string> UnsupportedPackages { get; } = [];
    public string ProjectDirectory => Path.GetDirectoryName(ProjectPath)!;
}

/// <summary>
/// Evaluates projects with MSBuild itself (`dotnet msbuild -getProperty/-getItem`, .NET 8+), so
/// globs, Directory.Build.props, conditions, implicit usings and SDK defaults are exactly what
/// `dotnet build` sees. ProjectReferences are evaluated recursively and their sources are folded
/// into the same closed-world compilation (whole-program, like NativeAOT).
/// </summary>
public static class ProjectLoader
{
    private static readonly string[] Properties =
    [
        "AssemblyName", "OutputType", "DefineConstants", "LangVersion", "Nullable", "AllowUnsafeBlocks",
        "ImplicitUsings", "TargetFramework", "TargetFrameworks", "MSBuildProjectFullPath",
    ];

    private static readonly string[] Items = ["Compile", "Using", "ProjectReference", "PackageReference"];

    /// <summary>Finds the single project file in a directory (like `dotnet build` with no arguments).</summary>
    public static string? FindProject(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj");
        return projects.Length switch
        {
            0 => null,
            1 => projects[0],
            _ => throw new ArgumentException($"multiple project files in '{directory}'; specify which one to compile"),
        };
    }

    public static ProjectInfo Evaluate(string projectPath, string configuration, IReadOnlyDictionary<string, string>? globalProperties = null)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath)) throw new FileNotFoundException($"project file '{projectPath}' not found");

        var args = new List<string> { "msbuild", projectPath, "-nologo", $"-p:Configuration={configuration}" };
        foreach (var (k, v) in globalProperties ?? new Dictionary<string, string>()) args.Add($"-p:{k}={v}");
        args.AddRange(Properties.Select(p => $"-getProperty:{p}"));
        args.AddRange(Items.Select(i => $"-getItem:{i}"));
        var json = RunDotnet(args, Path.GetDirectoryName(projectPath)!);

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("Properties");
        var items = doc.RootElement.GetProperty("Items");
        string? Prop(string name) => props.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s ? s : null;
        IEnumerable<JsonElement> Item(string name) =>
            items.TryGetProperty(name, out var arr) ? arr.EnumerateArray() : [];
        static string? Meta(JsonElement item, string name) =>
            item.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s ? s : null;

        if (Prop("TargetFramework") is null && Prop("TargetFrameworks") is { } tfms)
            throw new ArgumentException($"multi-targeted project ({tfms}); pass -p:TargetFramework=<tfm>");

        var info = new ProjectInfo
        {
            ProjectPath = projectPath,
            AssemblyName = Prop("AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath),
            OutputType = Prop("OutputType") ?? "Library",
            LanguageVersion = Prop("LangVersion"),
            Nullable = Prop("Nullable"),
            AllowUnsafe = string.Equals(Prop("AllowUnsafeBlocks"), "true", StringComparison.OrdinalIgnoreCase),
            ImplicitUsings = Prop("ImplicitUsings") is "enable" or "true",
        };

        info.Defines.AddRange((Prop("DefineConstants") ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var c in Item("Compile"))
            if (Meta(c, "FullPath") is { } path && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                info.Files.Add(path);

        // <Using Include="X" [Alias="Y"] [Static="true"] /> -> global using lines (what the SDK's
        // GenerateGlobalUsings target writes to obj/.../X.GlobalUsings.g.cs during a build).
        foreach (var u in Item("Using"))
        {
            var ns = Meta(u, "Identity")!;
            if (Meta(u, "Alias") is { } alias) info.GlobalUsings.Add($"global using {alias} = global::{ns};");
            else if (string.Equals(Meta(u, "Static"), "true", StringComparison.OrdinalIgnoreCase)) info.GlobalUsings.Add($"global using static global::{ns};");
            else info.GlobalUsings.Add($"global using global::{ns};");
        }

        foreach (var r in Item("ProjectReference"))
            if (Meta(r, "FullPath") is { } path) info.ProjectReferences.Add(path);

        foreach (var p in Item("PackageReference"))
            if (!string.Equals(Meta(p, "IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase))
                info.UnsupportedPackages.Add(Meta(p, "Identity") + (Meta(p, "Version") is { } v ? " " + v : ""));

        return info;
    }

    /// <summary>
    /// Evaluates the root project and every transitively referenced project, and merges them into
    /// one whole-program compilation input. Settings (defines, language version, nullable) come from
    /// the root; global usings are the union (they're compilation-wide in a single compilation).
    /// </summary>
    public static (FrontendSettings Settings, ProjectInfo Root, IReadOnlyList<ProjectInfo> All) Load(
        string projectPath, string configuration, TextWriter? log = null, IReadOnlyDictionary<string, string>? globalProperties = null)
    {
        var root = Evaluate(projectPath, configuration, globalProperties);
        var all = new List<ProjectInfo> { root };
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.ProjectPath };
        var queue = new Queue<string>(root.ProjectReferences);
        while (queue.TryDequeue(out var reference))
        {
            if (!seen.Add(Path.GetFullPath(reference))) continue;
            log?.WriteLine($"  project reference: {reference}");
            var p = Evaluate(reference, configuration, globalProperties);
            all.Add(p);
            foreach (var next in p.ProjectReferences) queue.Enqueue(next);
        }

        var files = all.SelectMany(p => p.Files).Distinct(StringComparer.Ordinal).ToList();
        var usings = all.SelectMany(p => p.GlobalUsings).Distinct(StringComparer.Ordinal).ToList();
        var settings = new FrontendSettings
        {
            Files = files,
            Defines = root.Defines,
            LanguageVersion = root.LanguageVersion,
            Nullable = root.Nullable,
            AllowUnsafe = all.Any(p => p.AllowUnsafe),
            GlobalUsings = usings.Count > 0 ? string.Join('\n', usings) : null,
            AssemblyName = root.AssemblyName,
        };
        return (settings, root, all);
    }

    private static string RunDotnet(List<string> args, string workingDirectory)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";
        var psi = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // When invoked from inside an MSBuild build (our targets), don't inherit the parent's node
        // settings — run a fresh, independent evaluation.
        foreach (var key in new[] { "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly" })
            psi.Environment.Remove(key);

        Process process;
        try { process = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException($"could not run '{dotnet}' to evaluate the project ({e.Message})", e);
        }
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"project evaluation failed (dotnet msbuild exit {process.ExitCode}):\n{stdout}{stderr.Result}");
        var start = stdout.IndexOf('{');
        return start >= 0 ? stdout[start..] : throw new InvalidOperationException($"unexpected msbuild output:\n{stdout}");
    }
}
