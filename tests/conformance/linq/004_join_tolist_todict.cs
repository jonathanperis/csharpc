var a = new[] { (1, "one"), (2, "two") };
var b = new[] { (1, "uno"), (2, "dos") };
foreach (var x in a.Join(b, l => l.Item1, r => r.Item1, (l, r) => l.Item2 + "=" + r.Item2)) Console.WriteLine(x);
var d = a.ToDictionary(t => t.Item1, t => t.Item2);
Console.WriteLine(d[2] + " " + a.Select(t => t.Item2).ToList().Count);
