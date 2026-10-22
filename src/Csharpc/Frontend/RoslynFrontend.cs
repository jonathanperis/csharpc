using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Csharpc.Frontend;

/// <summary>
/// csharpc does not reimplement C# parsing or semantic analysis: Roslyn *is* the C# front-end,
/// just as gofrontend is shared by gc-go and gollvm. We take Roslyn's fully bound and
/// lowered program (IOperation + ControlFlowGraph) and own everything after it.
/// See docs/adr/0001-roslyn-as-frontend.md.
/// </summary>
public static class RoslynFrontend
{
    /// <summary>Same set the .NET SDK injects for &lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt; console apps.</summary>
    private const string ImplicitUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    public static CSharpCompilation Analyze(IReadOnlyList<string> files, bool implicitUsings, IEnumerable<string> defines) =>
        Analyze(new FrontendSettings
        {
            Files = files,
            Defines = [.. defines],
            GlobalUsings = implicitUsings ? ImplicitUsings : null,
        });

    /// <summary>Default SDK implicit usings, for callers without a project file.</summary>
    public static string DefaultImplicitUsings => ImplicitUsings;

    public static CSharpCompilation Analyze(FrontendSettings settings)
    {
        var languageVersion = LanguageVersionOf(settings.LanguageVersion);

        var parseOptions = ParseOptions(languageVersion, settings.Defines);

        // Each project's files are parsed with that project's own symbols (DefineConstants).
        s_parsed.Clear();
        foreach (var project in settings.Projects)
        {
            var projectOptions = ParseOptions(LanguageVersionOf(project.LanguageVersion), project.Defines);
            foreach (var f in project.Files)
                if (!s_parsed.ContainsKey(Path.GetFullPath(f)))
                    s_parsed[Path.GetFullPath(f)] = CSharpSyntaxTree.ParseText(File.ReadAllText(f), projectOptions, path: Path.GetFullPath(f));
        }
        List<SyntaxTree> trees = [.. settings.Files.Select(f => s_parsed.TryGetValue(Path.GetFullPath(f), out var parsed) ? parsed
            : CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions, path: Path.GetFullPath(f)))];
        if (settings.GlobalUsings is { Length: > 0 } usings)
            trees.Add(CSharpSyntaxTree.ParseText(usings, parseOptions, path: "<global-usings>.g.cs"));
        trees.AddRange(CoreLibTrees(parseOptions));

        var nullable = NullableOf(settings.Nullable);
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication,
            nullableContextOptions: nullable,
            allowUnsafe: settings.AllowUnsafe,
            optimizationLevel: OptimizationLevel.Release,
            mainTypeName: settings.MainTypeName,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS8019"] = ReportDiagnostic.Suppress });

