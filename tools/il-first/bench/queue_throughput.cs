// Queue<T> throughput, the program behind the speed and NativeAOT columns of docs/il-first/System.Collections.Generic.Queue.md
// (tools/il-first/measure-aot.sh): Enqueue/Dequeue/TryDequeue, Peek, foreach, CopyTo, Contains. The output is checksums only.
using System;
using System.Collections.Generic;

long check = 0;
for (int round = 0; round < 20; round++)
{
    var queue = new Queue<int>();
    for (int i = 0; i < 1_000_000; i++)
    {
        queue.Enqueue(i);
        if ((i & 3) == 3) check += queue.Dequeue();
    }
    check += queue.Peek();
    foreach (var v in queue) check ^= v;
    var copy = new int[queue.Count];
    for (int i = 0; i < 20; i++) queue.CopyTo(copy, 0);
    check += copy[copy.Length / 2] + (queue.Contains(-1) ? 1 : 0);
    while (queue.TryDequeue(out var v)) check += v & 1;

    var strings = new Queue<string>();
    for (int i = 0; i < 500_000; i++) { strings.Enqueue("s"); if ((i & 1) == 1) check += strings.Dequeue().Length; }
}
Console.WriteLine(check);
