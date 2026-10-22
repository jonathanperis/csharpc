var d = new Dictionary<(int, string), double> { [(1, "a")] = 1.5, [(2, "b")] = 2.5 };
Console.WriteLine(d[(2, "b")]);
Console.WriteLine(d.ContainsKey((1, "A")));
var counts = new Dictionary<(char, int), int>();
foreach (var w in new[] { "aa", "ab", "aa" }) { var k = (w[0], w.Length); counts[k] = counts.GetValueOrDefault(k) + 1; }
foreach (var kv in counts) Console.WriteLine($"{kv.Key} {kv.Value}");
