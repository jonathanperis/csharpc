var d = new Dictionary<string, List<int>> { ["a"] = new() { 1, 2 }, ["b"] = new List<int> { 3 } };
Console.WriteLine(string.Join(";", d.Select(kv => $"{kv.Key}={string.Join(",", kv.Value)}")));
var nested = new Node { Children = { new Node { Name = "c1" }, new Node { Name = "c2" } }, Name = "root" };
Console.WriteLine(nested.Name + ":" + string.Join(",", nested.Children.Select(c => c.Name)));
var d2 = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };
Console.WriteLine(d2[2]);
class Node { public string Name = ""; public List<Node> Children { get; } = new(); }
