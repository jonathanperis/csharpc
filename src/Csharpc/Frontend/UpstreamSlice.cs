namespace Csharpc.Frontend;

/// <summary>
/// The System.Private.CoreLib types served from the framework's own IL (ADR 0023, docs/21-ownership.md Part B). The build cuts what the types
/// listed in src/Csharpc/upstream-types/ need out of the shared framework's CoreLib (tools/il-first/slicer) into CsharpcUpstream.dll and writes
/// CsharpcUpstream.map next to it; both ship beside the compiler and every compilation reads the map: a line <c>real=slice</c> says that the
/// CoreLib type <c>real</c> (metadata name, nested with '+') is served by the slice's type <c>slice</c> instead of by a corelib shadow, which must
/// not exist. <c>CSHARPC_UPSTREAM_MAP=a.map[,b.map]</c> adds maps of slices cut by tools/il-first/slicer/slice.sh (<c>-r slice.dll</c>) for
/// measuring a type before it joins the list; the build itself does not use it.
/// </summary>
public static class UpstreamSlice
{
    private static IReadOnlyList<(string Real, string Slice)>? s_entries;

    private static IReadOnlyDictionary<string, List<string>>? s_members;

    /// <summary>The map the build wrote next to the compiler.</summary>
    private static string ShippedMap => Path.Combine(AppContext.BaseDirectory, "CsharpcUpstream.map");

    /// <summary>The slice assembly of the shipped map (an IL assembly of every compilation), when there is one.</summary>
    public static IReadOnlyList<string> ShippedAssemblies => File.Exists(ShippedMap) ? [Path.ChangeExtension(ShippedMap, ".dll")] : [];

    /// <summary>The (real type, slice type) pairs of the shipped map and of CSHARPC_UPSTREAM_MAP.</summary>
    public static IReadOnlyList<(string Real, string Slice)> Entries => s_entries ??= Read();

    /// <summary>
    /// Built-in types (System.Char, System.Int32...) -> the slice classes that carry their implementations of interfaces the reference pack does not
    /// declare (<c>members:System.Char=CsharpcCoreLib.Upstream.System.CharInterfaceMembers</c>, issue #221). CoreLib implements its internal
    /// static abstract interfaces (IUtfChar&lt;T&gt;, IBinaryIntegerParseAndFormatInfo&lt;T&gt;...) on the primitives; a slice generic over them calls
    /// these through constrained calls. A class's static method "System.IUtfChar`1.CastFrom" implements <c>IUtfChar&lt;T&gt;.CastFrom</c> for that built-in type.
    /// </summary>
    public static IReadOnlyDictionary<string, List<string>> MemberClasses
    {
        get
        {
            _ = Entries;
            return s_members!;
        }
    }

    /// <summary>Whether the slice serves this CoreLib type (a hook that special-cases the shadow's representation must stand down).</summary>
    public static bool Serves(string real) => Entries.Any(e => e.Real == real);

    /// <summary>Part of the cache keys that depend on which shadows are mapped.</summary>
    public static string Key => Entries.Count == 0 && MemberClasses.Count == 0 ? "" : "upstream:" + string.Join(",", Entries.Select(e => e.Real)) + ";" + string.Join(",", MemberClasses.Keys);

    /// <summary>The namespace slice types live under (tools/il-first/slicer --prefix): "CsharpcCoreLib.Upstream.System.Text" is System.Text.</summary>
    public const string Prefix = "CsharpcCoreLib.Upstream";

    public static string RealNamespace(string ns) =>
        ns.StartsWith(Prefix + ".", StringComparison.Ordinal) ? ns[(Prefix.Length + 1)..] : ns == Prefix ? "" : ns;

    private static List<(string, string)> Read()
    {
        var entries = new List<(string, string)>();
        var members = new Dictionary<string, List<string>>();
        var maps = (Environment.GetEnvironmentVariable("CSHARPC_UPSTREAM_MAP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (File.Exists(ShippedMap)) maps.Insert(0, ShippedMap);
        foreach (var path in maps)
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.IndexOf('=') is not (var eq and > 0)) continue;
                if (line.StartsWith(MembersPrefix, StringComparison.Ordinal))
                {
                    var real = line[MembersPrefix.Length..eq];
                    if (!members.TryGetValue(real, out var classes)) members[real] = classes = [];
                    classes.Add(line[(eq + 1)..]);
                }
                else entries.Add((line[..eq], line[(eq + 1)..]));
            }
        s_members = members;
        return entries;
    }

    private const string MembersPrefix = "members:";
}
