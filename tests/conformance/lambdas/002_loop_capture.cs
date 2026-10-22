var fs = new List<Func<int>>();
foreach (var i in new[] { 1, 2, 3 }) fs.Add(() => i * 10);
foreach (var f in fs) Console.WriteLine(f());
var gs = new List<Func<int>>();
for (int j = 0; j < 3; j++) gs.Add(() => j);
foreach (var g in gs) Console.WriteLine(g());
