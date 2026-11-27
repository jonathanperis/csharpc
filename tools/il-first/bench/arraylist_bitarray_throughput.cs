// ArrayList and BitArray throughput, the program behind the speed and NativeAOT columns of docs/il-first/System.Collections.ArrayList.md
// (tools/il-first/measure-aot.sh): ArrayList Add/IndexOf/Contains/Sort/BinarySearch/RemoveAt over boxed ints, and BitArray construction from
// bool[], And/Or/Xor/Not, CopyTo and shifts over 4 096-bit arrays. The output is checksums only.
using System;
using System.Collections;

long check = 0;
for (int round = 0; round < 10; round++)
{
    var list = new ArrayList();
    for (int i = 0; i < 20_000; i++) list.Add((i * 7919) % 20_011);
    for (int i = 0; i < 2_000; i++) check += list.IndexOf(i * 3) + (list.Contains(i) ? 1 : 0);
    list.Sort();
    for (int i = 0; i < 100_000; i++) check += list.BinarySearch(i % 25_000);
    for (int i = 0; i < 1_000; i++) list.RemoveAt(list.Count / 2);
    check += list.Count + (int)list[17]!;

    var bools = new bool[4096];
    for (int i = 0; i < bools.Length; i++) bools[i] = (i * 31 + round) % 7 < 3;
    var bytes = new byte[512];
    for (int i = 0; i < 20_000; i++)
    {
        var a = new BitArray(bools);
        var b = new BitArray(4096, (i & 1) == 0);
        a.Xor(b).And(new BitArray(bools)).Or(b).Not();
        a.LeftShift(i % 64).RightShift(3);
        a.CopyTo(bytes, 0);
        check += bytes[i % 512] + (a.HasAnySet() ? 1 : 0);
    }
}
Console.WriteLine(check);
