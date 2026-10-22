var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["One"] = 1 };
Console.WriteLine(d["ONE"] + " " + d.ContainsKey("one") + " " + d.TryAdd("one", 5) + " " + d.Count);
try { d.Add("ONE", 2); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine(d.TryGetValue("two", out var v) + " " + v + " " + d.GetValueOrDefault("two", -1));
Console.WriteLine(d.Remove("one", out var removed) + " " + removed);
