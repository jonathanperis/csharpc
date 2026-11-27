// List<T> throughput, the program behind the NativeAOT column of docs/il-first/System.Collections.Generic.List.md
// (tools/il-first/measure-aot.sh): Add, indexer, foreach, IndexOf/Contains, Insert(0), RemoveAll, Sort. The output is checksums only.
using System;
using System.Collections.Generic;

long check = 0;
for (int round = 0; round < 20; round++)
{
    var list = new List<int>();
    for (int i = 0; i < 1_000_000; i++) list.Add(i * 7 % 1_000_003);
    for (int i = 0; i < list.Count; i++) check += list[i];
    foreach (var v in list) check ^= v;
    for (int i = 0; i < 50; i++) check += list.IndexOf(i * 12_345) + (list.Contains(-i) ? 1 : 0);
    check += list.RemoveAll(v => v % 3 == 0);
    list.Sort();
    check += list[0] + list[list.Count / 2] + list[^1];
    var small = new List<string>();
    for (int i = 0; i < 2_000; i++) small.Insert(0, "x");
    check += small.Count;
}
Console.WriteLine(check);
