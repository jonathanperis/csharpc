var lk = new[] { "a1", "b1", "a2" }.ToLookup(s => s[0]);
Console.WriteLine(string.Join(",", lk['a']) + " | " + lk['z'].Count());
var d = new[] { "x", "yy", "zzz" }.ToDictionary(s => s.Length);
Console.WriteLine(d[2]);
try { new[] { "a", "b" }.ToDictionary(s => 1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
