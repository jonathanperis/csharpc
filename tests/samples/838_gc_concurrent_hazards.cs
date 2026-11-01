// What a concurrent marker can lose (docs/adr/0020-concurrent-marking.md), exercised while other
// threads allocate: a reference moved from a local into a field, swapped through Interlocked, held
// only by an async method's or an iterator's frame across suspensions, copied into a large array or a
// struct field, read back from a WeakReference, or kept by a ConditionalWeakTable whose key is stored
// meanwhile. With --gc-concurrent and a small trigger these run during marking cycles; the output is
// the same with any collector.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

class Node { public int Value; public Node? Next; public Node(int v) { Value = v; } }
struct Pair { public Node? A; public Node? B; public long Tag; }
class Holder { public Node? Field; public Pair Pair; public object?[] Large = new object?[1000]; }

static class Program
{
    static volatile bool s_stop;

    static void Churn()
    {
        var keep = new List<object>();
        int n = 0;
        while (!s_stop)
        {
            keep.Add(new byte[64 + (n % 300)]);
            if (keep.Count > 2000) keep.Clear();
            n++;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Node Chain(int start, int length)
    {
        Node? head = null;
        for (int i = length - 1; i >= 0; i--) head = new Node(start + i) { Next = head };
        return head!;
    }

    static long Sum(Node? n) { long s = 0; for (; n != null; n = n.Next) s += n.Value; return s; }

    static async Task<long> HeldByFrame(int seed)
    {
        var local = Chain(seed, 200);           // only this frame refers to it across the awaits
        long total = 0;
        for (int i = 0; i < 20; i++)
        {
            await Task.Yield();
            Chain(0, 500);                        // garbage while suspended frames hold theirs
            total += Sum(local);
        }
        return total;
    }

    static IEnumerable<long> HeldByIterator(int seed)
    {
        var local = Chain(seed, 100);
        for (int i = 0; i < 20; i++)
        {
            Chain(0, 300);
            yield return Sum(local);
        }
    }

    static void Main()
    {
        var churners = new Thread[2];
        for (int i = 0; i < churners.Length; i++) { churners[i] = new Thread(Churn); churners[i].Start(); }

        var holder = new Holder();
        long moved = 0, swapped = 0, copied = 0, structs = 0, weak = 0, table = 0;
        var cwt = new ConditionalWeakTable<object, Node>();
        var keys = new object[50];
        for (int round = 0; round < 200; round++)
        {
            var local = Chain(round, 50);
            Chain(0, 2000);
            holder.Field = local;                 // local -> field
            local = null;
            Chain(0, 2000);
            moved += Sum(holder.Field);

            var fresh = Chain(round * 2, 30);
            var old = Interlocked.Exchange(ref holder.Field, fresh);
            Chain(0, 1000);
            swapped += Sum(holder.Field) + Sum(old);

            var source = new object?[1000];
            for (int i = 0; i < source.Length; i += 50) source[i] = Chain(i, 3);
            Array.Copy(source, holder.Large, source.Length);
            source = null;
            Chain(0, 2000);
            for (int i = 0; i < holder.Large.Length; i += 50) copied += Sum((Node?)holder.Large[i]);

            holder.Pair = new Pair { A = Chain(1, 10), B = Chain(2, 10), Tag = round };
            Chain(0, 1000);
            structs += Sum(holder.Pair.A) + Sum(holder.Pair.B) + holder.Pair.Tag;

            var target = Chain(round, 5);
            var reference = new WeakReference<Node>(target);
            holder.Pair.A = target;               // strongly held, so the target is there under any collector
            target = null;
            if (reference.TryGetTarget(out var back)) { holder.Field = back; weak += Sum(back); }
            Chain(0, 1000);
            weak += Sum(holder.Field);

            var key = new object();
            cwt.Add(key, Chain(round, 4));
            keys[round % keys.Length] = key;     // the key stored while marking may run
            Chain(0, 1000);
            if (cwt.TryGetValue(keys[round % keys.Length]!, out var value)) table += Sum(value);
        }

        var tasks = new List<Task<long>>();
        for (int i = 0; i < 16; i++) tasks.Add(HeldByFrame(i * 1000));
        long frames = 0;
        foreach (var t in tasks) frames += t.GetAwaiter().GetResult();
        long iterators = 0;
        foreach (var v in HeldByIterator(7)) iterators += v;

        s_stop = true;
        foreach (var c in churners) c.Join();
        Console.WriteLine($"moved {moved} swapped {swapped} copied {copied} structs {structs} weak {weak} table {table}");
        Console.WriteLine($"frames {frames} iterators {iterators}");
    }
}
