// System.Collections throughput, the program behind the numbers of ADR 0023's System.Collections row:
//   tools/il-first/ab.sh System.Collections tools/il-first/bench/collections_asm_throughput.cs
// Stack, LinkedList, PriorityQueue, SortedList, SortedDictionary, SortedSet, OrderedDictionary. The output is checksums only.
using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        long check = 0;
        for (int round = 0; round < 30; round++)
        {
            var stack = new Stack<int>();
            for (int i = 0; i < 100_000; i++) stack.Push(i * 3);
            foreach (int v in stack) check += v;
            while (stack.Count > 50_000) check += stack.Pop();
            check += stack.Peek();

            var list = new LinkedList<int>();
            for (int i = 0; i < 20_000; i++) { list.AddLast(i); if ((i & 3) == 0) list.AddFirst(-i); }
            for (var node = list.First; node != null; node = node.Next) check += node.Value;
            for (var node = list.First; node != null;) { var next = node.Next; if ((node.Value & 1) == 0) list.Remove(node); node = next; }
            check += list.Count;

            var pq = new PriorityQueue<int, int>();
            for (int i = 0; i < 50_000; i++) pq.Enqueue(i, (i * 7919) % 10007);
            while (pq.Count > 0) check += pq.Dequeue() & 0xff;

            var sl = new SortedList<int, int>();
            for (int i = 0; i < 5_000; i++) sl[(i * 7919) % 10007] = i;
            foreach (var kv in sl) check += kv.Key ^ kv.Value;
            for (int i = 0; i < 5_000; i++) if (sl.TryGetValue(i, out int v)) check += v;

            var sd = new SortedDictionary<int, string>();
            for (int i = 0; i < 20_000; i++) sd[(i * 7919) % 20011] = "v" + i;
            foreach (var kv in sd) check += kv.Key + kv.Value.Length;
            for (int i = 0; i < 20_000; i += 3) sd.Remove(i);
            check += sd.Count;

            var ss = new SortedSet<int>();
            for (int i = 0; i < 20_000; i++) ss.Add((i * 7919) % 20011);
            check += ss.GetViewBetween(100, 5000).Count + ss.Min + ss.Max;
            foreach (int v in ss) check += v & 15;

            var od = new OrderedDictionary<string, int>();
            for (int i = 0; i < 10_000; i++) od["k" + (i % 5000)] = i;
            foreach (var kv in od) check += kv.Value;
            for (int i = 0; i < 5000; i += 2) od.Remove("k" + i);
            check += od.Count;
        }
        Console.WriteLine(check);
    }
}
