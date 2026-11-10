// System.Collections.Concurrent throughput, the program behind the numbers of ADR 0023's Concurrent row:
//   tools/il-first/ab.sh System.Collections.Concurrent tools/il-first/bench/concurrent_throughput.cs
// BlockingCollection with producers and consumers on threads, ConcurrentBag, ConcurrentStack, Partitioner ranges. The result is
// independent of thread scheduling (sums and counts only), so the checksum is deterministic.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        long check = 0;
        for (int round = 0; round < 8; round++)
        {
            using var queue = new BlockingCollection<int>(boundedCapacity: 64);
            long consumed = 0;
            var producers = Enumerable.Range(0, 3).Select(p => new Thread(() => { for (int i = 0; i < 20_000; i++) queue.Add(p * 100_000 + i); })).ToArray();
            var consumers = Enumerable.Range(0, 2).Select(_ => new Thread(() => { foreach (int v in queue.GetConsumingEnumerable()) Interlocked.Add(ref consumed, v); })).ToArray();
            foreach (var t in producers.Concat(consumers)) t.Start();
            foreach (var t in producers) t.Join();
            queue.CompleteAdding();
            foreach (var t in consumers) t.Join();
            check += consumed;

            var bag = new ConcurrentBag<int>();
            Parallel.For(0, 4, w => { for (int i = 0; i < 25_000; i++) bag.Add(w + i); });
            check += bag.Count + bag.Sum(v => (long)v);

            var stack = new ConcurrentStack<int>();
            Parallel.For(0, 4, w => { for (int i = 0; i < 25_000; i++) stack.Push(w * i); });
            long popped = 0;
            while (stack.TryPop(out int v)) popped += v;
            check += popped;

            long ranged = 0;
            Parallel.ForEach(Partitioner.Create(0, 400_000, 10_000), range => { long local = 0; for (int i = range.Item1; i < range.Item2; i++) local += i & 7; Interlocked.Add(ref ranged, local); });
            check += ranged;
        }
        Console.WriteLine(check);
    }
}
