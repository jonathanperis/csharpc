var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
d.Remove("b");
d["e"] = 5;
d.Remove("d");
d["f"] = 6;
Console.WriteLine(string.Join(",", d.Keys));
