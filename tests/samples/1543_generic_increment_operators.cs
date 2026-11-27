// Generic operators from #425: ++/-- through T (user types, primitives, Int128, BigInteger, decimal), a local function over the
// enclosing method's T, and checked unary minus on Int128.
using System;
using System.Globalization;
using System.Numerics;

readonly struct Counter : IIncrementOperators<Counter>, IDecrementOperators<Counter>
{
    public readonly int Value;
    public Counter(int value) => Value = value;
    public static Counter operator ++(Counter c) => new(c.Value + 10);
    public static Counter operator --(Counter c) => new(c.Value - 1);
    public override string ToString() => "C" + Value;
}

static class Program
{
    static (T, T, T, T) Steps<T>(T x) where T : IIncrementOperators<T>, IDecrementOperators<T>
    {
        var post = x++;
        var pre = ++x;
        var down = x--;
        return (post, pre, down, --x);
    }

    static T Negate<T>(T x) where T : IUnaryNegationOperators<T, T> => checked(-x);

    static void Show<T>(T a, T b) where T : INumberBase<T>
    {
        string Text(T v) => v.ToString(null, CultureInfo.InvariantCulture);
        Console.WriteLine($"{typeof(T).Name}: {Text(a)} + {Text(b)} = {Text(a + b)}");
    }

    static void Main()
    {
        Console.WriteLine(Steps(new Counter(1)));
        Console.WriteLine(Steps(5));
        Console.WriteLine(Steps(2.5));
        Console.WriteLine(Steps((Int128)7));
        Console.WriteLine(Steps(new BigInteger(9)));
        Console.WriteLine(Steps(1.5m));
        Show(1.25, 2.5);
        Show((Int128)40, (Int128)2);
        Console.WriteLine(Negate((Int128)5));
        try { Console.WriteLine(Negate(Int128.MinValue)); }
        catch (OverflowException e) { Console.WriteLine(e.GetType().Name); }
        Console.WriteLine(Negate(-3));
    }
}
