var people = new[] { ("bob", 30), ("amy", 25), ("cat", 30), ("dan", 25), ("eve", 30) };
foreach (var p in people.OrderBy(p => p.Item2)) Console.Write(p.Item1 + " ");
Console.WriteLine();
foreach (var p in people.OrderByDescending(p => p.Item2).ThenBy(p => p.Item1)) Console.Write(p.Item1 + " ");
Console.WriteLine();
