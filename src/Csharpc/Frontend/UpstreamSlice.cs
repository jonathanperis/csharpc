namespace Csharpc.Frontend;

/// <summary>
/// Part B spike of issue #191 (mixed-mode CoreLib), opt-in and off by default: <c>CSHARPC_UPSTREAM_MAP=a.map[,b.map]</c> names map files
/// written by tools/il-first/slicer next to a slice assembly (<c>-r CsharpcUpstreamCore.dll</c>). A line <c>real=slice</c> says that the
/// System.Private.CoreLib type <c>real</c> (metadata name, nested with '+') is served by the slice's type <c>slice</c>, cut from the
/// framework's IL, instead of by a corelib shadow; the shadow of that type (and its nested types) is dropped. Everything else of the
/// corelib stays. See docs/21-ownership.md and the spike report on issue #191.
/// </summary>
public static class UpstreamSlice
{
    private static IReadOnlyList<(string Real, string Slice)>? s_entries;

    /// <summary>The (real type, slice type) pairs, empty unless CSHARPC_UPSTREAM_MAP is set.</summary>
    public static IReadOnlyList<(string Real, string Slice)> Entries => s_entries ??= Read();

    /// <summary>Whether the slice serves this CoreLib type (a hook that special-cases the shadow's representation must stand down).</summary>
    public static bool Serves(string real) => Entries.Any(e => e.Real == real);

    /// <summary>Part of the cache keys that depend on which shadows are mapped.</summary>
    public static string Key => Entries.Count == 0 ? "" : "upstream:" + string.Join(",", Entries.Select(e => e.Real));

    /// <summary>The namespace slice types live under (tools/il-first/slicer --prefix): "CsharpcCoreLib.Upstream.System.Text" is System.Text.</summary>
    public const string Prefix = "CsharpcCoreLib.Upstream";

    public static string RealNamespace(string ns) =>
        ns.StartsWith(Prefix + ".", StringComparison.Ordinal) ? ns[(Prefix.Length + 1)..] : ns == Prefix ? "" : ns;

    private static List<(string, string)> Read()
    {
        var entries = new List<(string, string)>();
        foreach (var path in (Environment.GetEnvironmentVariable("CSHARPC_UPSTREAM_MAP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var line in File.ReadAllLines(path))
                if (line.IndexOf('=') is var eq and > 0)
                    entries.Add((line[..eq], line[(eq + 1)..]));
        return entries;
    }
}
