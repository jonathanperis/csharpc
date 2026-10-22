var a = (x: 1, y: "s");
var b = (1, "s");
Console.WriteLine(a == b);
Console.WriteLine(a.Equals(b));
(int first, string second) c = a;
Console.WriteLine($"{c.first} {c.second} {c.Item1}");
Console.WriteLine((1, 2) != (1, 3));
