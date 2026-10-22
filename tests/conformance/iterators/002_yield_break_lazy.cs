foreach (var x in Take(Naturals(), 5)) Console.Write(x + " ");
Console.WriteLine();
static IEnumerable<int> Naturals() { int i = 0; while (true) yield return i++; }
static IEnumerable<int> Take(IEnumerable<int> src, int n) { foreach (var x in src) { if (n-- <= 0) yield break; yield return x; } }
