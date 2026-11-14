// ConcurrentDictionary<TKey,TValue> and ConcurrentQueue<T> throughput, the program behind the numbers of their docs/il-first row (#399):
//   nice -n 19 ionice -c3 tools/il-first/measure-aot.sh --callgrind tools/il-first/bench/concurrent_dictionary_throughput.cs
// Four threads add, look up and remove keys of their own range and update shared counters (int and string keys), then enumerate; four threads enqueue
// into one queue, then four drain it. The result is independent of thread scheduling (sums and counts only), so the checksum is deterministic.
using System;
using System.Collections.Concurrent;
using System.Threading;

class Program
{
    static void Main()
    {
        long check = 0;
        string[] names = new string[4_096];
        for (int i = 0; i < names.Length; i++) names[i] = "key" + i;

        for (int round = 0; round < 6; round++)
        {
            var map = new ConcurrentDictionary<int, long>();
            Run(4, w =>
            {
                for (int i = 0; i < 50_000; i++)
                {
                    int key = w * 50_000 + i;
                    map.TryAdd(key, i);
                    map.AddOrUpdate(-1 - (i & 1023), 1, (_, old) => old + 1); // shared counters, below every TryAdd key
                    if (i >= 7 && map.TryGetValue(key - 7, out long v) && (v & 1) == 0) map.TryRemove(key - 7, out _);
                }
            });
            long sum = map.Count;
            foreach (var pair in map) sum += pair.Key ^ pair.Value;
            check += sum;

            var byName = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            var lengths = new ConcurrentDictionary<string, int>();
            Run(4, w =>
            {
                for (int i = 0; i < 40_000; i++)
                {
                    string name = names[(i * 7 + w) & (names.Length - 1)];
                    lengths.GetOrAdd(name, static n => n.Length);
                    byName.AddOrUpdate(name, 1, (_, old) => old + 1);
                }
            });
            foreach (var pair in byName) check += pair.Value;
            foreach (var pair in lengths) check += pair.Value;

            // Producers enqueue concurrently, then consumers drain concurrently: no consumer spins on an empty queue, so the instruction count
            // does not depend on scheduling (a consumer stops at its first failed TryDequeue, which only an empty queue gives).
            var queue = new ConcurrentQueue<int>();
            long dequeued = 0;
            Run(4, w => { for (int i = 0; i < 50_000; i++) queue.Enqueue(w * 100_000 + i); });
            Run(4, _ =>
            {
                long local = 0;
                while (queue.TryDequeue(out int v)) local += v;
                Interlocked.Add(ref dequeued, local);
            });
            check += dequeued + queue.Count;
        }
        Console.WriteLine(check);
    }

    static void Run(int count, Action<int> body)
    {
        var threads = new Thread[count];
        for (int w = 0; w < count; w++) { int id = w; threads[w] = new Thread(() => body(id)); }
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
    }
}
