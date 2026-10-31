using System.Reflection;

namespace IlResources;

// Raw manifest resources, as System.Text.Encoding.CodePages reads its codepages.nlp table.
public static class RawResources
{
    private static Assembly Self => typeof(RawResources).Assembly;

    public static string[] Names() => Self.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public static string Describe(string name)
    {
        using var stream = Self.GetManifestResourceStream(name);
        if (stream is null) return $"{name}: null";
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return $"{name}: {stream.GetType().Name} length {stream.Length} canRead {stream.CanRead} canWrite {stream.CanWrite} canSeek {stream.CanSeek} text '{(name.EndsWith(".dat") ? System.Text.Encoding.ASCII.GetString(bytes).ReplaceLineEndings("|") : $"0x{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}")}'";
    }

    // Module and reference metadata of an IL assembly (csharpc keys its caches on its module's MVID).
    public static string ModuleInfo()
    {
        var module = Self.ManifestModule;
        return $"module {module.ScopeName} {module.ModuleVersionId != Guid.Empty} {ReferenceEquals(module, Self.ManifestModule)} {module.Assembly == Self}";
    }

    public static string References() =>
        string.Join(";", Self.GetReferencedAssemblies().Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));

    // A .resources stream through ResourceReader, as csharpc's front end reads the IL assemblies' strings.
    public static IEnumerable<string> ReadStrings()
    {
        using var stream = Self.GetManifestResourceStream("IlResources.Strings.resources")!;
        using var reader = new System.Resources.ResourceReader(stream);
        var keys = new List<string>();
        var e = reader.GetEnumerator();
        while (e.MoveNext()) keys.Add((string)e.Key);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            reader.GetResourceData(key, out var type, out var data);
            using var value = new BinaryReader(new MemoryStream(data));
            yield return $"{key}: {type} {data.Length} [{value.ReadString()}]";
        }
        var values = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in reader) values.Add($"{entry.Key}={entry.Value}");
        values.Sort(StringComparer.Ordinal);
        yield return string.Join(" | ", values);
        string? missing;
        try { reader.GetResourceData("NoSuchKey", out missing, out _); }
        catch (ArgumentException ex) { missing = ex.GetType().Name; }
        yield return $"missing: {missing}";
        string invalid = "accepted";
        try { _ = new System.Resources.ResourceReader(new MemoryStream(new byte[16])); }
        catch (ArgumentException ex) { invalid = ex.Message; }
        yield return $"invalid: {invalid}";
    }

    public static string ByType()
    {
        using var stream = Self.GetManifestResourceStream(typeof(RawResources), "table.dat");
        return stream is null ? "null" : $"by type: {stream.Length}";
    }
}
