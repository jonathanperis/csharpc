#:property AllowUnsafeBlocks=true
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct Pair { public int A; public long B; }
public struct Big { public long A, B, C; public double D; }

unsafe class P
{
    static int Add(int a, int b) => a + b;
    static ref int Pick(ref int a, ref int b, bool first) { if (first) return ref a; return ref b; }
    static void Out(out int v, in Pair p) { v = p.A + (int)p.B; }
    static Pair Make(int a, long b) => new Pair { A = a, B = b };
    static Big Grow(Big x, int k) { x.A += k; x.D *= 2; return x; }
    static string Cat(string a, object o) => a + o;
    static T Id<T>(T t) => t;
    static T Combine<T>(delegate*<T, T, T> f, T a, T b) => f(a, b);
    static double Scale(double d, float f, byte b, short s) => d * f + b + s;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static int Native(int x) => x * 7;
    [UnmanagedCallersOnly] static void NativeVoid(int* p) { *p += 5; }
    struct Holder { public delegate*<int, int, int> F; }
    static delegate*<int, int, int>[] table = { &Add, &Mul };
    static int Mul(int a, int b) => a * b;
    static delegate*<int, int, int> field = &Add;

    static void Main()
    {
        delegate*<int, int, int> add = &Add;
        Console.WriteLine(add(2, 3));
        int x = 1, y = 2;
        delegate*<ref int, ref int, bool, ref int> pick = &Pick;
        ref int r = ref pick(ref x, ref y, false);
        r = 99;
        Console.WriteLine($"{x} {y}");
        delegate*<out int, in Pair, void> o = &Out;
        o(out int ov, new Pair { A = 3, B = 4 });
        Console.WriteLine(ov);
        delegate*<int, long, Pair> make = &Make;
        var p = make(5, 6);
        Console.WriteLine($"{p.A} {p.B}");
        delegate*<Big, int, Big> grow = &Grow;
        var b = grow(new Big { A = 1, B = 2, C = 3, D = 4.5 }, 10);
        Console.WriteLine($"{b.A} {b.B} {b.C} {b.D}");
        delegate*<string, object, string> cat = &Cat;
        Console.WriteLine(cat("a", 42));
        delegate*<string, string> id = &Id<string>;
        Console.WriteLine(id("gen"));
        Console.WriteLine(Combine<int>(&Add, 3, 4));
        Console.WriteLine(Combine<string>(&Plus, "x", "y"));
        delegate*<double, float, byte, short, double> sc = &Scale;
        Console.WriteLine(sc(1.5, 2f, 3, -4));
        delegate* unmanaged[Cdecl]<int, int> nat = &Native;
        Console.WriteLine(nat(6));
        delegate* unmanaged<int*, void> nv = &NativeVoid;
        int q = 1; nv(&q); Console.WriteLine(q);
        var h = new Holder { F = &Mul };
        Console.WriteLine(h.F(6, 7));
        Console.WriteLine(table[0](1, 2) + table[1](3, 4));
        Console.WriteLine(field(10, 20));
        field = &Mul; Console.WriteLine(field(10, 20));
        nint raw = (nint)add;
        Console.WriteLine(((delegate*<int, int, int>)raw)(8, 9));
        Console.WriteLine(add != null);
        delegate*<int, int, int> nul = null;
        Console.WriteLine(nul == null);
        Console.WriteLine(Invoke(&Add, 1, 2));
    }
    static string Plus(string a, string b) => a + b;
    static int Invoke(delegate*<int, int, int> f, int a, int b) => f(a, b);
}
