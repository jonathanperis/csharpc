// Objects held only by an async method's (or iterator's) frame across suspension points, while
// enough is allocated in between for csharpc's --gc-nursery to run minor collections. The frame
// survives the first minor and becomes old; LLVM's coroutine lowering then spills fresh (young)
// references into it with plain stores, so the collector must still find them (ADR 0004).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

sealed class Node
{
    public readonly int Id;
    public readonly int[] Data;
    public readonly string Text;
    public Node(int id)
    {
        Id = id;
        Data = new int[24];
        for (var i = 0; i < Data.Length; i++) Data[i] = id * 31 + i;
        Text = "node-" + id;
    }
    public long Check()
    {
        long sum = Id;
        for (var i = 0; i < Data.Length; i++)
        {
            if (Data[i] != Id * 31 + i) return -1;
            sum += Data[i];
        }
        return Text == "node-" + Id ? sum : -2;
    }
}

static class Program
{
    static long s_sink;

    // Garbage shaped like Node (same size classes) so freed slots get reused and overwritten.
    static void Churn(int megabytes)
    {
        long bytes = (long)megabytes * 1024 * 1024;
        for (long done = 0; done < bytes; done += 160)
        {
            var junk = new int[24];
            junk[3] = (int)done;
            s_sink += junk[3] & 1;
            var o = new object[3];
            o[0] = junk;
            s_sink += o.Length;
        }
    }

    // Minor collections free what they miss; a full one hands those slots out again, and more garbage
    // overwrites them.
    static void Settle()
    {
        Churn(40);
        GC.Collect();
        Churn(16);
    }

    static async Task<long> Worker(int id)
    {
        long total = 0;
        for (var round = 0; round < 4; round++)
        {
            var held = new Node(id * 100 + round);
            await Task.Yield();
            Settle();
            var second = new List<Node> { new Node(id * 1000 + round) };
            await Task.Yield();
            Settle();
            await Task.Yield();
            total += held.Check() + second[0].Check();
        }
        return total;
    }

    static IEnumerable<long> Iterate(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var held = new Node(5000 + i);
            yield return 0;
            Settle();
            var other = new Node(6000 + i);
            yield return held.Check();
            Settle();
            yield return other.Check() + held.Check();
        }
    }

    static async Task Main()
    {
        var tasks = new List<Task<long>>();
        for (var i = 1; i <= 3; i++) tasks.Add(Worker(i));
        var results = await Task.WhenAll(tasks);
        for (var i = 0; i < results.Length; i++) Console.WriteLine($"worker {i + 1}: {results[i]}");

        long iterated = 0;
        foreach (var v in Iterate(3)) iterated += v;
        Console.WriteLine($"iterator: {iterated}");
        Console.WriteLine(s_sink > 0);
    }
}
