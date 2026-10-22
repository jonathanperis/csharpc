foreach (var f in Fib().Take(10)) Console.Write(f + " ");
Console.WriteLine();
static IEnumerable<long> Fib() { long a = 0, b = 1; while (true) { yield return a; (a, b) = (b, a + b); } }