        var name = settings.AssemblyName ?? (settings.Files.Count > 0 ? Path.GetFileNameWithoutExtension(settings.Files[0]) : "program");
        var compilation = CSharpCompilation.Create(name, trees, ReferenceAssemblies(), options);
        // Source generators the SDK runs implicitly ([LibraryImport], [GeneratedRegex], [JsonSerializable]).
        compilation = SourceGenerators.Run(compilation, trees.Where(t => !IsCoreLibTree(t)), out var generatorDiagnostics);
        GeneratorDiagnostics = generatorDiagnostics;
        return compilation;
    }

    private static LanguageVersion LanguageVersionOf(string? text)
    {
        var languageVersion = LanguageVersion.Preview;
        if (text is { Length: > 0 } lv && !LanguageVersionFacts.TryParse(lv, out languageVersion))
            languageVersion = LanguageVersion.Preview;
        // Versions newer than our Roslyn knows (or "default"/"latest") fall back to preview: csharpc tracks the newest language.
        if (languageVersion is LanguageVersion.Default or LanguageVersion.Latest or LanguageVersion.LatestMajor)
            languageVersion = LanguageVersion.Preview;
        return languageVersion;
    }

    private static CSharpParseOptions ParseOptions(LanguageVersion languageVersion, IEnumerable<string> defines) =>
        new CSharpParseOptions(languageVersion, DocumentationMode.None, SourceCodeKind.Regular, ["CSHARPC", .. defines])
            // Accept file-based app directives (`#:property ...`, `#:package ...`) like `dotnet run app.cs`.
            .WithFeatures([new("FileBasedProgram", "true")]);

    private static NullableContextOptions NullableOf(string? text) => text?.ToLowerInvariant() switch
    {
        "disable" => NullableContextOptions.Disable,
        "warnings" => NullableContextOptions.Warnings,
        "annotations" => NullableContextOptions.Annotations,
        _ => NullableContextOptions.Enable,
    };

    private static readonly Dictionary<string, SyntaxTree> s_parsed = new(StringComparer.Ordinal);

    /// <summary>
    /// Errors that compiling several projects as one compilation can introduce although C# semantics
    /// (and the generated code) are the same: definite-assignment rules are relaxed for structs
    /// imported from another assembly (their inaccessible fields don't count; warning CS8882).
    /// </summary>
    public static readonly IReadOnlySet<string> DefiniteAssignmentErrors = new HashSet<string> { "CS0165", "CS0170", "CS0171", "CS0177", "CS0188", "CS0269" };

    /// <summary>
    /// What `dotnet build` reports: each project compiled on its own, referencing the compilations
    /// of the projects it references (csharpc still generates code from one merged compilation).
    /// </summary>
    public static ImmutableArray<Diagnostic> ProjectDiagnostics(FrontendSettings settings)
    {
        var byPath = settings.Projects.ToDictionary(p => p.ProjectPath, StringComparer.Ordinal);
        var built = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var project in settings.Projects) Build(project);
        return builder.ToImmutable();

        CSharpCompilation Build(ProjectUnit project)
        {
            if (built.TryGetValue(project.ProjectPath, out var existing)) return existing;
            List<MetadataReference> references = [.. ReferenceAssemblies()];
            foreach (var reference in project.References)
                if (byPath.TryGetValue(reference, out var referenced)) references.Add(Build(referenced).ToMetadataReference());
            var parseOptions = ParseOptions(LanguageVersionOf(project.LanguageVersion), project.Defines);
            List<SyntaxTree> trees = [.. project.Files.Select(f => s_parsed[Path.GetFullPath(f)])];
            if (project.GlobalUsings is { Length: > 0 } usings)
                trees.Add(CSharpSyntaxTree.ParseText(usings, parseOptions, path: "<global-usings>.g.cs"));
            var options = new CSharpCompilationOptions(project.IsRoot ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableOf(project.Nullable),
                allowUnsafe: project.AllowUnsafe,
                optimizationLevel: OptimizationLevel.Release,
                mainTypeName: project.IsRoot ? settings.MainTypeName : null,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS8019"] = ReportDiagnostic.Suppress });
            var compilation = CSharpCompilation.Create(project.AssemblyName, trees, references, options);
            compilation = SourceGenerators.Run(compilation, trees, out var generatorDiagnostics);
            builder.AddRange(generatorDiagnostics);
            foreach (var tree in compilation.SyntaxTrees)
                builder.AddRange(compilation.GetSemanticModel(tree).GetDiagnostics());
            return built[project.ProjectPath] = compilation;
        }
    }

    /// <summary>Diagnostics reported by source generators during the last Analyze call.</summary>
    public static IReadOnlyList<Diagnostic> GeneratorDiagnostics { get; private set; } = [];

    /// <summary>
    /// The shadow corelib (src/Csharpc/corelib/**.cs, shipped next to the compiler): C# implementations
    /// of BCL types that codegen redirects library symbols to (CodeGen/CoreLib.cs). Parsed with the
    /// user's options so the compilation stays consistent; written in conservative C# for that reason.
    /// Only the members a program actually uses get bound and lowered.
    /// </summary>
    private static IEnumerable<SyntaxTree> CoreLibTrees(CSharpParseOptions parseOptions)
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_NO_CORELIB") == "1") yield break;
        var dir = Path.Combine(AppContext.BaseDirectory, "corelib");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order())
            yield return CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions,
                path: CodeGen.CoreLib.TreePrefix + Path.GetRelativePath(dir, file));
    }

    public static bool IsCoreLibTree(SyntaxTree tree) => tree.FilePath.StartsWith(CodeGen.CoreLib.TreePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Diagnostics for the user's sources only. The corelib is bound lazily, member by member, as
    /// codegen needs it (set CSHARPC_CHECK_CORELIB=1 to validate the whole corelib instead).
    /// </summary>
    public static ImmutableArray<Diagnostic> UserDiagnostics(CSharpCompilation compilation)
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_CHECK_CORELIB") == "1") return compilation.GetDiagnostics();
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        builder.AddRange(GeneratorDiagnostics);
        foreach (var tree in compilation.SyntaxTrees.Where(t => !IsCoreLibTree(t)))
            builder.AddRange(compilation.GetSemanticModel(tree).GetDiagnostics());
        return builder.ToImmutable();
    }

    /// <summary>
    /// The .NET 11 reference assemblies Roslyn binds user code against. The metadata only drives
    /// overload resolution and typing; none of that IL is ever compiled. Preferred source: the SDK's
    /// reference pack (packs/Microsoft.NETCore.App.Ref/&lt;ver&gt;/ref/net11.0) — the exact API surface a
    /// normal build uses, and it works when csharpc itself runs as a NativeAOT executable, where there
    /// is no TRUSTED_PLATFORM_ASSEMBLIES list. Fallback: the framework csharpc runs on.
    /// Override with CSHARPC_REFERENCES=&lt;directory of reference assemblies&gt;.
    /// </summary>
    private static ImmutableArray<MetadataReference> ReferenceAssemblies() => s_references ??= LoadReferences();

    private static ImmutableArray<MetadataReference>? s_references;

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var dir = Environment.GetEnvironmentVariable("CSHARPC_REFERENCES") is { Length: > 0 } explicitDir ? explicitDir : FindReferencePack();
        if (dir is not null)
            return [.. Directory.EnumerateFiles(dir, "*.dll").Order()
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];

        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").Split(Path.PathSeparator);
        return [.. tpa
            .Where(p => p.Length > 0 && p.StartsWith(runtimeDir, StringComparison.Ordinal))
            .Where(p => Path.GetFileName(p) is var n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard.dll" or "mscorlib.dll"))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    }

    /// <summary>Newest net11.0 reference pack among the usual dotnet installation roots.</summary>
    private static string? FindReferencePack()
    {
        IEnumerable<string?> roots =
        [
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            DotnetOnPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            "/usr/share/dotnet", "/usr/lib/dotnet", "/usr/local/share/dotnet",
        ];
        return roots.Where(r => r is not null && Directory.Exists(Path.Combine(r, "packs", "Microsoft.NETCore.App.Ref")))
            .SelectMany(r => Directory.EnumerateDirectories(Path.Combine(r!, "packs", "Microsoft.NETCore.App.Ref")))
            .Select(versionDir => (Version: ParseVersion(Path.GetFileName(versionDir)), Dir: Path.Combine(versionDir, "ref", "net11.0")))
            .Where(c => c.Version.Major == 11 && Directory.Exists(c.Dir))
            .OrderByDescending(c => c.Version)
            .Select(c => c.Dir)
            .FirstOrDefault();

        static Version ParseVersion(string text) =>
            Version.TryParse(text.Split('-')[0], out var v) ? v : new Version(0, 0);
    }

    private static string? DotnetOnPath()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var candidate = Path.Combine(dir, "dotnet");
            if (!File.Exists(candidate)) continue;
            var resolved = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
            return Path.GetDirectoryName(resolved);
        }
        return null;
    }
}

