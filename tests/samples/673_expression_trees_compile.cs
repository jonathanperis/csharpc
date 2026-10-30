// Expression<T>.Compile() runs the tree through System.Linq.Expressions' interpreter (as NativeAOT does):
// results, live captured variables, exceptions, lifted operators, calls, creation and initializers.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

enum Color { Red, Green = 5, Blue }

class Pt
{
    public int X;
    public int Y { get; set; }
    public List<int> L = new();
    public Pt Next;
    public static int S = 3;
    public Pt() { }
    public Pt(int x) { X = x; }
    public int M(int a) => a + X;
    public static int Twice(int a) => a * 2;
    public T Echo<T>(T value) => value;
    public override string ToString() => $"Pt({X},{Y},[{string.Join(",", L)}])";
}

struct Money
{
    public decimal Amount;
    public Money(decimal amount) { Amount = amount; }
    public static Money operator +(Money a, Money b) => new Money(a.Amount + b.Amount);
    public static bool operator ==(Money a, Money b) => a.Amount == b.Amount;
    public static bool operator !=(Money a, Money b) => a.Amount != b.Amount;
    public static implicit operator decimal(Money m) => m.Amount;
    public static explicit operator Money(int v) => new Money(v);
    public override bool Equals(object o) => o is Money m && m.Amount == Amount;
    public override int GetHashCode() => Amount.GetHashCode();
    public override string ToString() => "$" + Amount;
}

static class Ext
{
    public static int Doubled(this int x) => x * 2;
}

class Counter
{
    public int Count;
    public Expression<Func<int, int>> Adder() => x => x + Count;
}

