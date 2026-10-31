// Lambdas converted to Expression<T>: the trees Roslyn builds (constant folding, enum promotion, string
// concatenation through string.Concat, implicit conversions, nullable lifting) and their ToString().
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

enum Color { Red, Green = 5, Blue }
enum Small : byte { A = 1, B = 2 }

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
    public int this[int i] => i + X;
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
}

static class Ext
{
    public static int Doubled(this int x) => x * 2;
}

class Program
{
    int inst = 7;

    static void Show(LambdaExpression e) => Console.WriteLine(e);

    static void Main()
    {
        // Arithmetic, constants (folded), checked, shifts, logic.
        Show((Expression<Func<int>>)(() => 1 + 2));
        Show((Expression<Func<int, int>>)(x => x + 1 + 2));
        Show((Expression<Func<int, int>>)(x => checked(x * 2 + 1)));
        Show((Expression<Func<int, int>>)(x => x % 3 / 2 - 1));
        Show((Expression<Func<int, int>>)(x => x << 2 >> 1));
        Show((Expression<Func<int, bool>>)(x => x > 1 || x < -1 & x != 0 ^ true));
        Show((Expression<Func<int, int>>)(x => -x));
        Show((Expression<Func<int, int>>)(x => ~x));
        Show((Expression<Func<bool, bool>>)(b => !b));
        Show((Expression<Func<int, int>>)(x => +x));

        // Conversions.
        Show((Expression<Func<long, int, long>>)((l, i) => l * i));
        Show((Expression<Func<double, int>>)(d => (int)d));
        Show((Expression<Func<double, int>>)(d => checked((int)d)));
        Show((Expression<Func<int, object>>)(x => x));
        Show((Expression<Func<object, string>>)(o => (string)o));
        Show((Expression<Func<object, string>>)(o => o as string));
        Show((Expression<Func<object, bool>>)(o => o is string));
        Show((Expression<Func<char, double, float, decimal, bool>>)((ch, db, fl, de) => ch == 'a' && db > 1.5 && fl < 2.5f && de != 1.5m));

        // Enums.
        Show((Expression<Func<Color, bool>>)(c => c == Color.Green));
        Show((Expression<Func<Color, int>>)(c => (int)c + 1));
        Show((Expression<Func<Color, Color>>)(c => c | Color.Blue));
        Show((Expression<Func<Small, bool>>)(s => s == Small.B));
        Show((Expression<Func<Color?, bool>>)(c => c == Color.Red));

        // Nullable lifting.
        Show((Expression<Func<int?, int, int?>>)((n, i) => n + i));
        Show((Expression<Func<int?, bool>>)(n => n > 3));
        Show((Expression<Func<int?, int>>)(n => n ?? -1));
        Show((Expression<Func<int?, bool>>)(n => n == null));

        // Strings.
        Show((Expression<Func<string, string, string>>)((s, t) => s + t + 1));
        Show((Expression<Func<string, bool>>)(s => s == "x" || s.Length > 2 && s.StartsWith("G")));
        Show((Expression<Func<string, string>>)(s => s ?? "none"));
        Show((Expression<Func<string>>)(() => "q\"uote"));
        Show((Expression<Func<string>>)(() => null));

        // Members, calls, indexers.
        Show((Expression<Func<Pt, int>>)(p => p.X + p.Y + Pt.S + p.M(2) + Pt.Twice(p.X)));
        Show((Expression<Func<Pt, int>>)(p => p.Next.Next.X));
        Show((Expression<Func<Pt, string>>)(p => p.Echo("hi") + p.Echo<int>(3)));
        Show((Expression<Func<Pt, int>>)(p => p[4]));
        Show((Expression<Func<int, int>>)(x => x.Doubled()));
        Show((Expression<Func<IEnumerable<int>, int>>)(xs => xs.Where(x => x > 1).Select(x => x * x).Sum()));
        Show((Expression<Func<List<int>, int>>)(l => l[0] + l.Count));
        Show((Expression<Func<int, string>>)(x => x.ToString()));
        Show((Expression<Func<double, double>>)(d => Math.Abs(d) + Math.Max(d, 1.0)));

        // User-defined operators and conversions.
        Show((Expression<Func<Money, Money, Money>>)((a, b) => a + b));
        Show((Expression<Func<Money, Money, bool>>)((a, b) => a == b));
        Show((Expression<Func<Money, decimal>>)(m => m));
        Show((Expression<Func<int, Money>>)(i => (Money)i));
        Show((Expression<Func<Money?, decimal?>>)(m => m));

        // Conditionals, creation, arrays, initializers.
        Show((Expression<Func<int, string>>)(x => x > 0 ? "pos" : "neg"));
        Show((Expression<Func<bool, object>>)(b => b ? "s" : (object)1));
        Show((Expression<Func<Pt>>)(() => new Pt(1) { Y = 2, L = { 3, 4 } }));
        Show((Expression<Func<Pt>>)(() => new Pt { X = 1, Next = new Pt { Y = 2 } }));
        Show((Expression<Func<Dictionary<string, int>>>)(() => new Dictionary<string, int> { { "a", 1 }, { "b", 2 } }));
        Show((Expression<Func<int[]>>)(() => new[] { 1, 2, 3 }));
        Show((Expression<Func<int, int[]>>)(n => new int[n]));
        Show((Expression<Func<int[], long, int>>)((arr, i) => arr[0] + arr[i] + arr.Length));
        Show((Expression<Func<Money>>)(() => new Money()));
        Show((Expression<Func<DateTime>>)(() => default(DateTime)));
        Show((Expression<Func<Type>>)(() => typeof(int)));
        Show((Expression<Func<int>>)(() => default(int)));

        // Delegates and nested lambdas.
        Show((Expression<Func<Func<int, int>, int>>)(fn => fn(3)));
        Show((Expression<Func<int, Func<int, int>>>)(x => y => x + y));
        Show((Expression<Func<int, Expression<Func<int, int>>>>)(x => y => x + y));
        Show((Expression<Action<int>>)(x => Console.WriteLine(x)));

        // Natural types (C# 10): Expression and LambdaExpression targets.
        Expression natural = (int a) => a * 2;
        Console.WriteLine(natural);
        LambdaExpression lambda = (string s) => s.Length;
        Console.WriteLine(lambda + " : " + lambda.Type);

        // Captured variables and this.
        int y = 10;
        Show((Expression<Func<int, int>>)(x => x + y));
        new Program().Inst(5);

        // The tree's shape.
        Expression<Func<int, bool>> tree = num => num < 5;
        var body = (BinaryExpression)tree.Body;
        Console.WriteLine($"{tree.NodeType} {tree.Type} {tree.ReturnType} {tree.Parameters.Count} {tree.Name ?? "<null>"} {tree.TailCall}");
        Console.WriteLine($"{body.NodeType} {body.Left.NodeType} {body.Right.NodeType} {body.Type} {body.Method?.Name ?? "<null>"}");
        Console.WriteLine($"{((ParameterExpression)body.Left).Name} {((ConstantExpression)body.Right).Value} {((ConstantExpression)body.Right).Type}");
        Expression<Func<string, string, string>> concat = (a, b) => a + b;
        Console.WriteLine(((BinaryExpression)concat.Body).Method);
        Expression<Func<Pt, int>> member = p => p.X + p.Y;
        var add = (BinaryExpression)member.Body;
        Console.WriteLine($"{((MemberExpression)add.Left).Member.MemberType} {((MemberExpression)add.Left).Member.Name} {((MemberExpression)add.Right).Member.MemberType} {((MemberExpression)add.Right).Member.Name}");
    }

    void Inst(int p)
    {
        Show((Expression<Func<int>>)(() => inst + 1));
        int loc = 3;
        Show((Expression<Func<int>>)(() => inst + loc + p));
    }
}
