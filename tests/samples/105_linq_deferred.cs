// Deferred execution: side effects happen at enumeration time, in .NET's order.
var log = new List<string>();
var source = new List<int> { 1, 2, 3, 4, 5, 6 };
var query = source.Where(x => { log.Add("w" + x); return x % 2 == 0; })
                  .Select(x => { log.Add("s" + x); return x * 10; });
log.Add("built");
source.Add(8);
foreach (var v in query) log.Add("got" + v);
Console.WriteLine(string.Join(" ", log));
log.Clear();
var first = query.First();
Console.WriteLine(first + " " + string.Join(" ", log));
log.Clear();
Console.WriteLine(query.Take(2).Sum() + " " + string.Join(" ", log));
var ordered = source.OrderByDescending(x => x % 3).ThenBy(x => -x).ToList();
Console.WriteLine(string.Join(",", ordered));
var lookup = source.ToLookup(x => x % 3);
Console.WriteLine(lookup.Count + " " + string.Join(",", lookup[0]) + " " + lookup.Contains(5) + " " + string.Join(",", lookup[2]));
var joined = new[] { "a1", "b2", "c3" }.Join(new[] { 1, 3, 4 }, s => s[1] - '0', n => n, (s, n) => s + ":" + n);
Console.WriteLine(string.Join(" ", joined));
var nested = new List<List<int>> { new() { 1, 2 }, new() { 3 }, new() };
Console.WriteLine(string.Join(",", nested.SelectMany(l => l)) + " " + nested.Count(l => l.Any()) + " " + nested.Sum(l => l.Count));
var doubles = new List<double> { 2.5, double.NaN, -1, 8 };
Console.WriteLine(doubles.Max() + " " + doubles.Min() + " " + doubles.Where(d => !double.IsNaN(d)).Average());
var empty = new List<int>();
Console.WriteLine(empty.FirstOrDefault() + " " + empty.DefaultIfEmpty(-1).First() + " " + empty.Any() + " " + empty.Sum());
Console.WriteLine(string.Join(",", Enumerable.Range(1, 10).Where(i => i % 3 == 0).Reverse()) + " " + Enumerable.Range(1, 5).Aggregate(1, (a, b) => a * b));
Console.WriteLine(string.Join(",", new[] { "b", "a", "c" }.Order()) + " " + string.Join(",", new[] { 3, 1, 2 }.OrderDescending()));
