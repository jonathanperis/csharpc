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
    /// <summary>JsonSerializerIsReflectionEnabledByDefault (false in trimmed and AOT-published projects).</summary>
    public bool JsonReflectionEnabled { get; init; } = true;
    /// <summary>The assembly's version as the SDK stamps it (FrontendSettings.AssemblyVersion).</summary>
    public string? AssemblyVersion { get; init; } = "1.0.0.0";
    public List<string> GlobalUsings { get; } = [];
    public List<string> ProjectReferences { get; } = [];
    /// <summary>Explicit &lt;PackageReference&gt; items ("Id Version").</summary>
    public List<string> PackageReferences { get; } = [];
    /// <summary>&lt;Reference&gt; items with an assembly file: compiled from IL (docs/adr/0010-il-frontend.md).</summary>
    public List<string> AssemblyReferences { get; } = [];
    /// <summary>RuntimeHostConfigurationOption items (runtimeconfig.json configProperties), in item order.</summary>
    public List<KeyValuePair<string, string>> RuntimeConfig { get; } = [];
    /// <summary>&lt;Reference&gt; items csharpc could not map to a file (e.g. GAC-style names).</summary>
    public List<string> UnresolvedReferences { get; } = [];
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
        "StartupObject", "JsonSerializerIsReflectionEnabledByDefault",
        "AssemblyVersion", "Version", "GenerateAssemblyInfo", "GenerateAssemblyVersionAttribute", "SignAssembly",
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

    private static readonly string[] Items = ["Compile", "Using", "ProjectReference", "PackageReference", "Reference", "RuntimeHostConfigurationOption"];

    /// <summary>
    /// configProperties of a file-based app (`dotnet run app.cs`, which defaults to PublishAot=true and
    /// so gets the AOT feature switches), as the .NET 11 SDK writes them; EntryPointFilePath and
    /// EntryPointFileDirectoryPath are added per file. Single-file compilations behave like that.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> FileBasedRuntimeConfig(string entryFile)
    {
        var path = Path.GetFullPath(entryFile);
        yield return new("EntryPointFilePath", path);
        yield return new("EntryPointFileDirectoryPath", Path.GetDirectoryName(path)!);
        string[] enabled = ["Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability", "System.Linq.Enumerable.IsSizeOptimized"];
        string[] disabled =
        [
            "System.ComponentModel.DefaultValueAttribute.IsSupported", "System.ComponentModel.Design.IDesignerHost.IsSupported",
            "System.ComponentModel.TypeConverter.EnableUnsafeBinaryFormatterInDesigntimeLicenseContextSerialization",
            "System.ComponentModel.TypeDescriptor.IsComObjectDescriptorSupported", "System.Data.DataSet.XmlSerializationIsSupported",
            "System.Diagnostics.Tracing.EventSource.IsSupported", "System.Net.SocketsHttpHandler.Http3Support",
            "System.Resources.ResourceManager.AllowCustomResourceTypes", "System.Resources.UseSystemResourceKeys",
            "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", "System.Runtime.InteropServices.BuiltInComInterop.IsSupported",
            "System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting", "System.Runtime.InteropServices.EnableCppCLIHostActivation",
            "System.Runtime.InteropServices.Marshalling.EnableGeneratedComInterfaceComImportInterop",
            "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "System.StartupHookProvider.IsSupported",
            "System.Text.Encoding.EnableUnsafeUTF7Encoding", "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault",
            "System.Threading.Thread.EnableAutoreleasePool", "System.Linq.Expressions.CanEmitObjectArrayDelegate",
        ];
        foreach (var name in enabled) yield return new(name, "true");
        foreach (var name in disabled) yield return new(name, "false");
    }

    /// <summary>
    /// The version the SDK's generated AssemblyInfo gives the assembly: AssemblyVersion, else Version
    /// without its prerelease/build suffix, else 1.0.0.0. "source" when the SDK generates no
    /// [AssemblyVersion] (the program's own attribute, or 0.0.0.0, applies); null when it can't be
    /// told at compile time (signed assemblies, wildcard versions).
    /// </summary>
    public static string? AssemblyVersionOf(Func<string, string?> property)
    {
        static bool IsFalse(string? v) => string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(property("SignAssembly"), "true", StringComparison.OrdinalIgnoreCase)) return null;
        if (IsFalse(property("GenerateAssemblyInfo")) || IsFalse(property("GenerateAssemblyVersionAttribute"))) return "source";
        var text = property("AssemblyVersion") ?? property("Version")?.Split('-', '+')[0];
        if (text is null) return "1.0.0.0";
        if (!Version.TryParse(text, out var v)) return null;
        return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}.{Math.Max(v.Revision, 0)}";
    }

    /// <summary>
    /// AssemblyVersionOf for a file-based app: `#:property Name=Value` directives at the top of the
    /// entry file set MSBuild properties.
    /// </summary>
    public static string? FileBasedAssemblyVersion(string entryFile)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadLines(entryFile))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("#:property ", StringComparison.Ordinal)) continue;
                var assignment = trimmed["#:property ".Length..].Trim();
                var eq = assignment.IndexOfAny(['=', ' ']);
                if (eq > 0) properties[assignment[..eq].Trim()] = assignment[(eq + 1)..].Trim().Trim('"');
            }
        }
        catch (IOException) { }
        return AssemblyVersionOf(name => properties.TryGetValue(name, out var v) && v.Length > 0 ? v : null);
    }

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
            JsonReflectionEnabled = !string.Equals(Prop("JsonSerializerIsReflectionEnabledByDefault"), "false", StringComparison.OrdinalIgnoreCase),
            AssemblyVersion = AssemblyVersionOf(Prop),
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

        // <Reference Include="Name"><HintPath>path/Name.dll</HintPath></Reference> or Include="path/Name.dll".
        foreach (var r in Item("Reference"))
        {
            if (string.Equals(Meta(r, "IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase)) continue;
            var identity = Meta(r, "Identity") ?? "";
            var candidate = Meta(r, "HintPath") is { } hint ? Path.GetFullPath(hint, info.ProjectDirectory)
                : identity.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? Meta(r, "FullPath") ?? Path.GetFullPath(identity, info.ProjectDirectory)
                : null;
            if (candidate is not null && File.Exists(candidate)) info.AssemblyReferences.Add(candidate);
            else info.UnresolvedReferences.Add(candidate ?? identity);
        }

        // runtimeconfig.json writes the last value of each option; boolean JSON values reach the runtime as "true"/"false".
        foreach (var o in Item("RuntimeHostConfigurationOption"))
            if (Meta(o, "Identity") is { } name)
            {
                var value = Meta(o, "Value") ?? "";
                info.RuntimeConfig.RemoveAll(kv => kv.Key == name);
                info.RuntimeConfig.Add(new(name, bool.TryParse(value, out var flag) ? (flag ? "true" : "false") : value));
            }

        foreach (var p in Item("PackageReference"))
            if (!string.Equals(Meta(p, "IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase))
                info.PackageReferences.Add(Meta(p, "Identity") + (Meta(p, "Version") is { } v ? " " + v : ""));

        // NuGet packages ship assemblies: restore, then take the runtime assets NuGet selected for this
        // target framework (the implementation assemblies, transitive closure included) and compile
        // them from IL like any other <Reference> (docs/adr/0010-il-frontend.md).
        if (info.PackageReferences.Count > 0)
            info.AssemblyReferences.AddRange(ResolvePackageAssemblies(projectPath, configuration, globalProperties));

        return info;
    }

    /// <summary>`dotnet msbuild -restore -t:ResolvePackageAssets`: the package DLLs `dotnet build` would copy to the output.</summary>
    private static IEnumerable<string> ResolvePackageAssemblies(string projectPath, string configuration, IReadOnlyDictionary<string, string>? globalProperties)
    {
        var args = new List<string> { "msbuild", projectPath, "-nologo", "-restore", $"-p:Configuration={configuration}",
            "-t:ResolvePackageAssets", "-getItem:RuntimeCopyLocalItems" };
        foreach (var (k, v) in globalProperties ?? new Dictionary<string, string>()) args.Add($"-p:{k}={v}");
        string json;
        try { json = RunDotnet(args, Path.GetDirectoryName(projectPath)!); }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException($"could not restore the NuGet packages of '{Path.GetFileName(projectPath)}': {e.Message}", e);
        }
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Items", out var items) || !items.TryGetProperty("RuntimeCopyLocalItems", out var assets)) return [];
        return [.. assets.EnumerateArray()
            .Select(a => a.TryGetProperty("FullPath", out var p) ? p.GetString() : null)
            .Where(p => p is not null && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => p!)];
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
            AssemblyVersion = root.AssemblyVersion,
            MainTypeName = root.StartupObject,
            IlReferencePaths = [.. all.SelectMany(p => p.AssemblyReferences).Distinct(StringComparer.Ordinal)],
            RuntimeConfig = root.RuntimeConfig,
            JsonReflectionEnabled = root.JsonReflectionEnabled,
            Projects = all.Count > 1 ? [.. all.Select(p => new ProjectUnit
            {
                ProjectPath = p.ProjectPath,
                AssemblyName = p.AssemblyName,
                AssemblyVersion = p.AssemblyVersion,
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
