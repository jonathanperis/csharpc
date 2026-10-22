var text = "one two three two three three";
var top = text.Split(' ').GroupBy(w => w).Select(g => (Word: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Word);
foreach (var (w, c) in top) Console.WriteLine($"{w}: {c}");
