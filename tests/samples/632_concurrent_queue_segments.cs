// ConcurrentQueue<T> across segment boundaries: growth past the first 32-slot segment, wrap-around,
// snapshots (enumeration, ToArray, CopyTo, Count) while items are dequeued, Clear, and four producers
// with four consumers (only order-independent totals are printed).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static void Main()
    {
        var q = new ConcurrentQueue<int>();
        Console.WriteLine($"{q.IsEmpty} {q.Count} {q.TryPeek(out var none)} {none} {q.TryDequeue(out _)}");
        for (int i = 0; i < 100; i++) q.Enqueue(i);
        Console.WriteLine($"{q.Count} {q.IsEmpty} {q.TryPeek(out var first)} {first}");
        for (int i = 0; i < 40; i++) q.TryDequeue(out _);
        for (int i = 100; i < 170; i++) q.Enqueue(i);
        Console.WriteLine($"{q.Count} {string.Join(",", q.Take(5))} {q.Sum()} {q.ToArray().Length} {q.Last()}");
        using (var e = q.GetEnumerator())
        {
            for (int i = 0; i < 10; i++) q.TryDequeue(out _);
            q.Enqueue(-1);
            int n = 0, sum = 0;
            while (e.MoveNext()) { n++; sum += e.Current; }
            Console.WriteLine($"snapshot {n} {sum}; now {q.Count} {q.Contains(-1)}");
        }
        var target = new int[q.Count + 3];
        q.CopyTo(target, 3);
        Console.WriteLine($"{target[0]} {target[3]} {target[^1]}");
        try { q.CopyTo(new int[5], 0); } catch (ArgumentException ex) { Console.WriteLine(ex.Message); }
        try { q.CopyTo(target, -1); } catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.ParamName); }
        try { new ConcurrentQueue<int>(null!); } catch (ArgumentNullException ex) { Console.WriteLine(ex.ParamName); }
        q.Clear();
        Console.WriteLine($"{q.Count} {q.IsEmpty}");
        var fromList = new ConcurrentQueue<string>(Enumerable.Range(0, 50).Select(i => $"s{i}"));
        IProducerConsumerCollection<string> pc = fromList;
        pc.TryAdd("end");
        pc.TryTake(out var taken);
        Console.WriteLine($"{fromList.Count} {taken} {fromList.Last()}");

        var shared = new ConcurrentQueue<long>();
        const int PerProducer = 20000;
        long consumed = 0, consumedSum = 0;
        int producersDone = 0;
        var producers = Enumerable.Range(0, 4).Select(p => Task.Run(() =>
        {
            for (int i = 0; i < PerProducer; i++) shared.Enqueue(p * PerProducer + i);
            Interlocked.Increment(ref producersDone);
        })).ToArray();
        var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var lastSeen = new Dictionary<long, long>();
            while (true)
            {
                if (shared.TryDequeue(out long v))
                {
                    // Items of one producer come out in the order it enqueued them.
                    long producer = v / PerProducer;
                    if (lastSeen.TryGetValue(producer, out long previous) && previous >= v) throw new Exception("out of order");
                    lastSeen[producer] = v;
                    Interlocked.Increment(ref consumed);
                    Interlocked.Add(ref consumedSum, v);
                }
                else if (Volatile.Read(ref producersDone) == 4 && shared.IsEmpty) return;
            }
        })).ToArray();
        Task.WaitAll(producers);
        Task.WaitAll(consumers);
        long expected = 4L * PerProducer * (4L * PerProducer - 1) / 2;
        Console.WriteLine($"{consumed} {consumedSum == expected} {shared.Count}");
    }
}
