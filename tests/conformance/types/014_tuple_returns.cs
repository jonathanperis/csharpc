var r = MinMax(new[] { 4, 9, 1, 7 });
Console.WriteLine($"{r.min} {r.max}");
static (int min, int max) MinMax(int[] xs)
{
    int lo = int.MaxValue, hi = int.MinValue;
    foreach (var x in xs) { if (x < lo) lo = x; if (x > hi) hi = x; }
    return (lo, hi);
}
