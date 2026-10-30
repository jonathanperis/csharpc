// Expression trees in generic methods and types (types substituted per instantiation), static fields read
// live, struct receivers, and a compiled tree used as an ordinary delegate.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

struct Vec
{
    public int X;
    public int Plus(int y) => X + y;
}

class Holder<T>
{
    public T Value;
    public Expression<Func<T>> Get() => () => Value;
}

static class Program
{
    static int counter = 3;

    static Expression<Func<T, T>> Identity<T>() => x => x;
    static Expression<Func<List<T>, int>> Count<T>() => l => l.Count;
    static Expression<Func<T, string>> Show<T>() where T : class => x => x == null ? "null" : x.ToString();

    static void Main()
    {
        Console.WriteLine(Identity<int>());
        Console.WriteLine(Identity<string>().Compile()("s"));
        Console.WriteLine(Count<double>() + " " + Count<double>().Compile()(new List<double> { 1, 2 }));
        Console.WriteLine(Show<string>() + " " + Show<string>().Compile()(null));
        Expression<Func<int>> s = () => counter * 2;
        Console.WriteLine(s + " " + s.Compile()());
        counter = 10;
        Console.WriteLine(s.Compile()());
        var v = new Vec { X = 4 };
        Expression<Func<Vec, int>> vt = w => w.Plus(2) + w.X;
        Console.WriteLine(vt + " " + vt.Compile()(v));
        var h = new Holder<string> { Value = "held" };
        Console.WriteLine(h.Get() + " " + h.Get().Compile()());
        Expression<Func<string, int>> len = x => x.Length;
        Console.WriteLine(string.Join(",", new[] { "a", "bbb" }.Select(len.Compile())));
    }
}
