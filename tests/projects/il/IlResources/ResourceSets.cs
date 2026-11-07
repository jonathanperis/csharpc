using System.Collections;
using System.Globalization;
using System.Resources;

namespace IlResources;

// ResourceManager.GetResourceSet / ResourceSet / GetStream over the library's embedded resources, from IL: Strings.resx
// (strings) and Blobs.resources (a .resources file written with ResourceWriter: primitives by type code, a byte array and a Stream).
public static class ResourceSets
{
    private static string Show(object? o) => o switch
    {
        null => "null",
        byte[] b => "byte[" + string.Join(",", b) + "]",
        string s => "\"" + s + "\"",
        Stream st => st.GetType().Name,
        _ => o.GetType().Name + ":" + Convert.ToString(o, CultureInfo.InvariantCulture),
    };

    private static string Attempt(Func<object?> action)
    {
        try { return Show(action()); }
        catch (Exception e) { return $"{e.GetType().Name}: {e.Message}"; }
    }

    private static List<string> Entries(ResourceSet set)
    {
        var entries = new List<string>();
        foreach (DictionaryEntry e in set) entries.Add(e.Key + "=" + Show(e.Value));
        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    public static IEnumerable<string> Describe()
    {
        var strings = new ResourceManager(typeof(Strings));
        var blobs = new ResourceManager("IlResources.Blobs", typeof(ResourceSets).Assembly);

        var set = strings.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        yield return set.GetType().Name + " " + string.Join(" | ", Entries(set));
        yield return Show(set.GetString("greeting")) + " " + Show(set.GetString("greeting", true)) + " " + Show(set.GetObject("Missing"));
        yield return Attempt(() => set.GetString(null!));
        yield return ReferenceEquals(set, strings.GetResourceSet(CultureInfo.GetCultureInfo("fr-FR"), false, true)) + " "
            + (strings.GetResourceSet(CultureInfo.GetCultureInfo("fr-FR"), true, false) is null);

        var typed = blobs.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        yield return string.Join(" | ", Entries(typed));
        foreach (var name in new[] { "Text", "Bytes", "Data", "Count", "Nothing" })
            yield return name + ": " + Attempt(() => typed.GetString(name));
        yield return Show(typed.GetObject("Count")) + " " + ReferenceEquals(typed.GetObject("Count"), typed.GetObject("Count"));
        yield return Attempt(() => typed.GetString("Count"));
        yield return Show(blobs.GetObject("Data")) + " " + (typed.GetObject("Data") is UnmanagedMemoryStream);

        foreach (var name in new[] { "Data", "Bytes", "Text", "Missing" })
            yield return "stream " + name + ": " + Attempt(() =>
            {
                using var stream = blobs.GetStream(name);
                if (stream is null) return null;
                var bytes = new byte[stream.Length];
                var read = stream.Read(bytes, 0, bytes.Length);
                return $"{stream.GetType().Name} {stream.Length} {read} [{string.Join(",", bytes)}] {stream.CanWrite}";
            });
        yield return Attempt(() => strings.GetStream("Greeting"));

        typed.Dispose();
        yield return Attempt(() => typed.GetString("Text"));
        yield return Attempt(() => blobs.GetString("Text"));
        blobs.ReleaseAllResources();
        yield return Attempt(() => blobs.GetString("Text"));
    }
}
