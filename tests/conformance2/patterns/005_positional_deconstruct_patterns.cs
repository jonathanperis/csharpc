static string Q(Point p) => p switch { (0, 0) => "origin", (var x, 0) => $"x-axis {x}", (0, _) => "y-axis", var (x, y) when x == y => "diagonal", _ => "plane" };
Console.WriteLine($"{Q(new(0, 0))} {Q(new(3, 0))} {Q(new(0, 2))} {Q(new(4, 4))} {Q(new(1, 2))}");
record Point(int X, int Y);
