// System.Collections.Hashtable: .NET's bucket layout (enumeration order with int keys), growth,
// removal, comparers, Keys/Values, Clone and exceptions; File.GetCreationTime of a missing file.
using System;
using System.Collections;
using System.IO;

class Program
{
    static void Dump(Hashtable t)
    {
        foreach (DictionaryEntry e in t) Console.Write($"{e.Key}={e.Value} ");
        Console.WriteLine($"(count {t.Count})");
    }

    static void Main()
    {
        var t = new Hashtable();
        for (int i = 0; i < 12; i++) t.Add(i * 7, "v" + i);
        Dump(t);
        t.Remove(14);
        t.Remove(999);
        t[70] = "replaced";
        t[1000] = "new";
        Dump(t);
        Console.WriteLine($"{t.ContainsKey(21)} {t.Contains(14)} {t.ContainsValue("v3")} {t.ContainsValue(null)} {t[999] ?? "null"}");
        foreach (var k in t.Keys) Console.Write(k + " ");
        Console.WriteLine();
        foreach (var v in t.Values) Console.Write(v + " ");
        Console.WriteLine();
        var clone = (Hashtable)t.Clone();
        clone.Clear();
        Console.WriteLine($"{clone.Count} {t.Count}");

        var small = new Hashtable(100, 0.5f);
        for (int i = 200; i > 150; i -= 9) small[i] = i * 2;
        Dump(small);

        var names = new Hashtable(100, StringComparer.OrdinalIgnoreCase);
        names.Add("/Tmp/File.txt", 1);
        names["readme.MD"] = 2;
        Console.WriteLine($"{names["/TMP/FILE.TXT"]} {names["README.md"]} {names["other"] ?? "missing"} {names.Count}");

        var copy = new Hashtable(t);
        Console.WriteLine(copy.Count);
        IDictionaryEnumerator e = t.GetEnumerator();
        e.MoveNext();
        Console.WriteLine($"{e.Key} {e.Value} {e.Entry.Key}");

        try { t.Add(0, "dup"); } catch (ArgumentException ex) { Console.WriteLine(ex.Message); }
        try { t.Add(null!, 1); } catch (ArgumentNullException ex) { Console.WriteLine(ex.Message); }
        try { _ = new Hashtable(-1); } catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.Message); }
        try { _ = new Hashtable(1, 2.0f); } catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.Message); }
        try { foreach (DictionaryEntry x in t) t[x.Key] = 0; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        var before = t.GetEnumerator();
        try { _ = before.Key; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { _ = before.Value; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { _ = before.Entry; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { _ = before.Current; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        while (before.MoveNext()) { }
        try { _ = before.Key; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { _ = before.Current; } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }

        Console.WriteLine(File.GetCreationTimeUtc("/no/such/file").ToString("o"));
        Console.WriteLine(new FileInfo("/no/such/file").CreationTimeUtc.Year);
        string path = Path.Combine(Path.GetTempPath(), "csharpc_505_" + Environment.ProcessId);
        File.WriteAllText(path, "x");
        var created = new FileInfo(path).CreationTimeUtc;
        Console.WriteLine($"{Math.Abs((created - DateTime.UtcNow).TotalMinutes) < 5} {created.Kind} {File.GetCreationTime(path).Kind} {created <= File.GetLastWriteTimeUtc(path)}");
        File.Delete(path);
    }
}
