var fs = new List<Func<int>>();
for (int i = 0; i < 3; i++) fs.Add(() => i);
foreach (var x in new[] { 10, 20, 30 }) fs.Add(() => x);
int j = 0;
while (j < 3) { int k = j * 100; fs.Add(() => k); j++; }
Console.WriteLine(string.Join(",", fs.Select(f => f())));
