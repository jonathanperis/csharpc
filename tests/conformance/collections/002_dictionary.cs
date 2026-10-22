var d = new Dictionary<string, int>();
foreach (var w in "the cat and the hat and the bat".Split(' '))
    d[w] = d.TryGetValue(w, out var c) ? c + 1 : 1;
foreach (var key in d.Keys.OrderBy(k => k)) Console.WriteLine($"{key}={d[key]}");
Console.WriteLine(d.ContainsKey("cat") + " " + d.Remove("cat") + " " + d.Count);
try { _ = d["zzz"]; } catch (KeyNotFoundException) { Console.WriteLine("missing"); }
