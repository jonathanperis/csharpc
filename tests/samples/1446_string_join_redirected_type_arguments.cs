// string.Join<T>(string, IEnumerable<T>) served from String's CoreLib IL (#423) when T is a type csharpc's corelib redirects
// (KeyValuePair<TKey, TValue>): a Dictionary, a list and a LINQ sequence of pairs, and nested pairs, each element through its ToString.
using System;
using System.Collections.Generic;
using System.Linq;

static class Program
{
    static void Main()
    {
        var counts = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        Console.WriteLine(string.Join(",", counts));
        Console.WriteLine(string.Join(" | ", new List<KeyValuePair<int, string>> { new(1, "one"), new(2, "two") }));
        Console.WriteLine(string.Join(";", counts.Where(p => p.Value > 1)));
        Console.WriteLine(string.Join(",", new[] { KeyValuePair.Create(KeyValuePair.Create(1, 2), "x") }.AsEnumerable()));
        Console.WriteLine("[" + string.Join(",", new Dictionary<string, int>()) + "]");
        Console.WriteLine(string.Join(",", counts.Keys) + " " + string.Join(",", counts.Values));
    }
}
