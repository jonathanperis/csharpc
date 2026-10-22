List<int> l = new();
l.Add(3);
Point p = new(1, 2);
Console.WriteLine(l[0] + p.X + p.Y);
record Point(int X, int Y);
