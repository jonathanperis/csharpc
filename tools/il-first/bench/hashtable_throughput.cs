// Hashtable throughput, the program behind the speed and NativeAOT columns of docs/il-first/System.Collections.Hashtable.md
// (tools/il-first/measure-aot.sh): inserts, lookups (hits and misses), enumeration, int and string keys, Remove. The output is checksums only.
using System;
using System.Collections;

long check = 0;
for (int round = 0; round < 20; round++)
{
    var table = new Hashtable();
    for (int i = 0; i < 200_000; i++) table[i] = i * 5;
    for (int i = 0; i < 1_000_000; i++) { var v = table[i % 240_000]; if (v != null) check += (int)v; }
    foreach (DictionaryEntry e in table) check ^= (int)e.Key;
    for (int i = 0; i < 200_000; i += 2) table.Remove(i);
    check += table.Count + (table.ContainsKey(7) ? 1 : 0);

    var named = new Hashtable();
    for (int i = 0; i < 100_000; i++) named["k" + (i % 20_000)] = i;
    for (int i = 0; i < 200_000; i++) if (named["k" + (i % 30_000)] is int n) check += n;
    check += named.Count;
}
Console.WriteLine(check);
