foreach (var p in new[] { new Pt(0, 0), new Pt(3, 0), new Pt(0, 4), new Pt(1, 1) })
    Console.WriteLine(p switch { (0, 0) => "origin", (_, 0) => "x-axis", (0, _) => "y-axis", var (x, y) => $"({x},{y})" });
record Pt(int X, int Y);
