static IEnumerable<int> Gen() { try { yield return 1; yield return 2; yield return 3; } finally { Console.WriteLine("cleanup"); } }
foreach (var x in Gen()) { Console.WriteLine(x); if (x == 2) break; }
using (var en = Gen().GetEnumerator()) { en.MoveNext(); Console.WriteLine("first " + en.Current); }
Console.WriteLine(Gen().First());
