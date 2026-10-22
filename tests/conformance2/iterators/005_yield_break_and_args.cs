static IEnumerable<int> Take(IEnumerable<int> src, int n) { if (n <= 0) yield break; foreach (var x in src) { yield return x; if (--n == 0) yield break; } }
static IEnumerable<int> Nat() { int i = 0; while (true) yield return i++; }
Console.WriteLine(string.Join(",", Take(Nat(), 5)));
Console.WriteLine(Take(Nat(), 0).Any());
