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
        var languageVersion = LanguageVersion.Preview;
        if (settings.LanguageVersion is { Length: > 0 } lv && !LanguageVersionFacts.TryParse(lv, out languageVersion))
            languageVersion = LanguageVersion.Preview;
        // Versions newer than our Roslyn knows (or "default"/"latest") fall back to preview: csharpc tracks the newest language.
        if (languageVersion is LanguageVersion.Default or LanguageVersion.Latest or LanguageVersion.LatestMajor)
            languageVersion = LanguageVersion.Preview;

        var parseOptions = new CSharpParseOptions(languageVersion, DocumentationMode.None, SourceCodeKind.Regular,
            ["CSHARPC", .. settings.Defines])
            // Accept file-based app directives (`#:property ...`, `#:package ...`) like `dotnet run app.cs`.
            .WithFeatures([new("FileBasedProgram", "true")]);

        List<SyntaxTree> trees = [.. settings.Files.Select(f =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions, path: Path.GetFullPath(f)))];
        if (settings.GlobalUsings is { Length: > 0 } usings)
            trees.Add(CSharpSyntaxTree.ParseText(usings, parseOptions, path: "<global-usings>.g.cs"));
        trees.AddRange(CoreLibTrees(parseOptions));

        var nullable = settings.Nullable?.ToLowerInvariant() switch
        {
            "disable" => NullableContextOptions.Disable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => NullableContextOptions.Enable,
        };
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication,
            nullableContextOptions: nullable,
            allowUnsafe: settings.AllowUnsafe,
            optimizationLevel: OptimizationLevel.Release,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS8019"] = ReportDiagnostic.Suppress });

        var name = settings.AssemblyName ?? (settings.Files.Count > 0 ? Path.GetFileNameWithoutExtension(settings.Files[0]) : "program");
        var compilation = CSharpCompilation.Create(name, trees, ReferenceAssemblies(), options);
        // Source generators the SDK runs implicitly ([LibraryImport], [GeneratedRegex], [JsonSerializable]).
        compilation = SourceGenerators.Run(compilation, trees.Where(t => !IsCoreLibTree(t)), out var generatorDiagnostics);
        GeneratorDiagnostics = generatorDiagnostics;
        return compilation;
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
    /// Bind against the framework assemblies of the runtime csharpc itself runs on (.NET 11). The
    /// metadata only drives overload resolution and typing; none of that IL is ever compiled.
    /// </summary>
    private static ImmutableArray<MetadataReference> ReferenceAssemblies()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").Split(Path.PathSeparator);
        return [.. tpa
            .Where(p => p.StartsWith(runtimeDir, StringComparison.Ordinal))
            .Where(p => Path.GetFileName(p) is var n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard.dll" or "mscorlib.dll"))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
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
}