/// <summary>Everything the front-end needs; produced from CLI arguments or an evaluated project.</summary>
public sealed record FrontendSettings
{
    public required IReadOnlyList<string> Files { get; init; }
    public IReadOnlyList<string> Defines { get; init; } = [];
    public string? LanguageVersion { get; init; }
    public string? Nullable { get; init; }
    public bool AllowUnsafe { get; init; } = true;
    /// <summary>Source of a synthesized global-usings file (null = none).</summary>
    public string? GlobalUsings { get; init; }
    public string? AssemblyName { get; init; }
    /// <summary>Type whose Main is the entry point (&lt;StartupObject&gt;); null = the only one.</summary>
    public string? MainTypeName { get; init; }
    /// <summary>The root project and the projects it references, when there are several.</summary>
    public IReadOnlyList<ProjectUnit> Projects { get; init; } = [];
}

/// <summary>One project's own compilation settings (projects keep them when folded into one program).</summary>
public sealed record ProjectUnit
{
    public required string ProjectPath { get; init; }
    public required string AssemblyName { get; init; }
    public required bool IsRoot { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
    public IReadOnlyList<string> Defines { get; init; } = [];
    public string? LanguageVersion { get; init; }
    public string? Nullable { get; init; }
    public bool AllowUnsafe { get; init; }
    public string? GlobalUsings { get; init; }
    /// <summary>Full paths of the referenced projects.</summary>
    public IReadOnlyList<string> References { get; init; } = [];
}
