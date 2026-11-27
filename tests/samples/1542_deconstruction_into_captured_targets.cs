// Deconstruction whose right side has control flow (object initializers, ?:, ??): Roslyn captures the targets first and
// assigns through the captures (#386). Also the ref-returning call target that already worked.
using System;
using System.Runtime.CompilerServices;

class Point { public int X; public override string ToString() => "P" + X; }
class Holder { public Point? Field; public int Count; }
struct Pair { public int A; public Point? B; public int Prop { get; set; } }

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

        var (dx, dy) = (i > 0 ? new Point { X = 10 } : new Point { X = 11 }, s ?? "x");
        Console.WriteLine($"{dx} {dy}");

        var pair = new Pair();
        (pair.A, pair.B) = (Next(), new Point { X = 12 });
        Console.WriteLine($"{pair.A} {pair.B}");

        var pairs = new Pair[2];
        (pairs[i].A, pairs[i].B, pairs[i + 1].Prop) = (Next(), new Point { X = 13 }, i > 0 ? 0 : 14);
        Console.WriteLine($"{pairs[0].A} {pairs[0].B} {pairs[1].Prop}");

        Unsafe.AsRef(in p) = new Point { X = 4 };
        Console.WriteLine(p.X);
    }
}
