// Pool workers blocked in SemaphoreSlim.Wait (bounded BlockingCollection.Add) must inject
// replacement workers so queued consumers can run — otherwise WaitAll(producers) deadlocks when
// every worker is stuck in Add (macOS CI flake on 544_blocking_collection).
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

var shared = new BlockingCollection<int>(boundedCapacity: 2);
var producers = Enumerable.Range(0, 8).Select(p => Task.Run(() =>
{
    for (int i = 0; i < 50; i++) shared.Add(p * 1000 + i);
})).ToArray();
var consumers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
{
    long sum = 0;
    foreach (var v in shared.GetConsumingEnumerable()) sum += v;
    return sum;
})).ToArray();
Task.WaitAll(producers);
shared.CompleteAdding();
long total = consumers.Sum(c => c.Result);
Console.WriteLine(total);
Console.WriteLine(total == Enumerable.Range(0, 8).Sum(p => Enumerable.Range(0, 50).Sum(i => (long)(p * 1000 + i))));
