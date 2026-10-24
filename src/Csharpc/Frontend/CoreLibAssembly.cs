using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpc.Frontend;

/// <summary>
/// The corelib compiled to IL once instead of parsed and bound into every compilation
/// (docs/adr/0015-corelib-il.md). The assembly is built on first use by Roslyn from the shipped corelib
/// sources and cached in the csharpc cache directory, keyed on everything that decides its IL: the
/// sources, the preprocessor symbols they test, Roslyn's version, the reference assemblies and the
/// format below. Programs reference it like an IL assembly (ADR 0010): codegen lowers its method
/// bodies from IL as the program reaches them, and CoreLib.Map redirects to its shadow types by the
/// index written next to it.
/// </summary>
public static partial class CoreLibAssembly
{
    public const string Name = "CsharpcCoreLib";

    /// <summary>Bump when the way the assembly is compiled changes (options, emit settings, index format).</summary>
    private const int Format = 2;

    /// <summary>The cached assembly and its shadow index.</summary>
    public sealed record Built(string Path, IReadOnlyList<ShadowDeclaration> Shadows, IReadOnlyList<RuntimeExport> Exports);

    /// <summary>A method with [RuntimeExport] (ModuleCodeGen.RuntimeExports.cs): its type's metadata name in CsharpcCoreLib, and its name.</summary>
    public readonly record struct RuntimeExport(string Type, string Method);

    /// <summary>A [Shadows] or [ShadowMembers] declaration: the real BCL type and the shadow's metadata name.</summary>
    public readonly record struct ShadowDeclaration(bool MembersOnly, string Real, string Shadow);

    /// <summary>The corelib of the current compilation when it is an assembly (null: parsed from source).</summary>
    public static Built? Current { get; private set; }

