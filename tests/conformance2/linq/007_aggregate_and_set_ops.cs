var a = new[] { 1, 2, 3, 4 }; var b = new[] { 3, 4, 5 };
Console.WriteLine(string.Join(",", a.Union(b)) + " | " + string.Join(",", a.Intersect(b)) + " | " + string.Join(",", a.Except(b)));
Console.WriteLine(a.Aggregate((x, y) => x * y) + " " + a.Aggregate(10, (acc, x) => acc + x) + " " + a.Aggregate("", (s, x) => s + x, s => s.Length));
Console.WriteLine($"{a.Average()} {a.Sum()} {a.Max()} {b.Min()} {a.Count(x => x > 1)}");
