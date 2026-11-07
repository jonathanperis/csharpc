using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend.Il;

/// <summary>
/// A program given as its main assembly (`csharpc app.dll`): what csc built, plus what the .NET host
/// reads next to it. Inside the dotnet pipeline this is csharpc's input (ADR 0016): every method, Main
/// included, is lowered from IL through the IL front-end (ADR 0010), and the Roslyn compilation has no
/// sources of the program.
/// </summary>
public sealed record IlProgram
{
    public required string Path { get; init; }
    /// <summary>The assembly's simple name: the default output name, Assembly.GetEntryAssembly().GetName().Name.</summary>
    public required string Name { get; init; }
    /// <summary>The CLI header's EntryPoint (a MethodDef token): for async and top-level Mains, the synthesized sync wrapper.</summary>
    public required int EntryPointToken { get; init; }
    /// <summary>`app.runtimeconfig.json`'s configProperties, as the host hands them to AppContext (strings).</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> RuntimeConfig { get; init; }
    /// <summary>
    /// `app.runtimeconfig.json`'s frameworks other than Microsoft.NETCore.App, which is always there: the
    /// shared frameworks the host loads the program's framework assemblies from (Microsoft.AspNetCore.App
    /// for a web app), as --framework names them.
    /// </summary>
    public required IReadOnlyList<string> Frameworks { get; init; }

    /// <summary>
    /// The native libraries deps.json lists for the target's runtime identifier (`runtimeTargets` of assetType native,
    /// runtimes/&lt;rid&gt;/native next to the assembly): copied next to the executable, where P/Invoke probing finds them.
    /// </summary>
    public IReadOnlyList<string> NativeAssets { get; init; } = [];

