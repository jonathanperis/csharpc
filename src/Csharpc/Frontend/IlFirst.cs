using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Csharpc.Frontend;

/// <summary>
/// The opt-in "framework IL first" switch (<c>CSHARPC_IL_FIRST=A,B</c> or <c>--il-first A,B</c>; docs/adr/0023-framework-il-first.md):
/// for the listed shared-framework assemblies the corelib's shadows are not mapped and the types they
/// define no longer count as "implemented by csharpc", so FrameworkIl compiles the assembly from the shared framework's IL
/// when the program uses it. Never the default: without the switch nothing in this class has an effect. It generalizes
/// the Windows native-TLS switch, which drops a hand-picked family of shadows instead of whole assemblies.
/// </summary>
public static class IlFirst
{
    private static string[] s_assemblies = [];
    private static HashSet<string> s_types = new(StringComparer.Ordinal);

    /// <summary>The assemblies named by the switch, sorted and without ".dll".</summary>
    public static IReadOnlyList<string> Assemblies => s_assemblies;

    public static bool On => s_assemblies.Length > 0;

    /// <summary>Part of the cache keys that depend on which shadows are mapped (the corelib assembly, the framework index).</summary>
    public static string Key => On ? "il-first:" + string.Join(",", s_assemblies) : "";

    /// <summary>
    /// Set once by the driver, before the corelib index is read or built: the driver's <c>--il-first</c> names plus
    /// <c>CSHARPC_IL_FIRST</c>. Throws InvalidOperationException for a name that is no assembly of the shared framework,
    /// or one csharpc can't take as IL.
    /// </summary>
    public static void Configure(IEnumerable<string> fromOptions)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in fromOptions.Append(Environment.GetEnvironmentVariable("CSHARPC_IL_FIRST") ?? ""))
            foreach (var name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!name.Equals("none", StringComparison.OrdinalIgnoreCase))
                    names.Add(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name);
        s_assemblies = [.. names];
        s_types = new HashSet<string>(StringComparer.Ordinal);
        if (names.Count == 0) return;

        if (SharedFramework.Directory is not { } dir)
            throw new InvalidOperationException("--il-first: the .NET 11 shared framework (Microsoft.NETCore.App) was not found");
        foreach (var name in names)
        {
            if (name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("--il-first: System.Private.CoreLib is the runtime's own code (FCalls, intrinsics, the GC interface); it stays hand-implemented");
            var path = Path.Combine(dir, name + ".dll");
            if (!File.Exists(path))
                throw new InvalidOperationException($"--il-first: '{name}' is not an assembly of the shared framework ({dir})");
            var before = s_types.Count;
            using var pe = new PEReader(File.OpenRead(path));
            var md = pe.GetMetadataReader();
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                if (!t.GetDeclaringType().IsNil) continue;
                var ns = md.GetString(t.Namespace);
                if (ns.Length == 0 && md.GetString(t.Name) == "<Module>") continue;
                s_types.Add(ns.Length == 0 ? md.GetString(t.Name) : ns + "." + md.GetString(t.Name));
            }
            // A facade (System.Collections, System.Runtime) defines nothing: its types are CoreLib's, which stay shadows.
            if (s_types.Count == before)
                throw new InvalidOperationException($"--il-first: '{name}' is a facade of the runtime's CoreLib and defines no types of its own; those stay hand-implemented");
        }
    }

    /// <summary>
    /// Whether a corelib shadow of this real type is dropped: it is a type (or a nested type of one) that a listed assembly
    /// defines. <paramref name="real"/> is the metadata name the shadow attribute gives ("System.Collections.Generic.List`1",
    /// "System.Text.Json.Utf8JsonReader+Enumerator").
    /// </summary>
    public static bool Excluded(string real) => On && s_types.Contains(real.Split('+')[0]);
}
