var a = new Point(1, 2);
var b = a with { X = 5 };
Console.WriteLine(a);
Console.WriteLine(b);
Console.WriteLine(a.Equals(new Point(1, 2)));
record struct Point(int X, int Y);
