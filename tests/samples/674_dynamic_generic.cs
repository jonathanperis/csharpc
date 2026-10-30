// `dynamic` in generic code (each instantiation binds with its own type arguments, as the binder sees them),
// lambdas, local functions, iterators, async methods, field initializers and nullable conversions.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

class Box<T>
{
    public T Value;
    public Box(T value) { Value = value; }
    // `Value` is statically T: the binder sees the instantiation's type argument.
    public dynamic Twice() => (dynamic)Value + (dynamic)Value;
    public string Describe(dynamic other) => Show(Value, other);
    static string Show(T mine, int other) => $"{typeof(T).Name}+int:{mine}/{other}";
    static string Show(T mine, string other) => $"{typeof(T).Name}+string:{mine}/{other}";
    static string Show(T mine, object other) => $"{typeof(T).Name}+object:{mine}/{other}";
}

class Counter
{
    public event EventHandler Ticked;
    public int Count;
    public void Tick() { Count++; Ticked?.Invoke(this, EventArgs.Empty); }
}

class Program
{
    static dynamic s_seed = 10;
    static readonly int s_doubled = s_seed * 2;

    static T Add<T>(T a, T b) => (dynamic)a + b;
    static dynamic Sum<T>(IEnumerable<T> items)
    {
        dynamic total = default(T);
        foreach (var item in items) total += item;
        return total;
    }

    static IEnumerable<dynamic> Doubles(dynamic start)
    {
        for (var i = 0; i < 3; i++) yield return start * (i + 1);
    }

    static async Task<dynamic> Later(dynamic value)
    {
        await Task.Yield();
        return value + 1;
    }

    static void Main()
    {
        Console.WriteLine(Add(2, 3));
        Console.WriteLine(Add(2.5, 0.25));
        Console.WriteLine(Add("a", "b"));
        Console.WriteLine(Add(1L, 2L).GetType().Name);
        Console.WriteLine(Sum(new[] { 1, 2, 3 }));
        Console.WriteLine(Sum(new List<double> { 0.5, 0.25 }));
        Console.WriteLine(Sum(new[] { 1.5m, 2m }));
        Console.WriteLine(new Box<int>(21).Twice());
        Console.WriteLine(new Box<string>("ab").Twice());
        Console.WriteLine(new Box<int>(1).Describe(2));
        Console.WriteLine(new Box<int>(1).Describe("s"));
        Console.WriteLine(new Box<string>("x").Describe(3.5));

        Func<dynamic, dynamic, dynamic> mul = (a, b) => a * b;
        Console.WriteLine(mul(6, 7));
        Console.WriteLine(mul(1.5, 2));
        Console.WriteLine(mul(2, 3L).GetType().Name);
        dynamic Local(dynamic x) => x.ToString() + "!";
        Console.WriteLine(Local(5) + Local("s"));
        foreach (var d in Doubles(1.5)) Console.Write(d + " ");
        Console.WriteLine();
        Console.WriteLine(Later(41).Result);
        Console.WriteLine(s_doubled);

        // Nullable targets, `is`/`as`, `??`, events through dynamic.
        dynamic maybe = 5;
        int? n = maybe;
        Console.WriteLine(n);
        maybe = null;
        n = maybe;
        Console.WriteLine(n.HasValue);
        dynamic text = "str";
        Console.WriteLine(text is string);
        Console.WriteLine((text as string)?.Length);
        Console.WriteLine(maybe ?? "fallback");
        dynamic counter = new Counter();
        EventHandler handler = (s, e) => Console.WriteLine("ticked " + ((Counter)s).Count);
        counter.Ticked += handler;
        counter.Tick();
        counter.Ticked -= handler;
        counter.Tick();
        Console.WriteLine(counter.Count);
        dynamic values = new List<int> { 1, 2 };
        values[0]++;
        values[1] += values[0];
        Console.WriteLine(values[0] + "," + values[1]);
    }
}
