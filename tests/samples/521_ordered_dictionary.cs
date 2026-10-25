// OrderedDictionary<TKey, TValue>: order, index access, insert/remove, keys/values, capacity, exceptions.
using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Try(string label, Action a)
    {
        try { a(); Console.WriteLine(label + ": ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void IncrementValue(OrderedDictionary<string, int> d, string key)
    {
        if (!d.TryAdd(key, 1, out int index))
        {
            int value = d.GetAt(index).Value;
            d.SetAt(index, value + 1);
        }
    }

    static void Main()
    {
        var d = new OrderedDictionary<string, int>();
        Console.WriteLine(d.Capacity);
        foreach (var w in "the quick brown fox jumps over the lazy dog the end".Split(' ')) IncrementValue(d, w);
        Console.WriteLine(string.Join(", ", d.Select(p => $"{p.Key}={p.Value}")) + $" | {d.Count} {d.Capacity}");
        d.Insert(0, "first", 100);
        d.Remove("fox");
        d.RemoveAt(d.Count - 1);
        d["quick"] = 7;
        d["new"] = 8;
        d.SetAt(1, "THE", 9);
        Console.WriteLine(string.Join(", ", d.Select(p => $"{p.Key}={p.Value}")));
        Console.WriteLine($"{d.IndexOf("brown")} {d.IndexOf("nope")} {d.ContainsKey("lazy")} {d.ContainsValue(8)} {d.GetAt(2)}");
        Console.WriteLine($"{d.TryGetValue("dog", out int v, out int i)} {v} {i} {d.Remove("dog", out int removed)} {removed}");
        Console.WriteLine($"keys: {string.Join(" ", d.Keys)} | {((IList<string>)d.Keys)[0]} {d.Keys.Contains("new")} | values: {string.Join(" ", d.Values)} {((IList<int>)d.Values)[1]}");
        IDictionary<string, int> asDict = d;
        Console.WriteLine($"{asDict["new"]} {asDict.Keys.Count} {((IList<KeyValuePair<string, int>>)d)[0]}");
        Console.WriteLine($"{d.EnsureCapacity(50)} {d.Capacity}");
        d.TrimExcess();
        Console.WriteLine(d.Capacity);

        var ci = new OrderedDictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "1" };
        ci["a"] = "2";
        Console.WriteLine($"{ci.Count} {ci["A"]} {ci.GetAt(0).Key}");
        var copy = new OrderedDictionary<int, char>(new Dictionary<int, char> { [3] = 'c', [1] = 'a' });
        Console.WriteLine(string.Join(",", copy.Keys) + " " + new OrderedDictionary<int, int>(5).Capacity);

        Try("add dup", () => d.Add("new", 1));
        Try("get missing", () => _ = d["missing"]);
        Try("get at", () => d.GetAt(99));
        Try("get at -1", () => d.GetAt(-1));
        Try("insert", () => d.Insert(99, "x", 1));
        Try("insert dup", () => d.Insert(0, "new", 1));
        Try("set at dup", () => d.SetAt(0, "new", 1));
        Try("remove at", () => d.RemoveAt(99));
        Try("null key", () => d.Add(null!, 1));
        Try("capacity", () => new OrderedDictionary<int, int>(-1));
        Try("modified", () => { foreach (var p in d) d["zz" + p.Key] = 0; });
        Try("keys readonly", () => ((ICollection<string>)d.Keys).Add("x"));
    }
}
