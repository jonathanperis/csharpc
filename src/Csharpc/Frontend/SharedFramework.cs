using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Csharpc.Frontend;

/// <summary>
/// The implementation assemblies of the .NET shared framework (shared/Microsoft.NETCore.App/&lt;ver&gt;),
/// read with System.Reflection.Metadata. User code binds against reference assemblies, where e.g.
/// List&lt;T&gt; lives in System.Collections.dll; at run time .NET resolves it through type forwards to
/// System.Private.CoreLib, and that is what Type.Assembly reports. Following the same forwards at
/// compile time gives csharpc the identical answer (and works when csharpc itself runs as NativeAOT).
/// </summary>
public static class SharedFramework
{
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, MetadataReader?> s_readers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string, string), string?> s_cache = [];
    private static string? s_directory;
    private static bool s_searched;

    /// <summary>Override with CSHARPC_SHARED_FRAMEWORK=&lt;directory containing System.Private.CoreLib.dll&gt;.</summary>
    public static string? Directory
    {
        get
        {
            lock (s_lock)
            {
                if (!s_searched) { s_directory = Find(); s_searched = true; }
                return s_directory;
            }
        }
    }

    /// <summary>
    /// Assembly.FullName of the assembly that defines the top-level type <paramref name="ns"/>.<paramref name="name"/>
    /// (metadata name, e.g. "List`1"), starting from the reference assembly Roslyn bound it in and
    /// following type forwards; null when it can't be found.
    /// </summary>
    public static string? DefiningAssembly(string assemblyName, string ns, string name)
    {
        lock (s_lock)
        {
            if (s_cache.TryGetValue((assemblyName, ns, name), out var known)) return known;
            return s_cache[(assemblyName, ns, name)] = Resolve(assemblyName, ns, name, depth: 0);
        }
    }

    private static string? Resolve(string assemblyName, string ns, string name, int depth)
    {
        if (depth > 8 || Reader(assemblyName) is not { } reader) return null;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (type.GetDeclaringType().IsNil && reader.StringComparer.Equals(type.Name, name) && reader.StringComparer.Equals(type.Namespace, ns))
                return reader.GetAssemblyDefinition().GetAssemblyName().FullName;
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!reader.StringComparer.Equals(exported.Name, name) || !reader.StringComparer.Equals(exported.Namespace, ns)) continue;
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) return null;
            var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            return Resolve(reader.GetString(target.Name), ns, name, depth + 1);
        }
        return null;
    }

    private static MetadataReader? Reader(string assemblyName)
    {
        if (s_readers.TryGetValue(assemblyName, out var cached)) return cached;
        MetadataReader? reader = null;
        if (Directory is { } dir && Path.Combine(dir, assemblyName + ".dll") is var path && File.Exists(path))
        {
            // Kept open for the compiler's lifetime: a handful of small framework assemblies.
            var pe = new PEReader(File.OpenRead(path));
            if (pe.HasMetadata) reader = pe.GetMetadataReader();
        }
        return s_readers[assemblyName] = reader;
    }

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_SHARED_FRAMEWORK") is { Length: > 0 } explicitDir) return explicitDir;
        // Running on the JIT: the framework csharpc itself runs on is .NET 11's.
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (runtimeDir.Length > 0 && File.Exists(Path.Combine(runtimeDir, "System.Private.CoreLib.dll"))
            && Path.GetFileName(Path.TrimEndingDirectorySeparator(runtimeDir)).StartsWith("11.", StringComparison.Ordinal))
            return runtimeDir;
        IEnumerable<string?> roots =
        [
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet11"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            "/usr/share/dotnet", "/usr/lib/dotnet", "/usr/local/share/dotnet",
            Environment.GetEnvironmentVariable("ProgramFiles") is { } pf ? Path.Combine(pf, "dotnet") : null,
        ];
        return roots.Where(r => r is not null && System.IO.Directory.Exists(Path.Combine(r, "shared", "Microsoft.NETCore.App")))
            .SelectMany(r => System.IO.Directory.EnumerateDirectories(Path.Combine(r!, "shared", "Microsoft.NETCore.App")))
            .Where(d => Path.GetFileName(d).StartsWith("11.", StringComparison.Ordinal) && File.Exists(Path.Combine(d, "System.Private.CoreLib.dll")))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
