// A heap that grows, empties and grows again with other object sizes: the collector gives the pages
// of surplus empty chunks back to the OS and must hand them out again as zeroed objects of any size.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

class Node { public long A, B; public Node? Next; public int[]? Data; }

static class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Fill(int count, int size)
    {
        var keep = new List<int[]>(count);
        long dirty = 0;
        for (int i = 0; i < count; i++)
        {
            var a = new int[size];
            foreach (var x in a) if (x != 0) dirty++;
            a[0] = i; a[size - 1] = -i;
            keep.Add(a);
        }
        long sum = 0;
        foreach (var a in keep) sum += a[0] + a[^1];
        return dirty * 1_000_000 + sum + keep.Count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Nodes()
    {
        Node? head = null;
        for (int i = 0; i < 20_000; i++) head = new Node { A = i, Next = i % 100 == 0 ? null : head };
        long nonZero = 0;
        for (int i = 0; i < 50_000; i++) { var n = new Node(); if (n.A != 0 || n.B != 0 || n.Next != null || n.Data != null) nonZero++; }
        return $"nodes {head!.A} {nonZero}";
    }

    static void Main()
    {
        Console.WriteLine($"burst: {Fill(3_000_000, 4)}");
        GC.Collect();
        int[] sizes = [3, 40, 7, 100, 1, 300, 13, 2, 60];
        for (int round = 0; round < sizes.Length; round++)
        {
            Console.WriteLine($"round {round}: {Fill(1_500_000 / (sizes[round] + 4), sizes[round])} {Nodes()}");
            if (round % 3 == 2) GC.Collect();
        }
    }
}
