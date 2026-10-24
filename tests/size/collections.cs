var list = new List<int> { 5, 3, 8, 1 };
list.Sort();
var map = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
Console.WriteLine(string.Join(",", list) + " " + map["b"]);
