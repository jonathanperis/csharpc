(int, string) t = (1, "one");
Console.WriteLine(t.Item1 + " " + t.Item2);
var named = (x: 3, y: 4);
Console.WriteLine(named.x * named.y);
var (a, b) = named;
Console.WriteLine(a + b);
Console.WriteLine(t);
Console.WriteLine((1, 2) == (1, 2));
