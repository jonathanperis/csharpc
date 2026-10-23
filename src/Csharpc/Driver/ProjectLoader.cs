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
    /// <summary>&lt;StartupObject&gt;: the type whose Main is the entry point (csc -main).</summary>
    public string? StartupObject { get; init; }
    /// <summary>&lt;InvariantGlobalization&gt; / &lt;PredefinedCulturesOnly&gt;: the runtimeconfig switches
    /// System.Globalization.Invariant / PredefinedCulturesOnly, compiled into the program (null: not set).</summary>
    public bool? InvariantGlobalization { get; init; }
    public bool? PredefinedCulturesOnly { get; init; }
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
        "StartupObject", "InvariantGlobalization", "PredefinedCulturesOnly",
    ];

    /// <summary>
    /// The SDK adds the target-framework symbols (NET, NET11_0, NETCOREAPP, NET5_0_OR_GREATER ...
    /// NETCOREAPP3_1_OR_GREATER) to DefineConstants in this target, not during evaluation, so the
    /// project evaluation runs it: the values reported then are exactly what csc receives.
    /// </summary>
    private const string DefineConstantsTarget = "AddImplicitDefineConstants";

    /// <summary>
    /// What `DefineConstants` holds for a net11.0 C# build when there is no project file (single
    /// files): the C# SDK's configuration symbols plus the target's framework symbols.
    /// </summary>
    public static IEnumerable<string> ImplicitDefines(string configuration)
    {
        yield return "TRACE";
        if (configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase)) yield return "DEBUG";
        else if (configuration.Equals("Release", StringComparison.OrdinalIgnoreCase)) yield return "RELEASE";
        yield return "NET";
        yield return "NET11_0";
        yield return "NETCOREAPP";
        for (var v = 5; v <= 11; v++) yield return $"NET{v}_0_OR_GREATER";
        foreach (var v in new[] { "1_0", "1_1", "2_0", "2_1", "2_2", "3_0", "3_1" }) yield return $"NETCOREAPP{v}_OR_GREATER";
    }

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

    /// <summary>An MSBuild boolean property as the runtimeconfig would get it ("true"/"false", any case).</summary>
    private static bool? Switch(string? value) =>
        value is null ? null : string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase) ? false : null;

    /// <summary>
    /// The corelib symbols for the globalization switches (Globalization/GlobalizationMode.cs): csharpc has no
    /// runtimeconfig.json, so the project's values are compiled in; the DOTNET_SYSTEM_GLOBALIZATION_*
    /// environment variables still override them at run time, as in .NET.
    /// </summary>
    public static IEnumerable<string> GlobalizationDefines(bool? invariant, bool? predefinedCulturesOnly)
    {
        if (invariant == true) yield return "CSHARPC_INVARIANT_GLOBALIZATION";
        if (predefinedCulturesOnly == true) yield return "CSHARPC_PREDEFINED_CULTURES_ONLY";
        if (predefinedCulturesOnly == false) yield return "CSHARPC_PREDEFINED_CULTURES_ONLY_FALSE";
    }

    /// <summary>
    /// File-based apps (`dotnet run app.cs`): `#:property InvariantGlobalization=true` and
    /// `#:property PredefinedCulturesOnly=...` directives at the top of an input file.
    /// </summary>
    public static (bool? Invariant, bool? PredefinedCulturesOnly) FileGlobalizationProperties(IEnumerable<string> files)
    {
        bool? invariant = null, predefined = null;
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#!", StringComparison.Ordinal)) continue;
                if (!line.StartsWith("#:", StringComparison.Ordinal)) break;
                if (!line.StartsWith("#:property ", StringComparison.Ordinal)) continue;
                var assignment = line["#:property ".Length..].Trim();
                var eq = assignment.IndexOf('=');
                if (eq < 0) continue;
                var name = assignment[..eq].Trim();
                var value = Switch(assignment[(eq + 1)..]);
                if (name.Equals("InvariantGlobalization", StringComparison.OrdinalIgnoreCase)) invariant = value;
                else if (name.Equals("PredefinedCulturesOnly", StringComparison.OrdinalIgnoreCase)) predefined = value;
            }
        }
        return (invariant, predefined);
    }

    public static ProjectInfo Evaluate(string projectPath, string configuration, IReadOnlyDictionary<string, string>? globalProperties = null)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath)) throw new FileNotFoundException($"project file '{projectPath}' not found");

        var args = new List<string> { "msbuild", projectPath, "-nologo", $"-p:Configuration={configuration}", $"-t:{DefineConstantsTarget}" };
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
            StartupObject = Prop("StartupObject"),
            InvariantGlobalization = Switch(Prop("InvariantGlobalization")),
            PredefinedCulturesOnly = Switch(Prop("PredefinedCulturesOnly")),
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
    /// one whole-program compilation input. Each project's files are parsed with its own symbols;
    /// compilation settings (language version, nullable) come from the root; global usings are the
    /// union (they're compilation-wide in a single compilation). Diagnostics come from per-project
    /// compilations (RoslynFrontend.ProjectDiagnostics), like `dotnet build`.
    /// </summary>
    public static (FrontendSettings Settings, ProjectInfo Root, IReadOnlyList<ProjectInfo> All) Load(
        string projectPath, string configuration, TextWriter? log = null, IReadOnlyDictionary<string, string>? globalProperties = null)
    {
        var root = Evaluate(projectPath, configuration, globalProperties);
        var all = new List<ProjectInfo> { root };
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.ProjectPath };
        // Breadth-first over the reference graph; each level's evaluations run in parallel
        // (every `dotnet msbuild` evaluation costs ~1 s of process startup).
        var level = root.ProjectReferences.Select(Path.GetFullPath).Where(seen.Add).ToList();
        while (level.Count > 0)
        {
            foreach (var reference in level) log?.WriteLine($"  project reference: {reference}");
            var evaluated = level.AsParallel().AsOrdered().Select(r => Evaluate(r, configuration, globalProperties)).ToList();
            all.AddRange(evaluated);
            level = [.. evaluated.SelectMany(p => p.ProjectReferences).Select(Path.GetFullPath).Where(seen.Add)];
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
            MainTypeName = root.StartupObject,
            Projects = all.Count > 1 ? [.. all.Select(p => new ProjectUnit
            {
                ProjectPath = p.ProjectPath,
                AssemblyName = p.AssemblyName,
                IsRoot = ReferenceEquals(p, root),
                Files = p.Files,
                Defines = p.Defines,
                LanguageVersion = p.LanguageVersion,
                Nullable = p.Nullable,
                AllowUnsafe = p.AllowUnsafe,
                GlobalUsings = p.GlobalUsings.Count > 0 ? string.Join('\n', p.GlobalUsings) : null,
                References = [.. p.ProjectReferences.Select(Path.GetFullPath)],
            })] : [],
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
