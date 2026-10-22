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

    public static CSharpCompilation Analyze(IReadOnlyList<string> files, bool implicitUsings, IEnumerable<string> defines)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular,
            ["CSHARPC", .. defines]);

        List<SyntaxTree> trees = [.. files.Select(f =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions, path: Path.GetFullPath(f)))];
        if (implicitUsings)
            trees.Add(CSharpSyntaxTree.ParseText(ImplicitUsings, parseOptions, path: "<implicit-usings>.g.cs"));

        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication,
            nullableContextOptions: NullableContextOptions.Enable,
            allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS8019"] = ReportDiagnostic.Suppress });

        var name = files.Count > 0 ? Path.GetFileNameWithoutExtension(files[0]) : "program";
        return CSharpCompilation.Create(name, trees, ReferenceAssemblies(), options);
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
