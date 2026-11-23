// Deconstruction whose right side has control flow (object initializers, ?:, ??): Roslyn captures the targets first and
// assigns through the captures (#386). Also the ref-returning call target that already worked.
using System;
using System.Runtime.CompilerServices;

class Point { public int X; public override string ToString() => "P" + X; }
class Holder { public Point? Field; public int Count; }

static class Program
{
    static int s_calls;
    static int Next() => ++s_calls;

    static void Main()
    {
        Point? p = null;
        (p, _) = (new Point { X = 3 }, 0);
        Console.WriteLine(p.X);

        var holder = new Holder();
        (holder.Field, holder.Count) = (new Point { X = 5 }, holder.Count + 1);
        Console.WriteLine($"{holder.Field} {holder.Count}");

        var points = new Point?[2];
        int i = 0;
        (points[i], points[i + 1]) = (new Point { X = 7 }, i > 0 ? null : new Point { X = 8 });
        Console.WriteLine($"{points[0]} {points[1]}");

        int a = 0;
        string? s = null;
        (a, s) = (Next(), s ?? "fallback");
        Console.WriteLine($"{a} {s}");

        (var q, var n) = (new Point { X = 9 }, Next());
        Console.WriteLine($"{q} {n}");

        Unsafe.AsRef(in p) = new Point { X = 4 };
        Console.WriteLine(p.X);
    }
}
