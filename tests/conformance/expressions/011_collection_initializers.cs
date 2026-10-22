var list = new List<int> { 1, 2, 3 };
var dict = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
var d2 = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };
Console.WriteLine(list.Count + dict["b"] + d2[2]);
