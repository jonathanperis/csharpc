extern alias JsonGenerator;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Csharpc.Frontend;

/// <summary>
/// What csc gets for source generators in a project build (ProjectLoader reads it from the csc command
/// line the SDK's CoreCompile target computes): the /analyzer assemblies after the SDK's own selection
/// (targeting packs, NuGet packages' analyzers folders, the Razor SDK's generator), the /additionalfile
/// items (.cshtml/.razor for Razor), the /analyzerconfig files (the GeneratedMSBuildEditorConfig with the
/// build_property.* / build_metadata.* options, .editorconfig and .globalconfig files) and the /features
/// (InterceptorsNamespaces for the interceptors the request-delegate and configuration-binder generators emit).
/// </summary>
public sealed record GeneratorInputs
{
    public IReadOnlyList<string> Analyzers { get; init; } = [];
    public IReadOnlyList<string> AdditionalFiles { get; init; } = [];
    public IReadOnlyList<string> AnalyzerConfigFiles { get; init; } = [];
    public IReadOnlyDictionary<string, string> Features { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>csc's base directory for generated files (its /out directory): generated trees' paths, as PDBs and stack traces show them.</summary>
    public string? GeneratedFilesDirectory { get; init; }
}

/// <summary>
/// Runs Roslyn source generators over the program, as csc does, and compiles the generated C# with the
/// user's code: e.g. LibraryImport marshalling or Razor templates are ordinary C# lowered by csharpc (no
/// special casing in codegen). A project brings its generators (<see cref="GeneratorInputs"/>); single
/// files get the ones the SDK runs for every project, from the Microsoft.NETCore.App.Ref pack.
/// </summary>
public static class SourceGenerators
{
    /// <summary>
    /// Single files: generators worth running, keyed by the attribute that triggers them. The SDK's JSON and
    /// LibraryImport generators are linked into csharpc (<see cref="Linked"/>); the entry with a null assembly is a
    /// generator built into csharpc: [GeneratedRegex] (GeneratedRegex.cs) replaces the SDK's, whose output is built
    /// on RegexRunner internals.
    /// </summary>
    private static readonly (string Trigger, string? Assembly)[] s_generators =
    [
        ("LibraryImport", LibraryImportGeneratorAssembly),
        ("GeneratedRegex", null),
        ("JsonSerializable", JsonGeneratorAssembly),
    ];

    private const string LibraryImportGeneratorAssembly = "Microsoft.Interop.LibraryImportGenerator.dll";
    private const string JsonGeneratorAssembly = "System.Text.Json.SourceGeneration.dll";
    /// <summary>The SDK's (and the System.Text.RegularExpressions package's) [GeneratedRegex] generator, which csharpc replaces.</summary>
    private const string RegexGeneratorAssembly = "System.Text.RegularExpressions.Generator.dll";

