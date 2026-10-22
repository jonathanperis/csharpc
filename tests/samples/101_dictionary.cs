// Dictionary<TKey,TValue> port: enumeration order (incl. reuse of removed slots), TryGetValue, Keys/Values.
var d = new Dictionary<string, int>();
d["x"] = 1; d["y"] = 2; d["z"] = 3; d.Remove("x"); d["w"] = 4; d.Remove("z"); d["v"] = 5;
Console.WriteLine(string.Join(",", d.Keys));
foreach (var kv in d) Console.WriteLine($"{kv.Key}={kv.Value} {kv}");
Console.WriteLine(d.Count + " " + d.ContainsKey("y") + " " + d.TryGetValue("w", out var w) + " " + w);
var counts = new Dictionary<char, int>();
foreach (char c in "hello world") counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
foreach (var (k, v) in counts) Console.Write($"{k}:{v} ");
Console.WriteLine();
var squares = new Dictionary<int, long>();
for (int i = 0; i < 1000; i++) squares.Add(i, (long)i * i);
long sum = 0; foreach (var v in squares.Values) sum += v;
Console.WriteLine(sum + " " + squares[999] + " " + squares.GetValueOrDefault(5000, -1));
Console.WriteLine(d);
