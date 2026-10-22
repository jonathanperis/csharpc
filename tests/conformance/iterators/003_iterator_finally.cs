foreach (var x in Gen()) { Console.WriteLine(x); if (x == 2) break; }
static IEnumerable<int> Gen() { try { yield return 1; yield return 2; yield return 3; } finally { Console.WriteLine("cleanup"); } }
