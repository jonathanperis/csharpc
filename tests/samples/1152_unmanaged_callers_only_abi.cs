#:property AllowUnsafeBlocks=true
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [UnmanagedCallersOnly] methods are entered with the C ABI and `delegate* unmanaged` calls pass the C ABI, whatever
// the signature: structs go by value in registers (integer and SSE eightbytes), in memory
// (byval / sret) or as a pointer to a copy, depending on the platform. Both ends are checked against each other here
// (the native side is in sample 1151).
unsafe class P
{
    struct IntPair { public int A, B; }
    struct Mixed { public long Id; public double Value; }
    struct FloatPair { public float X, Y; }
    struct Vec2 { public double X, Y; }
    struct Big { public long A, B, C, D; public double E; }
    struct Tiny { public byte A; public short B; }

    [UnmanagedCallersOnly] static int Small(sbyte s, byte u, short h, ushort w) => s + u + h + w;
    [UnmanagedCallersOnly] static IntPair Swap(IntPair p) => new IntPair { A = p.B, B = p.A };
    [UnmanagedCallersOnly] static Mixed Bump(Mixed m, int by) => new Mixed { Id = m.Id + by, Value = m.Value * 2 };
    [UnmanagedCallersOnly] static FloatPair Scale(FloatPair p, float k) => new FloatPair { X = p.X * k, Y = p.Y * k };
    [UnmanagedCallersOnly] static Vec2 Add(Vec2 a, Vec2 b) => new Vec2 { X = a.X + b.X, Y = a.Y + b.Y };
    [UnmanagedCallersOnly] static Big Grow(Big b, Vec2 v, IntPair p, long extra) =>
        new Big { A = b.A + p.A, B = b.B + p.B, C = b.C + extra, D = b.D + (long)v.X, E = b.E + v.Y };
    [UnmanagedCallersOnly] static Tiny Pack(Tiny t) => new Tiny { A = (byte)(t.A + 1), B = (short)(t.B - 1) };
    [UnmanagedCallersOnly] static double Sum(IntPair a, IntPair b, IntPair c, Mixed m, Vec2 v) => a.A + a.B + b.A + b.B + c.A + c.B + m.Id + m.Value + v.X + v.Y;
    [UnmanagedCallersOnly] static int Plain(int x) => x * 3;

    static T Twice<T>(delegate* unmanaged<T, T> f, T x) where T : unmanaged => f(f(x));
    [UnmanagedCallersOnly] static Vec2 Double(Vec2 v) => new Vec2 { X = v.X * 2, Y = v.Y * 2 };

    static void Main()
    {
        Console.WriteLine(((delegate* unmanaged<sbyte, byte, short, ushort, int>)&Small)(-5, 200, -300, 60000));
        var swap = (delegate* unmanaged<IntPair, IntPair>)&Swap;
        var s = swap(new IntPair { A = 1, B = 2 });
        Console.WriteLine($"{s.A} {s.B}");
        var bump = (delegate* unmanaged<Mixed, int, Mixed>)&Bump;
        var m = bump(new Mixed { Id = 40, Value = 1.25 }, 2);
        Console.WriteLine($"{m.Id} {m.Value}");
        var scale = (delegate* unmanaged<FloatPair, float, FloatPair>)&Scale;
        var f = scale(new FloatPair { X = 1.5f, Y = -2f }, 4f);
        Console.WriteLine($"{f.X} {f.Y}");
        var add = (delegate* unmanaged<Vec2, Vec2, Vec2>)&Add;
        var v = add(new Vec2 { X = 1, Y = 2 }, new Vec2 { X = 10, Y = 20 });
        Console.WriteLine($"{v.X} {v.Y}");
        var grow = (delegate* unmanaged<Big, Vec2, IntPair, long, Big>)&Grow;
        var g = grow(new Big { A = 1, B = 2, C = 3, D = 4, E = 5.5 }, new Vec2 { X = 100, Y = 0.25 }, new IntPair { A = 7, B = 8 }, 1000);
        Console.WriteLine($"{g.A} {g.B} {g.C} {g.D} {g.E}");
        var pack = (delegate* unmanaged<Tiny, Tiny>)&Pack;
        var t = pack(new Tiny { A = 255, B = -32768 });
        Console.WriteLine($"{t.A} {t.B}");
        var sum = (delegate* unmanaged<IntPair, IntPair, IntPair, Mixed, Vec2, double>)&Sum;
        Console.WriteLine(sum(new IntPair { A = 1, B = 2 }, new IntPair { A = 3, B = 4 }, new IntPair { A = 5, B = 6 }, new Mixed { Id = 7, Value = 8.5 }, new Vec2 { X = 9, Y = 10 }));

        var twice = Twice<Vec2>(&Double, new Vec2 { X = 1.5, Y = 2.5 });
        Console.WriteLine($"{twice.X} {twice.Y}");
        Console.WriteLine(Twice<int>(&Plain, 7));

        // The same address, through an integer.
        nint address = (nint)(delegate* unmanaged<IntPair, IntPair>)&Swap;
        var again = ((delegate* unmanaged<IntPair, IntPair>)address)(new IntPair { A = 9, B = 8 });
        Console.WriteLine($"{again.A} {again.B}");
    }
}
