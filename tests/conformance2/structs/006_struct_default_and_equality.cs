var a = new Pt(); var b = default(Pt);
Console.WriteLine(a.Equals(b));
Console.WriteLine(new Pt(1, "x").Equals(new Pt(1, "x")));
Console.WriteLine(new Pt(1, "x").Equals(new Pt(1, "y")));
Console.WriteLine(a.Name == null);
struct Pt(int x, string n) { public int X = x; public string Name = n; }
