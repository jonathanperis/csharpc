// ValueTuple: tuple literals, named elements, returning tuples, deconstruction, equality.
(int, string) pair = (1, "one");
Console.WriteLine(pair.Item1);
Console.WriteLine(pair.Item2);
Console.WriteLine(pair);

var named = (Name: "Ada", Age: 36);
Console.WriteLine($"{named.Name} is {named.Age}");
named.Age++;
Console.WriteLine(named);

var (min, max) = MinMax(new[] { 4, -2, 9, 7, 0 });
Console.WriteLine($"min={min} max={max}");
(int q, int r) = DivRem(17, 5);
Console.WriteLine($"17 = 5 * {q} + {r}");
var stats = Stats(new[] { 1.0, 2.0, 3.0, 4.0 });
Console.WriteLine($"mean={stats.Mean} total={stats.Total} count={stats.Count}");

(long, double) widened = (1, 2);          // element-wise implicit conversions
Console.WriteLine(widened);
(string?, int) withNull = (null, 3);
Console.WriteLine(withNull.Item1 == null);

int a = 1, b = 2;
(a, b) = (b, a);
Console.WriteLine($"{a} {b}");
var nested = (1, (2.5, "deep"));
Console.WriteLine(nested.Item2.Item2);
Console.WriteLine(nested);

Console.WriteLine((1, "x") == (1, "x"));
Console.WriteLine(pair != (1, "two"));
var t3 = (true, 'c', 3u);
Console.WriteLine(t3);
var copy = named;
copy.Name = "Grace";
Console.WriteLine($"{named.Name} {copy.Name}");

static (int Min, int Max) MinMax(int[] values)
{
    int lo = int.MaxValue, hi = int.MinValue;
    foreach (var v in values)
    {
        if (v < lo) lo = v;
        if (v > hi) hi = v;
    }
    return (lo, hi);
}

static (int, int) DivRem(int x, int y) => (x / y, x % y);

static (double Mean, double Total, int Count) Stats(double[] xs)
{
    double total = 0;
    foreach (var x in xs) total += x;
    return (total / xs.Length, total, xs.Length);
}
