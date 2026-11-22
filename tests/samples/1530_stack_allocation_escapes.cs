// Objects and delegates that do not escape their method live in its stack frame at -O1 and up (#457). Each case here either stays
// in the frame or escapes in a way the escape check must see; the output is the same either way, and wrong only if an object that
// escapes was put on the stack (a reused slot, a dangling reference after the frame is gone, a field the collector did not scan).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

class Pair
{
    public object? First;
    public string? Second;
    public int Count;
}

class Node
{
    public Node? Next;
    public int Value;
}

class Big
{
    public long A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T;
}

static class Program
{
    static Pair? s_kept;
    static readonly List<Pair> s_list = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Sum(Func<int, int> f, int n)
    {
        int total = 0;
        for (int i = 0; i < n; i++) total += f(i);
        return total;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Describe(Pair p) => p is null ? "null" : $"{p.First}/{p.Second}/{p.Count}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void KeepIfOdd(Pair p, int i) { if ((i & 1) == 1) s_kept = p; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Pair Identity(Pair p) => p;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Throw(Pair p) => throw new InvalidOperationException("pair " + p.Count);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Depth(int n)
    {
        var p = new Pair { Count = n, Second = "level" + n };
        int below = n == 0 ? 0 : Depth(n - 1);
        return below + p.Count + p.Second!.Length;
    }

    static void Main()
    {
        // A capturing lambda passed to a method that only invokes it; a collection runs inside the callback while the delegate (and
        // through it the closure) are only referenced from the frame.
        int offset = 3;
        long checksum = 0;
        for (int round = 0; round < 50; round++)
        {
            checksum += Sum(i => { if (i == 5) GC.Collect(); return i + offset + round; }, 10);
        }
        Console.WriteLine("delegates " + checksum);

        // Small objects in a loop whose fields hold the only references to heap strings, with collections while they live.
        long lengths = 0;
        for (int i = 0; i < 2000; i++)
        {
            var p = new Pair { First = new string('x', i % 7 + 1), Second = i.ToString(), Count = i };
            if (i % 250 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            lengths += ((string)p.First).Length + p.Second.Length + p.Count;
            if (i % 500 == 0) Console.WriteLine(Describe(p));
        }
        Console.WriteLine("pairs " + lengths);

        // Escapes the check must see: a conditional store to a static, a list, a returned argument, a loop-carried reference.
        for (int i = 0; i < 4; i++) KeepIfOdd(new Pair { Count = i, Second = "kept" }, i);
        GC.Collect();
        Console.WriteLine("kept " + Describe(s_kept!));
        for (int i = 0; i < 3; i++) s_list.Add(new Pair { Count = 10 + i, Second = "listed" });
        GC.Collect();
        foreach (var p in s_list) Console.WriteLine("listed " + Describe(p));
        Pair? last = null;
        for (int i = 0; i < 3; i++) last = Identity(new Pair { Count = 20 + i, Second = "identity" });
        GC.Collect();
        Console.WriteLine("identity " + Describe(last!));
        Node? head = null;
        for (int i = 0; i < 5; i++) head = new Node { Next = head, Value = i };
        GC.Collect();
        var values = new List<int>();
        for (var n = head; n != null; n = n.Next) values.Add(n.Value);
        Console.WriteLine("chain " + string.Join(",", values));
        Pair? previous = null;
        int distinct = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = new Pair { Count = i };
            if (!ReferenceEquals(p, previous)) distinct++;
            previous = p;
        }
        Console.WriteLine("distinct " + distinct + " last " + previous!.Count);

        // Identity-based operations on an object that otherwise stays local.
        var locked = new Pair { Count = 7 };
        lock (locked) locked.Count++;
        var hashed = new Pair { Count = 1 };
        int h1 = RuntimeHelpers.GetHashCode(hashed), h2 = RuntimeHelpers.GetHashCode(hashed);
        Console.WriteLine("lock " + locked.Count + " hash stable " + (h1 == h2));
        var weak = new WeakReference(new Pair { Count = 3 });
        Console.WriteLine("weak target type " + (weak.Target is null ? "collected" : "Pair"));

        // An exception thrown from a callee while the object lives in the frame.
        try { Throw(new Pair { Count = 42 }); }
        catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }

        // Recursion, and an object larger than the stack budget.
        Console.WriteLine("depth " + Depth(200));
        var big = new Big { A = 1, T = 20 };
        Console.WriteLine("big " + (big.A + big.T));

        // Threads: an object handed to another thread escapes.
        var shared = new Pair { Count = 0 };
        var t = new Thread(() => { for (int i = 0; i < 1000; i++) Interlocked.Increment(ref shared.Count); });
        t.Start(); t.Join();
        Console.WriteLine("thread " + shared.Count);
    }
}
