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

    public static string ByType()
    {
        using var stream = Self.GetManifestResourceStream(typeof(RawResources), "table.dat");
        return stream is null ? "null" : $"by type: {stream.Length}";
    }
}
