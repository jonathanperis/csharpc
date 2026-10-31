using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

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

    /// <summary>Reads the assembly's identity and entry point; throws <see cref="InvalidOperationException"/> with a user-facing message.</summary>
    public static IlProgram Read(string path)
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
            return new IlProgram
            {
                Path = path,
                Name = reader.GetString(reader.GetAssemblyDefinition().Name),
                EntryPointToken = token,
                RuntimeConfig = ReadRuntimeConfig(System.IO.Path.ChangeExtension(path, ".runtimeconfig.json")),
                AppLocalAssemblies = AppLocal(path, reader),
            };
        }
    }

    /// <summary>
    /// The assemblies the program references that sit next to it (ProjectReference and package outputs that
    /// `dotnet build` copies there, transitively): the host loads those from the app directory, so they are
    /// compiled from IL like `-r` references. Framework assemblies are not in the app directory.
    /// </summary>
    public required IReadOnlyList<string> AppLocalAssemblies { get; init; }

    private static List<string> AppLocal(string path, MetadataReader main)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        List<string> found = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFileNameWithoutExtension(path) };
        var queue = new Queue<MetadataReader>();
        queue.Enqueue(main);
        List<PEReader> opened = [];
        try
        {
            while (queue.TryDequeue(out var reader))
                foreach (var handle in reader.AssemblyReferences)
                {
                    var name = reader.GetString(reader.GetAssemblyReference(handle).Name);
                    var candidate = System.IO.Path.Combine(directory, name + ".dll");
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

    /// <summary>A runtimeconfig switch (System.Globalization.Invariant, ...): null when not set or not a boolean.</summary>
    public bool? Switch(string name) =>
        RuntimeConfig.LastOrDefault(kv => kv.Key == name).Value is { } value && bool.TryParse(value, out var flag) ? flag : null;
}
