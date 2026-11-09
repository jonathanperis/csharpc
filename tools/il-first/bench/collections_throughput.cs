// Dictionary / HashSet throughput, the program behind the numbers of issue #223 and docs/21-ownership.md Part B:
//   tools/il-first/slicer/slice.sh /tmp/dict.dll 'System.Collections.Generic.Dictionary!2,System.Collections.Generic.HashSet!1'
//   tools/il-first/slicer/compare.sh tools/il-first/bench/collections_throughput.cs /tmp/dict.dll 7
// (shadow build against slice build: binary size, compile CPU, run CPU, output equality). The output is the checksums only.
using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        long check = 0;
        // Dictionary<int,int>: 200k inserts, lookups and removes, 30 rounds
        for (int round = 0; round < 30; round++)
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i < 200_000; i++) d[i * 7] = i;
            for (int i = 0; i < 200_000; i++) if (d.TryGetValue(i * 7, out var v)) check += v;
            for (int i = 0; i < 200_000; i += 2) d.Remove(i * 7);
            check += d.Count;
        }
        // Dictionary<string,int>: 20k keys, 60 rounds
        var keys = new string[20_000];
        for (int i = 0; i < keys.Length; i++) keys[i] = "key" + i;
        for (int round = 0; round < 60; round++)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < keys.Length; i++) d[keys[i]] = i;
            for (int i = 0; i < keys.Length; i++) check += d[keys[i]];
            for (int i = 0; i < keys.Length; i += 3) d.Remove(keys[i]);
            check += d.Count;
        }
        // HashSet<int> and HashSet<string>
        for (int round = 0; round < 20; round++)
        {
            var h = new HashSet<int>();
            for (int i = 0; i < 100_000; i++) h.Add(i * 13);
            for (int i = 0; i < 100_000; i++) if (h.Contains(i * 13 + (i & 1))) check++;
            var hs = new HashSet<string>();
            for (int i = 0; i < keys.Length; i++) hs.Add(keys[i]);
            for (int i = 0; i < keys.Length; i++) if (hs.Contains(keys[(i * 7) % keys.Length])) check++;
        }
        Console.WriteLine(check);
    }
}
