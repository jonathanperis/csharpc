var t = (1, (2, 3), "x");
var (a, (b, c), d) = t;
Console.WriteLine($"{a}{b}{c}{d}");
Console.WriteLine(t);
Console.WriteLine(t.Item2.Item2);
