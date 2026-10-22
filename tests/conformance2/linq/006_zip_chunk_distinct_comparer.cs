Console.WriteLine(string.Join(" ", new[] { 1, 2, 3 }.Zip(new[] { "a", "b" }, (n, s) => s + n)));
foreach (var c in Enumerable.Range(1, 7).Chunk(3)) Console.Write("[" + string.Join(",", c) + "]");
Console.WriteLine();
Console.WriteLine(string.Join(",", new[] { "A", "a", "B", "b", "a" }.Distinct(StringComparer.OrdinalIgnoreCase)));
Console.WriteLine(string.Join(",", new[] { 3, 1, 3, 2, 1 }.Distinct()));
