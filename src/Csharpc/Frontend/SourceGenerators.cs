using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Csharpc.Frontend;

/// <summary>
/// Runs the source generators the .NET SDK runs for every project — the ones shipped in the
/// Microsoft.NETCore.App.Ref pack's analyzers folder ([LibraryImport] interop stubs, [GeneratedRegex],
/// [JsonSerializable]...). Like csc, csharpc then compiles the generated C# with the user's code, so
/// e.g. LibraryImport marshalling is ordinary C# lowered by csharpc (no special casing in codegen).
/// </summary>
public static class SourceGenerators
{
    /// <summary>
    /// Generators worth loading, keyed by the attribute that triggers them. A null assembly is a generator
    /// built into csharpc: [GeneratedRegex] (GeneratedRegex.cs) replaces the SDK's, whose output is built on
    /// RegexRunner internals.
    /// </summary>
    private static readonly (string Trigger, string? Assembly)[] s_generators =
    [
        ("LibraryImport", "Microsoft.Interop.LibraryImportGenerator.dll"),
        ("GeneratedRegex", null),
        ("JsonSerializable", "System.Text.Json.SourceGeneration.dll"),
    ];

    public static CSharpCompilation Run(CSharpCompilation compilation, IEnumerable<SyntaxTree> userTrees, out IReadOnlyList<Diagnostic> diagnostics)
    {
        diagnostics = [];
        if (Environment.GetEnvironmentVariable("CSHARPC_NO_GENERATORS") == "1") return compilation;
        var text = string.Concat(userTrees.Select(t => t.GetText().ToString()));
        var wanted = s_generators.Where(g => text.Contains(g.Trigger, StringComparison.Ordinal)).ToList();
        if (wanted.Count == 0) return compilation;

        var dir = AnalyzerDirectory();
        var loader = dir is null ? null : new Loader(dir);
        List<ISourceGenerator> generators = [];
        foreach (var (_, assembly) in wanted)
        {
            if (assembly is null) { generators.Add(new GeneratedRegexGenerator().AsSourceGenerator()); continue; }
            if (dir is null || loader is null) continue;
            var path = Path.Combine(dir, assembly);
            if (!File.Exists(path)) continue;
            var reference = new AnalyzerFileReference(path, loader);
            generators.AddRange(reference.GetGenerators(LanguageNames.CSharp));
        }
        if (generators.Count == 0) return compilation;

        var driver = CSharpGeneratorDriver.Create(generators, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);
        diagnostics = generatorDiagnostics;
        if (Environment.GetEnvironmentVariable("CSHARPC_DUMP_GENERATED") is { Length: > 0 } dumpDir)
        {
            Directory.CreateDirectory(dumpDir);
            foreach (var tree in updated.SyntaxTrees.Except(compilation.SyntaxTrees))
                File.WriteAllText(Path.Combine(dumpDir, Path.GetFileName(tree.FilePath)), tree.GetText().ToString());
        }
        return (CSharpCompilation)updated;
    }

    /// <summary>packs/Microsoft.NETCore.App.Ref/&lt;version&gt;/analyzers/dotnet/cs of the SDK csharpc runs on.</summary>
    private static string? AnalyzerDirectory()
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_ANALYZERS") is { Length: > 0 } explicitDir) return explicitDir;
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('/');
        var version = Path.GetFileName(runtimeDir);
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        var packs = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packs)) return null;
        var exact = Path.Combine(packs, version, "analyzers", "dotnet", "cs");
        if (Directory.Exists(exact)) return exact;
        return Directory.GetDirectories(packs).Order().Select(v => Path.Combine(v, "analyzers", "dotnet", "cs"))
            .LastOrDefault(Directory.Exists);
    }

    /// <summary>Loads generator assemblies (and their sibling dependencies) into the default context.</summary>
    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        private readonly string _dir;

        public Loader(string dir)
        {
            _dir = dir;
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var candidate = Path.Combine(_dir, name.Name + ".dll");
                return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
            };
        }

        public void AddDependencyLocation(string fullPath) { }

        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
