// Objects of every small-object size class and some large ones, reachable only through a ref to
// their last element (an interior pointer) across collections, while garbage of the same sizes
// churns around them; then large arrays allocated in shuffled order, so the collector's table of
// large objects is out of order.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

static class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Churn(int length, int count)
    {
        long sum = 0;
        for (int i = 0; i < count; i++) { var junk = new byte[length]; junk[^1] = 1; sum += junk.Length; }
        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long ThroughInterior(int length)
    {
        var array = new byte[length];
        for (int i = 0; i < length; i++) array[i] = (byte)(i * 7 + length);
        ref byte last = ref array[length - 1];
        array = null!;
        long churned = Churn(length, 2000);
        GC.Collect();
        churned += Churn(length + 1, 2000);
        GC.Collect();
        return last + churned * 0;
    }

    static void Main()
    {
        int[] lengths = [1, 8, 17, 30, 47, 63, 80, 100, 120, 150, 180, 240, 300, 370, 500, 760, 1000, 1500, 2000, 2030, 2100, 5000, 70000];
        foreach (int length in lengths) Console.Write($"{ThroughInterior(length)} ");
        Console.WriteLine();

        var order = new List<int>();
        for (int i = 0; i < 400; i++) order.Add((i * 7919) % 400);
        var large = new byte[400][];
        foreach (int i in order) { large[i] = new byte[3000 + i * 13]; large[i][^1] = (byte)i; }
        for (int round = 0; round < 3; round++)
        {
            for (int i = round; i < 400; i += 3) large[i] = new byte[2500 + i];
            GC.Collect();
            long sum = 0;
            foreach (var a in large) sum += a.Length + a[^1];
            Console.WriteLine(sum);
        }
    }
}