class Program
{
    static void Main()
    {
        Expression<Func<int, int, int>> add = (a, b) => a * 10 + b;
        Console.WriteLine(add.Compile()(4, 2));
        Console.WriteLine(((Expression<Func<int>>)(() => 1 + 2)).Compile()());
        Console.WriteLine(((Expression<Func<long, int, long>>)((l, i) => l * i)).Compile()(3000000000, 3));
        Console.WriteLine(((Expression<Func<double, int>>)(d => (int)d)).Compile()(-7.9));
        Console.WriteLine(((Expression<Func<int, int, int>>)((x, y) => x / y + x % y)).Compile()(17, 5));
        Console.WriteLine(((Expression<Func<uint, int, uint>>)((x, s) => x >> s)).Compile()(0x80000000u, 4));
        Console.WriteLine(((Expression<Func<int, bool>>)(x => x > 1 && x < 10 || x == -1)).Compile()(-1));
        Console.WriteLine(((Expression<Func<double, double>>)(d => Math.Sqrt(d) + Math.Max(d, 1.0))).Compile()(16));
        Console.WriteLine(((Expression<Func<float, float, float>>)((a, b) => a / b)).Compile()(1f, 3f));
        Console.WriteLine(((Expression<Func<decimal, decimal, decimal>>)((a, b) => a * b - 0.5m)).Compile()(1.5m, 3m));
        Console.WriteLine(((Expression<Func<decimal, decimal, bool>>)((a, b) => a != b)).Compile()(1.5m, 1.50m));
        Console.WriteLine(((Expression<Func<char, int>>)(c => c + 1)).Compile()('A'));

        // Checked arithmetic and other exceptions surface as in .NET.
        var mul = ((Expression<Func<int, int>>)(x => checked(x * 2))).Compile();
        try { Console.WriteLine(mul(int.MaxValue)); } catch (OverflowException e) { Console.WriteLine("Overflow: " + e.Message); }
        var div = ((Expression<Func<int, int>>)(x => 10 / x)).Compile();
        try { Console.WriteLine(div(0)); } catch (DivideByZeroException e) { Console.WriteLine("DivideByZero: " + e.Message); }
        var cast = ((Expression<Func<object, string>>)(o => (string)o)).Compile();
        try { Console.WriteLine(cast(5)); } catch (InvalidCastException) { Console.WriteLine("InvalidCast"); }
        var len = ((Expression<Func<string, int>>)(s => s.Length)).Compile();
        try { Console.WriteLine(len(null)); } catch (NullReferenceException) { Console.WriteLine("NullReference"); }
        var thrower = ((Expression<Func<int, int>>)(x => int.Parse("x" + x))).Compile();
        try { Console.WriteLine(thrower(1)); } catch (FormatException e) { Console.WriteLine("Format: " + e.GetType().Name); }

        // Enums and nullable lifting.
        Console.WriteLine(((Expression<Func<Color, bool>>)(c => c == Color.Green)).Compile()(Color.Green));
        Console.WriteLine(((Expression<Func<Color, Color>>)(c => c | Color.Blue)).Compile()(Color.Green));
        Console.WriteLine(((Expression<Func<Color, int>>)(c => (int)c + 1)).Compile()(Color.Blue));
        var lifted = ((Expression<Func<int?, int, int?>>)((n, i) => n + i)).Compile();
        Console.WriteLine($"{lifted(2, 3)} [{lifted(null, 3)}]");
        var cmp = ((Expression<Func<int?, bool>>)(n => n > 3)).Compile();
        Console.WriteLine($"{cmp(5)} {cmp(null)}");
        var coalesce = ((Expression<Func<int?, int>>)(n => n ?? -1)).Compile();
        Console.WriteLine($"{coalesce(4)} {coalesce(null)}");
        var isNull = ((Expression<Func<int?, bool>>)(n => n == null)).Compile();
        Console.WriteLine($"{isNull(null)} {isNull(0)}");

        // Strings.
        var concat = ((Expression<Func<string, string, string>>)((s, t) => s + t + 1)).Compile();
        Console.WriteLine(concat("a", null));
        var pred = ((Expression<Func<string, bool>>)(s => s == "x" || s.Length > 2 && s.StartsWith("G"))).Compile();
        Console.WriteLine($"{pred("x")} {pred("Go")} {pred("Gold")} {pred("Bold")}");
        Console.WriteLine(((Expression<Func<string, string>>)(s => s ?? "none")).Compile()(null));
        Console.WriteLine(((Expression<Func<int, string>>)(x => x.ToString() + "!")).Compile()(42));

        // Members and calls (static, instance, generic, extension, indexer, LINQ).
        var p = new Pt(4) { Y = 5, Next = new Pt(6) { Next = new Pt(7) } };
        Console.WriteLine(((Expression<Func<Pt, int>>)(q => q.X + q.Y + Pt.S + q.M(2) + Pt.Twice(q.X))).Compile()(p));
        Console.WriteLine(((Expression<Func<Pt, int>>)(q => q.Next.Next.X)).Compile()(p));
        Console.WriteLine(((Expression<Func<Pt, string>>)(q => q.Echo("hi") + q.Echo<int>(3))).Compile()(p));
        Console.WriteLine(((Expression<Func<int, int>>)(x => x.Doubled())).Compile()(21));
        Console.WriteLine(((Expression<Func<IEnumerable<int>, int>>)(xs => xs.Where(x => x > 1).Select(x => x * x).Sum())).Compile()(new[] { 1, 2, 3 }));
        Console.WriteLine(((Expression<Func<List<int>, int>>)(l => l[0] + l.Count)).Compile()(new List<int> { 10, 20 }));
        Console.WriteLine(((Expression<Func<Dictionary<string, int>, int>>)(d => d["b"])).Compile()(new Dictionary<string, int> { ["b"] = 9 }));

        // User-defined operators and conversions.
        Console.WriteLine(((Expression<Func<Money, Money, Money>>)((a, b) => a + b)).Compile()(new Money(1.25m), new Money(2)));
        Console.WriteLine(((Expression<Func<Money, Money, bool>>)((a, b) => a == b)).Compile()(new Money(2), new Money(2.0m)));
        Console.WriteLine(((Expression<Func<Money, decimal>>)(m => m)).Compile()(new Money(3.5m)));
        Console.WriteLine(((Expression<Func<int, Money>>)(i => (Money)i)).Compile()(8));

        // Conditionals, creation, arrays, initializers.
        var sign = ((Expression<Func<int, string>>)(x => x > 0 ? "pos" : x < 0 ? "neg" : "zero")).Compile();
        Console.WriteLine($"{sign(3)} {sign(-3)} {sign(0)}");
        Console.WriteLine(((Expression<Func<Pt>>)(() => new Pt(1) { Y = 2, L = { 3, 4 } })).Compile()());
        Console.WriteLine(((Expression<Func<Pt>>)(() => new Pt { X = 1, Next = new Pt { Y = 2 } })).Compile()().Next);
        var dict = ((Expression<Func<Dictionary<string, int>>>)(() => new Dictionary<string, int> { { "a", 1 }, { "b", 2 } })).Compile()();
        Console.WriteLine(string.Join(";", dict.Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(",", ((Expression<Func<int[]>>)(() => new[] { 1, 2, 3 })).Compile()()));
        Console.WriteLine(((Expression<Func<int, int[]>>)(n => new int[n])).Compile()(4).Length);
        Console.WriteLine(((Expression<Func<string[], int, string>>)((arr, i) => arr[i] + arr.Length)).Compile()(new[] { "a", "b" }, 1));
        Console.WriteLine(((Expression<Func<object, bool>>)(o => o is string)).Compile()("s"));
        Console.WriteLine(((Expression<Func<object, string>>)(o => o as string)).Compile()(1) ?? "null");
        Console.WriteLine(((Expression<Func<Money>>)(() => new Money())).Compile()());
        Console.WriteLine(((Expression<Func<Type>>)(() => typeof(int))).Compile()());

        // Delegates, nested lambdas, quotes.
        Console.WriteLine(((Expression<Func<Func<int, int>, int>>)(fn => fn(3))).Compile()(v => v * 7));
        var curried = ((Expression<Func<int, Func<int, int>>>)(x => y => x + y)).Compile();
        Console.WriteLine(curried(10)(5));
        var quoted = ((Expression<Func<int, Expression<Func<int, int>>>>)(x => y => x + y)).Compile();
        var inner = quoted(100);
        Console.WriteLine(inner + " -> " + inner.Compile()(1));
        ((Expression<Action<string>>)(s => Console.WriteLine("action " + s))).Compile()("ran");

        // Captured variables are read live, also by the compiled delegate.
        int captured = 10;
        Expression<Func<int, int>> plus = x => x + captured;
        var plusFn = plus.Compile();
        captured = 20;
        Console.WriteLine(plusFn(1) + " " + plus.Compile()(1));
        var counter = new Counter { Count = 3 };
        var adder = counter.Adder().Compile();
        counter.Count = 4;
        Console.WriteLine(adder(1));
        for (int i = 0; i < 3; i++)
        {
            int copy = i * i;
            Expression<Func<int>> read = () => copy + i;
            Console.WriteLine(read.Compile()());
        }

        // DynamicInvoke and LambdaExpression.Compile.
        LambdaExpression untyped = add;
        Console.WriteLine(untyped.Compile().DynamicInvoke(1, 2));
        Console.WriteLine(add.Compile(preferInterpretation: true)(5, 6));
    }
}
