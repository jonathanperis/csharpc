using System.Collections;
using System.Globalization;
using System.Resources;

// ResourceManager.GetResourceSet / ResourceSet / GetStream over the preserialized Strings.resx and the ResourceWriter-made Blobs.resources.
static string Show(object o) => o switch
{
    null => "null",
    byte[] b => "byte[" + string.Join(",", b) + "]",
    string s => "\"" + s.Replace("\r", "").Replace("\n", "|") + "\"",
    Stream st => st.GetType().Name,
    _ => o.GetType().Name + ":" + Convert.ToString(o, CultureInfo.InvariantCulture),
};
static void Try(string what, Action a)
{
    Console.WriteLine("-- " + what);
    try { a(); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}
static void Dump(ResourceSet set)
{
    var entries = new List<string>();
    foreach (DictionaryEntry e in set) entries.Add(e.Key + "=" + Show(e.Value));
    entries.Sort(StringComparer.Ordinal);
    foreach (var e in entries) Console.WriteLine("  " + e);
}
static ResourceManager Strings() => new ResourceManager("Repro.Strings", typeof(Marker).Assembly);
static ResourceManager Blobs() => new ResourceManager("Repro.Blobs", typeof(Marker).Assembly);

Try("the set of the neutral culture", () =>
{
    var resources = Strings();
    ResourceSet set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
    Console.WriteLine(set.GetType().Name + " " + (set is ResourceSet) + " " + (resources.ResourceSetType.Name));
    Dump(set);
    Console.WriteLine(set.GetString("Hello"));
    Console.WriteLine(set.GetString("hello") == null);
    Console.WriteLine(set.GetString("hello", true));
    Console.WriteLine(Show(set.GetObject("Count")));
    Console.WriteLine(Show(set.GetObject("Location")));
    Console.WriteLine(Show(set.GetObject("location", true)));
    Console.WriteLine(Show(set.GetObject("Missing")));
    Console.WriteLine(Show(set.GetString("Missing")));
    Try("GetString of an int", () => set.GetString("Count"));
    Try("GetString of a Point", () => set.GetString("Location"));
    Try("GetString(null)", () => set.GetString(null));
    Try("GetObject(null)", () => set.GetObject(null));
    Console.WriteLine(set.GetDefaultReader().Name + " " + set.GetDefaultWriter().Name);
    Console.WriteLine(ReferenceEquals(set, resources.GetResourceSet(CultureInfo.InvariantCulture, false, false)));
    var enumerator = set.GetEnumerator();
    Try("Key before MoveNext", () => Console.WriteLine(enumerator.Key));
    int count = 0;
    while (enumerator.MoveNext()) count++;
    Console.WriteLine(count + " " + enumerator.MoveNext());
    Try("Key after the end", () => Console.WriteLine(enumerator.Key));
    enumerator.Reset();
    Console.WriteLine(enumerator.MoveNext() + " " + (enumerator.Entry.Key is string));
    IEnumerator plain = ((IEnumerable)set).GetEnumerator();
    Console.WriteLine(plain.MoveNext() + " " + plain.Current.GetType().Name);
});

Try("cultures", () =>
{
    var invariant = CultureInfo.InvariantCulture;
    foreach (var create in new[] { true, false })
        foreach (var parents in new[] { true, false })
            foreach (var name in new[] { "", "fr", "fr-FR" })
            {
                var resources = Strings();
                var set = resources.GetResourceSet(CultureInfo.GetCultureInfo(name), create, parents);
                var neutral = resources.GetResourceSet(invariant, false, false);
                Console.WriteLine($"[{name}] create={create} parents={parents}: {(set == null ? "null" : "set " + set.GetString("Hello"))}, neutral cached: {neutral != null}, same: {ReferenceEquals(set, neutral)}");
            }
    // Once the neutral set is loaded, any culture finds it.
    foreach (var create in new[] { true, false })
        foreach (var parents in new[] { true, false })
        {
            var resources = Strings();
            resources.GetResourceSet(invariant, true, true);
            var set = resources.GetResourceSet(CultureInfo.GetCultureInfo("fr-FR"), create, parents);
            Console.WriteLine($"after the neutral set: create={create} parents={parents}: {(set == null ? "null" : "set " + set.GetString("Hello"))}");
        }
    Try("null culture", () => Strings().GetResourceSet(null, true, true));
    var shared = Strings();
    var fr = shared.GetResourceSet(CultureInfo.GetCultureInfo("fr-FR"), true, true);
    Console.WriteLine(ReferenceEquals(fr, shared.GetResourceSet(CultureInfo.InvariantCulture, false, false)) + " " + ReferenceEquals(fr, shared.GetResourceSet(CultureInfo.GetCultureInfo("de"), false, true)));
});

Try("closing a set", () =>
{
    var resources = Strings();
    var set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
    Console.WriteLine(set.GetString("Hello"));
    set.Dispose();
    Try("GetString on a closed set", () => set.GetString("Hello"));
    Try("GetObject on a closed set", () => set.GetObject("Hello"));
    Try("enumerate a closed set", () => { foreach (DictionaryEntry e in set) { } });
    Try("the manager shares the closed set", () => resources.GetString("Hello"));
    Console.WriteLine(ReferenceEquals(set, resources.GetResourceSet(CultureInfo.InvariantCulture, false, false)));
    resources.ReleaseAllResources();
    Console.WriteLine(resources.GetResourceSet(CultureInfo.InvariantCulture, false, false) == null);
    Console.WriteLine(resources.GetString("Hello"));
    var again = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
    Console.WriteLine(ReferenceEquals(again, set) + " " + again.GetString("Hello"));
    again.Close();
    Try("GetString after Close", () => again.GetString("Hello"));
});

Try("type-coded resources", () =>
{
    var resources = Blobs();
    var set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
    Dump(set);
    foreach (var name in new[] { "Text", "Bytes", "Data", "Count", "Big", "Ratio", "Flag", "Letter", "Stamp", "Span", "Price", "Nothing", "Small", "Byte", "Float", "Missing" })
        Try("GetString " + name, () => Console.WriteLine(Show(set.GetString(name))));
});

Try("a boxed value is cached, so reading it as a string is a bad cast", () =>
{
    var set = Blobs().GetResourceSet(CultureInfo.InvariantCulture, true, true);
    Console.WriteLine(ReferenceEquals(set.GetObject("Count"), set.GetObject("Count")));
    Try("GetString Count", () => set.GetString("Count"));
    Console.WriteLine(ReferenceEquals(set.GetObject("Bytes"), set.GetObject("Bytes")));
    Try("GetString Bytes", () => set.GetString("Bytes"));
    var manager = Blobs();
    Console.WriteLine(Show(manager.GetObject("Stamp")));
    Try("manager GetString Stamp", () => manager.GetString("Stamp"));
});

Try("GetStream", () =>
{
    var resources = Blobs();
    foreach (var name in new[] { "Data", "EmptyData", "Bytes", "Text", "Count", "Nothing", "Missing" })
        Try("stream " + name, () =>
        {
            using UnmanagedMemoryStream s = resources.GetStream(name);
            if (s == null) { Console.WriteLine("null"); return; }
            var bytes = new byte[s.Length];
            int n = s.Read(bytes, 0, bytes.Length);
            Console.WriteLine($"{s.GetType().Name} len={s.Length} pos={s.Position} read={n} [{string.Join(",", bytes)}] canRead={s.CanRead} canWrite={s.CanWrite} canSeek={s.CanSeek}");
        });
    Try("stream null", () => resources.GetStream(null));
    Try("each call has its own stream", () =>
    {
        var a = resources.GetStream("Data"); var b = resources.GetStream("Data");
        Console.WriteLine(ReferenceEquals(a, b));
        a.ReadByte(); Console.WriteLine(a.Position + " " + b.Position);
    });
    Try("with a culture", () => Console.WriteLine(resources.GetStream("Data", CultureInfo.GetCultureInfo("fr")).Length));
    Try("IgnoreCase", () =>
    {
        Console.WriteLine(resources.GetStream("data") == null);
        resources.IgnoreCase = true;
        Console.WriteLine(resources.GetStream("data").Length);
    });
    Try("GetObject wraps the stream", () =>
    {
        var m = Blobs();
        var one = (Stream)m.GetObject("Data"); one.ReadByte();
        var two = (Stream)m.GetObject("Data");
        Console.WriteLine($"{one.GetType().Name} {one.Position} {two.Position} {one.Length} {one.CanWrite} {ReferenceEquals(one, two)}");
        var set = m.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        Console.WriteLine(set.GetObject("Data").GetType().Name + " " + ReferenceEquals(set.GetObject("Data"), set.GetObject("Data")));
        Console.WriteLine(set.GetObject("Data") is UnmanagedMemoryStream);
    });
    Try("a string is not a stream", () => Strings().GetStream("Hello"));
    Try("a preserialized int is not a stream", () => Strings().GetStream("Count"));
    Console.WriteLine(Strings().GetStream("Missing") == null);
});

class Marker;
