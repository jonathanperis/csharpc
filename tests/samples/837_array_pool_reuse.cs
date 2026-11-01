// ArrayPool<T>.Shared keeps returned arrays: the next Rent of the same bucket on the same thread
// gets the same array, with what its last user left in it unless it was returned with clearArray;
// other buckets, sizes over the pooled range and ArrayPool.Create() pools behave as .NET's.
using System;
using System.Buffers;

static class Program
{
    static void Main()
    {
        var pool = ArrayPool<byte>.Shared;
        byte[] a = pool.Rent(1000);
        Console.WriteLine(a.Length);
        a[0] = 42; a[999] = 7;
        pool.Return(a);
        byte[] b = pool.Rent(900);
        Console.WriteLine($"{ReferenceEquals(a, b)} {b.Length} {b[0]} {b[999]}");
        pool.Return(b, clearArray: true);
        byte[] c = pool.Rent(1024);
        Console.WriteLine($"{ReferenceEquals(a, c)} {c[0]} {c[999]}");
        byte[] d = pool.Rent(1024);
        Console.WriteLine($"{ReferenceEquals(c, d)} {d.Length}");
        pool.Return(c);
        pool.Return(d);

        var strings = ArrayPool<string>.Shared;
        string[] s = strings.Rent(20);
        s[3] = "kept";
        strings.Return(s);
        string[] t = strings.Rent(32);
        Console.WriteLine($"{ReferenceEquals(s, t)} {t.Length} {t[3]}");

        try { pool.Return(new byte[1000]); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
        Console.WriteLine(pool.Rent(0).Length);

        var own = ArrayPool<int>.Create();
        int[] x = own.Rent(100);
        x[5] = 5;
        own.Return(x);
        int[] y = own.Rent(128);
        Console.WriteLine($"{y.Length} {ReferenceEquals(x, y)}");
    }
}
