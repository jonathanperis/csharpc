// Collection<T>, ReadOnlyCollection<T> and ReadOnlyDictionary<TKey,TValue> throughput, the program behind the speed and NativeAOT columns of
// docs/il-first/System.Collections.ObjectModel.md (tools/il-first/measure-aot.sh): Collection<T> Add/indexer/Insert/RemoveAt,
// ReadOnlyCollection<T> indexer/foreach/IndexOf, ReadOnlyDictionary TryGetValue and enumeration. The output is checksums only.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

long check = 0;
for (int round = 0; round < 20; round++)
{
    var collection = new Collection<int>();
    for (int i = 0; i < 500_000; i++) collection.Add(i * 7 % 1_000_003);
    for (int i = 0; i < collection.Count; i++) check += collection[i];
    for (int i = 0; i < 1_000; i++) collection.Insert(0, i);
    for (int i = 0; i < 1_000; i++) collection.RemoveAt(collection.Count - 1);
    check += collection.Count;

    var readOnly = new ReadOnlyCollection<int>(new List<int>(collection));
    for (int i = 0; i < readOnly.Count; i++) check += readOnly[i];
    foreach (var v in readOnly) check ^= v;
    for (int i = 0; i < 20; i++) check += readOnly.IndexOf(i * 12_345);

    var dictionary = new Dictionary<int, int>();
    for (int i = 0; i < 100_000; i++) dictionary[i] = i * 3;
    var view = new ReadOnlyDictionary<int, int>(dictionary);
    for (int i = 0; i < 1_000_000; i++) if (view.TryGetValue(i % 150_000, out var v)) check += v;
    foreach (var pair in view) check ^= pair.Key + pair.Value;
    foreach (var key in view.Keys) check += key & 1;
}
Console.WriteLine(check);