    /// <summary>
    /// The corelib assembly for these preprocessor symbols (the corelib's parse options today: CSHARPC, the
    /// program's symbols and the target's), building it if it isn't cached yet.
    /// </summary>
    public static Built Ensure(IReadOnlyList<(string File, string TreePath)> files, IEnumerable<string> symbols,
        IReadOnlyList<MetadataReference> references, TextWriter? log)
    {
        var cache = Driver.Toolchain.CacheDirectory;
        var environment = new List<string>
        {
            $"corelib-il {Format}",
            typeof(CSharpCompilation).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?",
        };
        environment.AddRange(references.Select(r => r.Display ?? ""));
        var allSymbols = symbols.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        // Fast path: a stamp of the sources' sizes and modification times (and every symbol) names the
        // content key it was last computed for, so a compile reads ~170 file attributes, not 4 MB of C#.
        // Any change to a file's size or time falls back to hashing the contents, never to a stale assembly.
        var stamp = Hash([.. environment, string.Join(";", allSymbols),
            .. files.SelectMany(f => new FileInfo(f.File) is var info ? [f.TreePath, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}"] : Array.Empty<string>())]);
        var stampFile = Path.Combine(cache, $"corelib-stamp-{stamp}.txt");
        if (TryReadText(stampFile) is { Length: 16 } stamped && Cached(stamped) is { } fast) return Current = fast;

        var texts = new string[files.Count];
        Parallel.For(0, files.Count, Driver.Parallelism.Options, i => texts[i] = File.ReadAllText(files[i].File));

        // Only the symbols the corelib tests change its code; the program's other symbols must not split the cache.
        var tested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
            foreach (Match directive in DirectivePattern().Matches(text))
                foreach (Match identifier in IdentifierPattern().Matches(directive.Groups[1].Value))
                    tested.Add(identifier.Value);
        var defines = allSymbols.Where(tested.Contains).ToArray();
        var key = Hash([.. environment, string.Join(";", defines), .. files.SelectMany((f, i) => new[] { f.TreePath, texts[i] })]);
        var built = Cached(key) ?? Build(key);
        try
        {
            File.WriteAllText(stampFile + $".{Environment.ProcessId}.tmp", key);
            File.Move(stampFile + $".{Environment.ProcessId}.tmp", stampFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Current = built;

        Built? Cached(string key)
        {
            var dll = Path.Combine(cache, $"corelib-{key}.dll");
            var index = Path.ChangeExtension(dll, ".shadows");
            return File.Exists(dll) && File.Exists(index) && ReadIndex(index) is { } read ? new Built(dll, read.Shadows, read.Exports) : null;
        }

        Built Build(string key) => Compile(Path.Combine(cache, $"corelib-{key}.dll"), files, texts, defines, references, log);
    }

    private static string Hash(IEnumerable<string> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }

    private static string? TryReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
    }

    private static Built Compile(string dll, IReadOnlyList<(string File, string TreePath)> files, string[] texts, string[] defines,
        IReadOnlyList<MetadataReference> references, TextWriter? log)
    {
        var index = Path.ChangeExtension(dll, ".shadows");

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular, defines);
        var trees = new SyntaxTree[files.Count];
        Parallel.For(0, files.Count, Driver.Parallelism.Options,
            i => trees[i] = CSharpSyntaxTree.ParseText(texts[i], parseOptions, path: files[i].TreePath, encoding: Encoding.UTF8));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            deterministic: true,
            concurrentBuild: Driver.Parallelism.Threads > 1);
        var compilation = CSharpCompilation.Create(Name, trees, references, options);
        using var pe = new MemoryStream();
        var result = compilation.Emit(pe);
        if (!result.Success)
            throw new InvalidOperationException("the corelib does not compile (a csharpc bug):" + Environment.NewLine
                + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));

        var shadows = new List<ShadowDeclaration>();
        var exports = new List<RuntimeExport>();
        foreach (var tree in trees)
        {
            ScanShadows(tree.GetRoot(), "", shadows);
            ScanRuntimeExports(tree.GetRoot(), exports);
        }
        // Written next to each other under temporary names; the assembly last, so its presence means both are complete.
        var suffix = $".{Environment.ProcessId}.tmp";
        File.WriteAllLines(index + suffix, [.. shadows.Select(s => $"{(s.MembersOnly ? "M" : "S")}\t{s.Real}\t{s.Shadow}"),
            .. exports.Select(e => $"E\t{e.Type}\t{e.Method}")]);
        File.Move(index + suffix, index, overwrite: true);
        File.WriteAllBytes(dll + suffix, pe.ToArray());
        File.Move(dll + suffix, dll, overwrite: true);
        log?.WriteLine($"  corelib: compiled to {dll} ({pe.Length / 1024} KiB, {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s)");
        return new Built(dll, shadows, exports);
    }

    private static (List<ShadowDeclaration> Shadows, List<RuntimeExport> Exports)? ReadIndex(string path)
    {
        try
        {
            List<ShadowDeclaration> shadows = [];
            List<RuntimeExport> exports = [];
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) return null;
                if (parts[0] == "E") exports.Add(new RuntimeExport(parts[1], parts[2]));
                else shadows.Add(new ShadowDeclaration(parts[0] == "M", parts[1], parts[2]));
            }
            return (shadows, exports);
        }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Methods with [RuntimeExport] in the top-level types of the CsharpcCoreLib namespace, the ones
    /// ModuleCodeGen.CollectRuntimeExports looks at: indexed so a compile doesn't read every member's
    /// attributes from metadata (~80 ms).
    /// </summary>
    private static void ScanRuntimeExports(SyntaxNode root, List<RuntimeExport> into)
    {
        foreach (var ns in root.ChildNodes().OfType<BaseNamespaceDeclarationSyntax>().Where(n => n.Name.ToString() == Name))
            foreach (var type in ns.Members.OfType<TypeDeclarationSyntax>())
            {
                var metadataName = type.Identifier.ValueText + (type.TypeParameterList is { } tps ? "`" + tps.Parameters.Count : "");
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                    if (method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is "RuntimeExport" or "RuntimeExportAttribute")
                        && !into.Contains(new RuntimeExport(metadataName, method.Identifier.ValueText)))
                        into.Add(new RuntimeExport(metadataName, method.Identifier.ValueText));
            }
    }

    /// <summary>[Shadows]/[ShadowMembers] declarations, from syntax (CodeGen/CoreLib.cs reads the same for source corelibs).</summary>
    public static void ScanShadows(SyntaxNode node, string prefix, List<ShadowDeclaration> into)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    ScanShadows(ns, prefix + ns.Name + ".", into);
                    break;
                case BaseTypeDeclarationSyntax type:
                {
                    var arity = type is TypeDeclarationSyntax { TypeParameterList: { } tps } ? tps.Parameters.Count : 0;
                    var metadataName = prefix + type.Identifier.ValueText + (arity > 0 ? "`" + arity : "");
                    foreach (var attribute in type.AttributeLists.SelectMany(l => l.Attributes))
                    {
                        var name = attribute.Name.ToString();
                        if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax { Token.Value: string real })
                            continue;
                        if (name is "Shadows" or "ShadowsAttribute") into.Add(new ShadowDeclaration(false, real, metadataName));
                        else if (name is "ShadowMembers" or "ShadowMembersAttribute") into.Add(new ShadowDeclaration(true, real, metadataName));
                    }
                    if (type is TypeDeclarationSyntax nested) ScanShadows(nested, metadataName + "+", into);
                    break;
                }
            }
        }
    }

    [GeneratedRegex(@"^[ \t]*#[ \t]*(?:if|elif)\b(.*)$", RegexOptions.Multiline)]
    private static partial Regex DirectivePattern();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex IdentifierPattern();
}