    public static CSharpCompilation Run(CSharpCompilation compilation, IEnumerable<SyntaxTree> userTrees, GeneratorInputs? inputs, out IReadOnlyList<Diagnostic> diagnostics)
    {
        diagnostics = [];
        if (Environment.GetEnvironmentVariable("CSHARPC_NO_GENERATORS") == "1") return compilation;
        List<Diagnostic> found = [];
        var generators = inputs is null ? DefaultGenerators(userTrees) : ProjectGenerators(compilation, inputs, found);
        if (generators.Count == 0) { diagnostics = found; return compilation; }

        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        ImmutableArray<AdditionalText> additionalTexts = [];
        AnalyzerConfigOptionsProvider? optionsProvider = null;
        string? generatedDirectory = null;
        if (inputs is not null)
        {
            additionalTexts = [.. inputs.AdditionalFiles.Select(p => (AdditionalText)new AdditionalFile(p))];
            optionsProvider = ConfigOptions.Create(inputs.AnalyzerConfigFiles, found);
            generatedDirectory = inputs.GeneratedFilesDirectory is { } dir && Path.IsPathRooted(dir) ? dir : null;
        }
        // csc disables host outputs (RegisterHostOutput is for IDEs) and roots generated files at its /out directory.
        var driver = CSharpGeneratorDriver.Create(generators, additionalTexts, parseOptions, optionsProvider,
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.Host, trackIncrementalGeneratorSteps: false, generatedDirectory));
        var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);
        found.AddRange(generatorDiagnostics.Select(GeneratorFailureAsError));
        // The diagnostic carries a failed generator's exception type and message only.
        if (Environment.GetEnvironmentVariable("CSHARPC_TRACE") == "1")
            foreach (var result in ran.GetRunResult().Results.Where(r => r.Exception is not null))
                Console.Error.WriteLine($"[csharpc] generator {result.Generator.GetType().Name} failed: {result.Exception}");
        diagnostics = found;
        updated = DropReflectionOnlyMetadata(compilation, (CSharpCompilation)updated);
        if (Environment.GetEnvironmentVariable("CSHARPC_DUMP_GENERATED") is { Length: > 0 } dumpDir)
        {
            Directory.CreateDirectory(dumpDir);
            foreach (var tree in updated.SyntaxTrees.Except(compilation.SyntaxTrees))
                File.WriteAllText(Path.Combine(dumpDir, Path.GetFileName(tree.FilePath)), tree.GetText().ToString());
        }
        return (CSharpCompilation)updated;
    }

    /// <summary>
    /// With several projects each project's generators have run on its own compilation (RoslynFrontend.BuildProjects).
    /// What no project owns is the metadata for the JSON contexts csharpc synthesizes for the merged program's
    /// reflection-style System.Text.Json calls: the JSON generator runs once more over the merged compilation, with the
    /// root project's options, and only the output for those synthesized contexts is kept (the projects' own contexts
    /// already have theirs).
    /// </summary>
    public static IReadOnlyList<SyntaxTree> RunSynthesizedJsonContexts(CSharpCompilation compilation, GeneratorInputs? inputs, out IReadOnlyList<Diagnostic> diagnostics)
    {
        diagnostics = [];
        if (!compilation.SyntaxTrees.Any(JsonReflectionContexts.IsSynthesizedTree)) return [];
        var jsonOnly = (inputs ?? new GeneratorInputs()) with
        {
            Analyzers = [.. (inputs?.Analyzers ?? []).Where(a => Path.GetFileName(a).Equals(JsonGeneratorAssembly, StringComparison.OrdinalIgnoreCase))],
            AdditionalFiles = [],
        };
        var updated = Run(compilation, [], jsonOnly, out var found);
        // Findings about the projects' own contexts are the per-project runs' to report.
        diagnostics = [.. found.Where(d => d.Severity == DiagnosticSeverity.Error && d.Location == Location.None)];
        return [.. updated.SyntaxTrees.Except(compilation.SyntaxTrees).Where(t => JsonReflectionContexts.IsSynthesizedContextFile(t.FilePath))];
    }

    private static List<ISourceGenerator> DefaultGenerators(IEnumerable<SyntaxTree> userTrees)
    {
        var text = string.Concat(userTrees.Select(t => t.GetText().ToString()));
        List<ISourceGenerator> generators = [];
        foreach (var (trigger, assembly) in s_generators)
        {
            if (!text.Contains(trigger, StringComparison.Ordinal)) continue;
            generators.Add(assembly is null ? new GeneratedRegexGenerator().AsSourceGenerator() : LinkedGenerator(assembly)!);
        }
        return generators;
    }

    /// <summary>
    /// The SDK's generators that are linked into csharpc (they come from the Microsoft.NETCore.App.Ref pack of the SDK
    /// that built it, Csharpc.csproj): no assembly is loaded at run time, so a csharpc compiled by csharpc runs them too.
    /// </summary>
    private static ISourceGenerator? LinkedGenerator(string assemblyFile) => assemblyFile.ToLowerInvariant() switch
    {
        "system.text.json.sourcegeneration.dll" => new JsonGenerator::System.Text.Json.SourceGeneration.JsonSourceGenerator().AsSourceGenerator(),
        "microsoft.interop.libraryimportgenerator.dll" => new Microsoft.Interop.LibraryImportGenerator().AsSourceGenerator(),
        _ => null,
    };

    /// <summary>
    /// The linked generator for a project's /analyzer file when it is the same build (assembly version) as the linked
    /// one: it then makes the same output the file would, and needs no assembly loading. Another version (a NuGet
    /// package's own copy) is loaded from its file, as csc does.
    /// </summary>
    private static ISourceGenerator? LinkedFor(string path)
    {
        var file = Path.GetFileName(path);
        Type? marker = file.ToLowerInvariant() switch
        {
            "system.text.json.sourcegeneration.dll" => typeof(JsonGenerator::System.Text.Json.SourceGeneration.JsonSourceGenerator),
            "microsoft.interop.libraryimportgenerator.dll" => typeof(Microsoft.Interop.LibraryImportGenerator),
            _ => null,
        };
        if (marker is null) return null;
        try
        {
            var identity = System.Reflection.Metadata.MetadataReader.GetAssemblyName(path);
            return identity.Version == marker.Assembly.GetName().Version ? LinkedGenerator(file) : null;
        }
        catch (Exception e) when (e is IOException or BadImageFormatException) { return null; }
    }

    /// <summary>
    /// The generators of a project's /analyzer assemblies (assemblies with only analyzers contribute
    /// nothing; csharpc doesn't run analyzers, whose findings never change the program). The SDK's
    /// [GeneratedRegex] generator is replaced by csharpc's.
    /// </summary>
    private static List<ISourceGenerator> ProjectGenerators(CSharpCompilation compilation, GeneratorInputs inputs, List<Diagnostic> diagnostics)
    {
        List<ISourceGenerator> generators = [];
        var analyzers = inputs.Analyzers.ToList();
        // csharpc's reflection-based System.Text.Json support synthesizes [JsonSerializable] contexts
        // (JsonReflectionContexts) that the JSON generator must turn into metadata, whatever the project's analyzers.
        if (!analyzers.Any(a => Path.GetFileName(a).Equals(JsonGeneratorAssembly, StringComparison.OrdinalIgnoreCase))
            && compilation.SyntaxTrees.Any(JsonReflectionContexts.IsSynthesizedTree))
            generators.Add(LinkedGenerator(JsonGeneratorAssembly)!);
        foreach (var path in analyzers)
        {
            if (Path.GetFileName(path).Equals(RegexGeneratorAssembly, StringComparison.OrdinalIgnoreCase))
            {
                generators.Add(new GeneratedRegexGenerator().AsSourceGenerator());
                continue;
            }
            if (!File.Exists(path))
            {
                // csc: error CS0006 for an /analyzer file that doesn't exist.
                diagnostics.Add(Error("CS0006", $"Metadata file '{path}' could not be found"));
                continue;
            }
            if (LinkedFor(path) is { } linked)
            {
                generators.Add(linked);
                continue;
            }
            var reference = Reference(path);
            void OnFailure(object? sender, AnalyzerLoadFailureEventArgs e)
            {
                // No analyzers at all (CS8033) is fine; anything else means a generator csc would load and run
                // is missing here, so its output would be missing too: an error, not csc's warning.
                if (e.ErrorCode == AnalyzerLoadFailureEventArgs.FailureErrorCode.NoAnalyzers) return;
                var what = e.TypeName is null ? $"analyzer assembly '{path}'" : $"generator '{e.TypeName}' from '{path}'";
                diagnostics.Add(Error(e.ErrorCode == AnalyzerLoadFailureEventArgs.FailureErrorCode.UnableToLoadAnalyzer ? "CS8034" : "CS8032",
                    $"csharpc could not load {what}: {e.Message}{(e.Exception is { } x ? $" ({x.GetType().Name}: {x.Message})" : "")}"));
            }
            reference.AnalyzerLoadFailed += OnFailure;
            try { generators.AddRange(reference.GetGenerators(LanguageNames.CSharp)); }
            finally { reference.AnalyzerLoadFailed -= OnFailure; }
        }
        // csc runs one instance per generator type; an assembly listed twice would otherwise run twice.
        return [.. generators.DistinctBy(g => g.GetGeneratorType())];
    }

    /// <summary>
    /// csc reports a generator that throws (CS8784 in Initialize, CS8785 while generating) as a warning and
    /// compiles without its output. Under csharpc that is likely csharpc's environment, not the program, and the
    /// missing output would make the program differ from `dotnet build`'s: an error.
    /// </summary>
    private static Diagnostic GeneratorFailureAsError(Diagnostic d) =>
        d.Id is "CS8784" or "CS8785" && d.Severity != DiagnosticSeverity.Error
            ? Diagnostic.Create(new DiagnosticDescriptor(d.Id, d.Descriptor.Title, "{0}", d.Descriptor.Category, DiagnosticSeverity.Error, true),
                d.Location, d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            : d;

    private static Diagnostic Error(string id, string message) =>
        Diagnostic.Create(new DiagnosticDescriptor(id, "Source generator", "{0}", "Compiler", DiagnosticSeverity.Error, true), Location.None, message);

    /// <summary>
    /// The JSON source generator emits reflection lambdas (`AttributeProviderFactory = static () =>
    /// typeof(T).GetProperty(...)`) that System.Text.Json only invokes when a program inspects
    /// JsonPropertyInfo.AttributeProvider. csharpc keeps reflection metadata only where a program needs it, so they
    /// become null: except for a member marked [Obsolete], whose factory stays because JsonSchemaExporter reads it
    /// to write `"deprecated": true`.
    /// </summary>
    private static CSharpCompilation DropReflectionOnlyMetadata(CSharpCompilation before, CSharpCompilation after)
    {
        foreach (var tree in after.SyntaxTrees.Except(before.SyntaxTrees).ToList())
        {
            if (!tree.FilePath.Contains("System.Text.Json.SourceGeneration", StringComparison.Ordinal) && !tree.GetText().ToString().Contains("AttributeProviderFactory", StringComparison.Ordinal))
                continue;
            var root = tree.GetRoot();
            var assignments = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax>()
                .Where(a => a.Left is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax { Identifier.ValueText: "AttributeProviderFactory" or "ConstructorAttributeProviderFactory" })
                .ToList();
            if (assignments.Count == 0) continue;
            var model = after.GetSemanticModel(tree);
            var keep = assignments
                .Where(a => ((Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax)a.Left).Identifier.ValueText == "AttributeProviderFactory" && IsObsoleteMemberLookup(a.Right, model))
                .Select(a => a.Right).ToHashSet();
            var newRoot = root.ReplaceNodes(assignments.Select(a => a.Right).Where(r => !keep.Contains(r)),
                (_, _) => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            after = after.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(newRoot, tree.Options));
        }
        return after;
    }

    /// <summary>`static () => typeof(T).GetProperty("Name", ...)` / GetField: does T declare that member with [Obsolete]?</summary>
    private static bool IsObsoleteMemberLookup(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax factory, SemanticModel model)
    {
        var call = factory.DescendantNodesAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().FirstOrDefault();
        if (call is not { Expression: Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetProperty" or "GetField" } access } ||
            access.Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax typeOf ||
            call.ArgumentList.Arguments.Count == 0 ||
            call.ArgumentList.Arguments[0].Expression is not Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax { Token.Value: string name } ||
            model.GetTypeInfo(typeOf.Type).Type is not { } type)
            return false;
        return type.GetMembers(name).Any(m => m is IPropertySymbol or IFieldSymbol &&
            m.GetAttributes().Any(a => a.AttributeClass is { Name: "ObsoleteAttribute", ContainingNamespace.Name: "System" }));
    }

    /// <summary>One reference per analyzer file, so generator instances (and loaded assemblies) are shared by every run.</summary>
    private static AnalyzerFileReference Reference(string path) =>
        s_references.GetOrAdd(Path.GetFullPath(path), p => new AnalyzerFileReference(p, Loader.Instance));

    private static readonly ConcurrentDictionary<string, AnalyzerFileReference> s_references = new(StringComparer.Ordinal);

    /// <summary>
    /// Loads analyzer assemblies like csc on .NET: one load context per directory, so two packages may ship
    /// different versions of the same dependency, with the compiler's own assemblies (Roslyn, the framework)
    /// shared from the host, which generators must bind to.
    /// </summary>
    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public static readonly Loader Instance = new();
        private readonly ConcurrentDictionary<string, DirectoryContext> _contexts = new(StringComparer.Ordinal);

        public void AddDependencyLocation(string fullPath) { }

        public Assembly LoadFromPath(string fullPath) =>
            _contexts.GetOrAdd(Path.GetDirectoryName(fullPath)!, d => new DirectoryContext(d)).LoadFile(fullPath);

        private static readonly string s_runtimeDirectory = Path.GetFullPath(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());

        /// <summary>An assembly of the shared framework csharpc runs on (System.*, netstandard, mscorlib ...).</summary>
        private static bool IsFrameworkAssembly(string name) =>
            File.Exists(Path.Combine(s_runtimeDirectory, name + ".dll"));

        private sealed class DirectoryContext(string directory) : AssemblyLoadContext($"csharpc analyzers: {directory}")
        {
            private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.Ordinal);

            public Assembly LoadFile(string path)
            {
                lock (_loaded)
                {
                    if (!_loaded.TryGetValue(path, out var assembly))
                        _loaded[path] = assembly = HostAssembly(System.Reflection.Metadata.MetadataReader.GetAssemblyName(path)) ?? LoadFromAssemblyPath(path);
                    return assembly;
                }
            }

            protected override Assembly? Load(AssemblyName name)
            {
                if (HostAssembly(name) is { } host) return host;
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? LoadFile(candidate) : null;
            }

            /// <summary>
            /// The host's copy when it has one (Microsoft.CodeAnalysis*, System.*), as csc's loader prefers the compiler's
            /// context. Not the assemblies csharpc links for its own use (Microsoft.Interop.SourceGeneration, the JSON
            /// generator): the SDK's other generators of another .NET (the net8.0 targeting pack's ComInterfaceGenerator ...)
            /// must bind to the copy next to them, not to this one (TypeLoadException on the newer library).
            /// </summary>
            private static Assembly? HostAssembly(AssemblyName name)
            {
                if (name.Name is null) return null;
                if (!name.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) && !IsFrameworkAssembly(name.Name)) return null;
                foreach (var loaded in Default.Assemblies)
                    if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)) return loaded;
                try { return Default.LoadFromAssemblyName(new AssemblyName(name.Name)); }
                catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { return null; }
            }
        }
    }

    /// <summary>An /additionalfile, read the way csc reads it (BOM-detected, UTF-8 by default).</summary>
    private sealed class AdditionalFile(string path) : AdditionalText
    {
        private SourceText? _text;
        public override string Path => path;

        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            if (_text is not null) return _text;
            try
            {
                using var stream = File.OpenRead(path);
                return _text = SourceText.From(stream, checksumAlgorithm: SourceHashAlgorithm.Sha256);
            }
            catch (IOException) { return null; }
        }
    }

    /// <summary>
    /// The options generators read (build_property.* globals, build_metadata.* per file, editorconfig keys),
    /// from the /analyzerconfig files as csc combines them (AnalyzerConfigSet).
    /// </summary>
    private sealed class ConfigOptions : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigSet _set;
        private readonly ConcurrentDictionary<string, AnalyzerConfigOptions> _byPath = new(StringComparer.Ordinal);

        private ConfigOptions(AnalyzerConfigSet set)
        {
            _set = set;
            GlobalOptions = new Options(set.GlobalConfigOptions.AnalyzerOptions);
        }

        public static ConfigOptions Create(IEnumerable<string> paths, List<Diagnostic> diagnostics)
        {
            List<AnalyzerConfig> configs = [];
            foreach (var path in paths)
            {
                try { configs.Add(AnalyzerConfig.Parse(SourceText.From(File.ReadAllText(path)), path)); }
                catch (IOException e) { diagnostics.Add(Error("CS8700", $"Could not read analyzer config file '{path}': {e.Message}")); }
            }
            var set = AnalyzerConfigSet.Create(configs, out var configDiagnostics);
            diagnostics.AddRange(configDiagnostics);
            return new ConfigOptions(set);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => For(tree.FilePath);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => For(textFile.Path);

        private AnalyzerConfigOptions For(string path) =>
            _byPath.GetOrAdd(path, p => new Options(_set.GetOptionsForSourcePath(p).AnalyzerOptions));

        private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
            public override IEnumerable<string> Keys => values.Keys;
        }
    }
}
