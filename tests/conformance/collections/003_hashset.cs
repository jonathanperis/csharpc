var a = new HashSet<int> { 1, 2, 3 };
var b = new HashSet<int> { 2, 3, 4 };
Console.WriteLine(a.Add(1) + " " + a.Add(5));
a.IntersectWith(b);
Console.WriteLine(string.Join(",", a.Order()));
a.UnionWith(new[] { 9 });
Console.WriteLine(a.Count + " " + a.Contains(9));
