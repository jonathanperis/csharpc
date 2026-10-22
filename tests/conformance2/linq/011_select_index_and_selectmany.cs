Console.WriteLine(string.Join(" ", new[] { "a", "b", "c" }.Select((s, i) => $"{i}{s}")));
Console.WriteLine(string.Join(" ", new[] { "ab", "cd" }.SelectMany(s => s.ToCharArray(), (s, c) => $"{s}:{c}")));
Console.WriteLine(new[] { 1, 2 }.SelectMany(x => Enumerable.Repeat(x, x)).Sum());