    /// <param name="runtimeIdentifier">The target's RID: which `runtimeTargets` of deps.json apply (the host picks the same by its own RID).</param>
    /// <summary>Reads the assembly's identity and entry point; throws <see cref="InvalidOperationException"/> with a user-facing message.</summary>
    public static IlProgram Read(string path, string? runtimeIdentifier = null)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"'{path}' not found");
        using var stream = File.OpenRead(path);
        PEReader pe;
        try { pe = new PEReader(stream); }
        catch (BadImageFormatException) { throw new InvalidOperationException($"'{path}' is not a .NET assembly"); }
        using (pe)
        {
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } cor)
                throw new InvalidOperationException($"'{path}' is not a .NET assembly");
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new InvalidOperationException($"'{path}' is a module, not an assembly");
            if ((cor.Flags & CorFlags.NativeEntryPoint) != 0)
                throw new InvalidOperationException($"'{path}' has a native entry point (mixed-mode assembly); not supported by csharpc");
            var token = cor.EntryPointTokenOrRelativeVirtualAddress;
            if (token == 0 || MetadataTokens.EntityHandle(token).Kind != HandleKind.MethodDefinition)
                throw new InvalidOperationException($"'{path}' has no entry point: it is a library, not a program (reference it with -r instead)");
            var runtimeConfig = System.IO.Path.ChangeExtension(path, ".runtimeconfig.json");
            var (managedTargets, nativeTargets) = ReadRuntimeTargets(path, runtimeIdentifier);
            return new IlProgram
            {
                Path = path,
                Name = reader.GetString(reader.GetAssemblyDefinition().Name),
                EntryPointToken = token,
                RuntimeConfig = ReadRuntimeConfig(runtimeConfig),
                Frameworks = ReadFrameworks(runtimeConfig),
                AppLocalAssemblies = AppLocal(path, reader, managedTargets),
                NativeAssets = nativeTargets,
            };
        }
    }

    /// <summary>
    /// The assemblies the program references that sit next to it (ProjectReference and package outputs that
    /// `dotnet build` copies there, transitively): the host loads those from the app directory, so they are
    /// compiled from IL like `-r` references. A framework-dependent app has no framework assemblies there;
    /// a self-contained (or RID-specific) one does, and those are not references: csharpc binds the
    /// framework it targets and compiles its own corelib, as NativeAOT gets its framework from the runtime pack.
    /// </summary>
    public required IReadOnlyList<string> AppLocalAssemblies { get; init; }

    /// <summary>
    /// deps.json's `runtimeTargets`: the platform-specific assets of its packages (runtimes/&lt;rid&gt;/lib/&lt;tfm&gt;/X.dll,
    /// runtimes/&lt;rid&gt;/native/libX.so). For the target's RID and the ones it falls back to (linux-x64, linux, unix, any),
    /// the best-matching managed assembly of each name replaces the portable lib/ one, as the host loads it, and the
    /// native libraries are the ones to deploy.
    /// </summary>
    private static (Dictionary<string, string> Managed, List<string> Native) ReadRuntimeTargets(string path, string? runtimeIdentifier)
    {
        Dictionary<string, string> managed = new(StringComparer.OrdinalIgnoreCase);
        List<string> native = [];
        var deps = System.IO.Path.ChangeExtension(path, ".deps.json");
        if (runtimeIdentifier is null || !File.Exists(deps)) return (managed, native);
        var directory = System.IO.Path.GetDirectoryName(path)!;
        var chain = Csharpc.Driver.RuntimeIdentifiers.Fallbacks(runtimeIdentifier);
        Dictionary<string, int> bestRank = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(deps), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object) return (managed, native);
            foreach (var target in targets.EnumerateObject())
                foreach (var library in target.Value.EnumerateObject())
                {
                    if (library.Value.ValueKind != JsonValueKind.Object || !library.Value.TryGetProperty("runtimeTargets", out var runtimeTargets)
                        || runtimeTargets.ValueKind != JsonValueKind.Object) continue;
                    foreach (var asset in runtimeTargets.EnumerateObject())
                    {
                        if (asset.Value.ValueKind != JsonValueKind.Object || !asset.Value.TryGetProperty("rid", out var ridElement)
                            || !asset.Value.TryGetProperty("assetType", out var typeElement)) continue;
                        var rank = chain.ToList().IndexOf(ridElement.GetString() ?? "");
                        if (rank < 0) continue;
                        var relative = asset.Value.TryGetProperty("localPath", out var local) && local.GetString() is { Length: > 0 } l ? l : asset.Name;
                        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, relative));
                        if (!File.Exists(file)) continue;
                        switch (typeElement.GetString())
                        {
                            case "runtime":
                                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                                if (bestRank.TryGetValue(name, out var existing) && existing <= rank) break;
                                bestRank[name] = rank;
                                managed[name] = file;
                                break;
                            case "native":
                                if (!native.Contains(file)) native.Add(file);
                                break;
                        }
                    }
                }
        }
        catch (JsonException) { /* a malformed deps.json says nothing, as for the framework assemblies */ }
        return (managed, native);
    }

    private static List<string> AppLocal(string path, MetadataReader main, Dictionary<string, string> runtimeTargets)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        List<string> found = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFileNameWithoutExtension(path) };
        // Framework assemblies are skipped as if already seen.
        foreach (var framework in FrameworkAssemblies(path, directory)) seen.Add(framework);
        var queue = new Queue<MetadataReader>();
        queue.Enqueue(main);
        List<PEReader> opened = [];
        try
        {
            while (queue.TryDequeue(out var reader))
                foreach (var handle in reader.AssemblyReferences)
                {
                    var name = reader.GetString(reader.GetAssemblyReference(handle).Name);
                    var candidate = runtimeTargets.TryGetValue(name, out var platformSpecific) ? platformSpecific : System.IO.Path.Combine(directory, name + ".dll");
                    if (!seen.Add(name) || !File.Exists(candidate)) continue;
                    found.Add(candidate);
                    var pe = new PEReader(File.OpenRead(candidate));
                    opened.Add(pe);
                    // (a file that isn't a .NET assembly is reported when the IL references are loaded)
                    try { if (pe.HasMetadata) queue.Enqueue(pe.GetMetadataReader()); }
                    catch (BadImageFormatException) { }
                }
        }
        finally { foreach (var pe in opened) pe.Dispose(); }
        return found;
    }

    /// <summary>
    /// The simple names of the framework assemblies copied next to a self-contained app: the runtime pack's
    /// entries in its deps.json (`type: runtimepack`), or, without one, the reference pack's names plus the
    /// System.Private.* implementation assemblies when the directory holds System.Private.CoreLib.dll.
    /// </summary>
    private static HashSet<string> FrameworkAssemblies(string path, string directory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deps = System.IO.Path.ChangeExtension(path, ".deps.json");
        if (File.Exists(deps))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(deps), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (document.RootElement.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Object)
                    foreach (var target in targets.EnumerateObject())
                        foreach (var library in target.Value.EnumerateObject())
                            if (library.Value.ValueKind == JsonValueKind.Object && library.Value.TryGetProperty("type", out var type) && type.GetString() == "runtimepack"
                                && library.Value.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
                                foreach (var file in runtime.EnumerateObject())
                                    names.Add(System.IO.Path.GetFileNameWithoutExtension(file.Name));
            }
            catch (JsonException) { /* a malformed deps.json says nothing; the fallback below still applies */ }
        }
        if (names.Count == 0 && File.Exists(System.IO.Path.Combine(directory, "System.Private.CoreLib.dll")))
        {
            foreach (var reference in RoslynFrontend.ReferenceAssemblies().OfType<PortableExecutableReference>())
                if (reference.FilePath is { } file) names.Add(System.IO.Path.GetFileNameWithoutExtension(file));
            foreach (var file in Directory.EnumerateFiles(directory, "System.Private.*.dll"))
                names.Add(System.IO.Path.GetFileNameWithoutExtension(file));
        }
        return names;
    }

    /// <summary>
    /// configProperties of the runtimeconfig.json the SDK writes next to the assembly. The host passes
    /// every value to the runtime as a string; JSON booleans become "true"/"false" (hostpolicy's spelling).
    /// No file: no properties (`dotnet app.dll` would refuse to start; csharpc does not need the host).
    /// </summary>
    private static List<KeyValuePair<string, string>> ReadRuntimeConfig(string path)
    {
        List<KeyValuePair<string, string>> result = [];
        if (!File.Exists(path)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options)
                || !options.TryGetProperty("configProperties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var property in properties.EnumerateObject())
                result.Add(new(property.Name, property.Value.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.String => property.Value.GetString()!,
                    _ => property.Value.GetRawText(),
                }));
        }
        catch (JsonException e) { throw new InvalidOperationException($"'{path}': {e.Message}"); }
        return result;
    }

    /// <summary>
    /// The frameworks of runtimeconfig.json by name (hostfxr reads `framework`, an object, and `frameworks`,
    /// an array), without Microsoft.NETCore.App. A self-contained app's runtimeconfig lists
    /// `includedFrameworks` instead: its framework assemblies are in the app directory.
    /// </summary>
    private static List<string> ReadFrameworks(string path)
    {
        List<string> result = [];
        if (!File.Exists(path)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options) || options.ValueKind != JsonValueKind.Object) return result;
            void Add(JsonElement framework)
            {
                if (framework.ValueKind == JsonValueKind.Object && framework.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && name.GetString() is { Length: > 0 } n && !n.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
                    && !result.Contains(n, StringComparer.OrdinalIgnoreCase))
                    result.Add(n);
            }
            if (options.TryGetProperty("framework", out var single)) Add(single);
            if (options.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
                foreach (var framework in many.EnumerateArray()) Add(framework);
        }
        catch (JsonException e) { throw new InvalidOperationException($"'{path}': {e.Message}"); }
        return result;
    }

    /// <summary>A runtimeconfig switch (System.Globalization.Invariant, ...): null when not set or not a boolean.</summary>
    public bool? Switch(string name) =>
        RuntimeConfig.LastOrDefault(kv => kv.Key == name).Value is { } value && bool.TryParse(value, out var flag) ? flag : null;
}
