var people = new[] { ("ann", 30), ("bob", 25), ("cid", 30), ("dee", 25), ("eve", 40) };
foreach (var g in people.GroupBy(p => p.Item2).OrderBy(g => g.Key))
    Console.WriteLine(g.Key + ": " + string.Join(",", g.Select(p => p.Item1)));
Console.WriteLine(string.Join(",", people.OrderBy(p => p.Item2).ThenByDescending(p => p.Item1).Select(p => p.Item1)));
