P? p = new P(3);
Console.WriteLine(p?.V);
Console.WriteLine(p.Value.Double());
p = null;
Console.WriteLine(p?.Double() ?? -1);
Console.WriteLine(p.HasValue);
readonly struct P(int v) { public int V => v; public int Double() => v * 2; }
