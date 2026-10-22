var nested = new[] { new[] { 1, 2 }, new[] { 3 } };
Console.WriteLine(string.Join(",", nested.SelectMany(x => x)));
var q = from n in Enumerable.Range(1, 5) let sq = n * n where sq > 5 select sq;
Console.WriteLine(string.Join(",", q));
