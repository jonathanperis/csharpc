// Partitioner (ranges, static and dynamic partitions of lists, arrays and enumerables), Barrier
// (phases, post-phase action, participants, its exceptions) and AggregateException.Flatten.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Ranges: Create(from, to, size) chunks deterministically.
var ranges = Partitioner.Create(0, 25, 7);
Console.WriteLine($"ranges: {string.Join(" ", ranges.GetDynamicPartitions().Select(r => $"[{r.Item1},{r.Item2})"))}");
var longRanges = Partitioner.Create(10L, 30L, 8L);
Console.WriteLine($"long ranges: {string.Join(" ", longRanges.GetDynamicPartitions().Select(r => $"[{r.Item1},{r.Item2})"))}");
try { Partitioner.Create(5, 5); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

// Static partitions of a list / array: each partition is a contiguous run.
var list = Enumerable.Range(1, 10).ToList();
var listPartitioner = Partitioner.Create(list, loadBalance: false);
var partitions = listPartitioner.GetPartitions(3);
for (int p = 0; p < partitions.Count; p++)
{
    var items = new List<int>();
    while (partitions[p].MoveNext()) items.Add(partitions[p].Current);
    Console.WriteLine($"list partition {p}: {string.Join(",", items)}");
}
var orderable = Partitioner.Create(new[] { "a", "b", "c", "d", "e" }, loadBalance: false);
foreach (var part in orderable.GetOrderablePartitions(2))
{
    var items = new List<string>();
    while (part.MoveNext()) items.Add($"{part.Current.Key}:{part.Current.Value}");
    Console.WriteLine($"orderable: {string.Join(" ", items)}");
}
Console.WriteLine($"supports dynamic: {listPartitioner.SupportsDynamicPartitions} keys ordered {orderable.KeysOrderedInEachPartition} {orderable.KeysOrderedAcrossPartitions} normalized {orderable.KeysNormalized}");

// Dynamic partitions of an enumerable, drained by one consumer: every element once, in order.
var dynamicSource = Partitioner.Create(Enumerable.Range(0, 20), EnumerablePartitionerOptions.NoBuffering);
using (var e = dynamicSource.GetDynamicPartitions().GetEnumerator())
{
    var all = new List<int>();
    while (e.MoveNext()) all.Add(e.Current);
    Console.WriteLine($"dynamic: {all.Count} items, sum {all.Sum()}, ordered {all.SequenceEqual(Enumerable.Range(0, 20))}");
}

// Parallel.ForEach over a range partitioner: the per-range work sums to the whole.
long total = 0;
Parallel.ForEach(Partitioner.Create(0, 1000, 100), range =>
{
    long local = 0;
    for (int i = range.Item1; i < range.Item2; i++) local += i;
    Interlocked.Add(ref total, local);
});
Console.WriteLine($"Parallel.ForEach over ranges: {total}");

// Barrier: three participants meet each phase; the post-phase action runs once per phase.
var log = new ConcurrentQueue<string>();
using (var barrier = new Barrier(3, b => log.Enqueue($"phase {b.CurrentPhaseNumber} done")))
{
    var workers = Enumerable.Range(0, 3).Select(id => Task.Run(() =>
    {
        for (int phase = 0; phase < 2; phase++) barrier.SignalAndWait();
    })).ToArray();
    Task.WaitAll(workers);
    Console.WriteLine($"barrier: {string.Join(", ", log)}; phase {barrier.CurrentPhaseNumber}, participants {barrier.ParticipantCount}");
    barrier.AddParticipant();
    Console.WriteLine($"after add: {barrier.ParticipantCount} remaining {barrier.ParticipantsRemaining}");
    barrier.RemoveParticipants(2);
    Console.WriteLine($"after remove: {barrier.ParticipantCount}");
    Console.WriteLine($"timed out: {barrier.SignalAndWait(10)}");
}
using (var failing = new Barrier(1, _ => throw new InvalidOperationException("post-phase failed")))
{
    try { failing.SignalAndWait(); }
    catch (BarrierPostPhaseException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} / {e.InnerException?.Message}"); }
}
try { new Barrier(-1); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

// AggregateException.Flatten: nested aggregates unpacked breadth first.
var nested = new AggregateException("outer",
    new InvalidOperationException("one"),
    new AggregateException("middle", new ArgumentException("two"), new AggregateException(new FormatException("three"))),
    new TimeoutException("four"));
var flat = nested.Flatten();
Console.WriteLine($"flatten: {flat.Message}");
Console.WriteLine($"inner: {string.Join(", ", flat.InnerExceptions.Select(x => x.GetType().Name + ":" + x.Message))}");
Console.WriteLine($"default message: {new AggregateException(new Exception("x")).Flatten().Message}");
