var items = new[] { new M { V = 1 }, new M { V = 2 } };
foreach (var it in items) { var c = it; c.V *= 10; }
Console.WriteLine(items[0].V + items[1].V);
for (int i = 0; i < items.Length; i++) items[i].V *= 10;
Console.WriteLine(items[0].V + items[1].V);
struct M { public int V; }
