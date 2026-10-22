var groups = new[] { 3, 1, 4, 1, 5, 9, 2, 6 }.GroupBy(n => n % 3).Select(g => (Key: g.Key, Sum: g.Sum(), Max: g.Max())).OrderBy(t => t.Key);
foreach (var (key, sum, max) in groups) Console.WriteLine($"{key}: {sum} {max}");
var dict = groups.ToDictionary(t => t.Key, t => t.Sum);
Console.WriteLine(dict[1]);
