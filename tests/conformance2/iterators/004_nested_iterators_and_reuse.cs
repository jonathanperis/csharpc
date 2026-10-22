static IEnumerable<int> Inner(int n) { for (int i = 0; i < n; i++) yield return i; }
static IEnumerable<string> Outer() { foreach (var n in new[] { 1, 2, 3 }) foreach (var i in Inner(n)) yield return $"{n}:{i}"; }
var seq = Outer();
Console.WriteLine(string.Join(" ", seq));
Console.WriteLine(seq.Count());
