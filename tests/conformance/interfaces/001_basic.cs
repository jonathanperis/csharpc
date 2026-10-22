IShape[] shapes = { new Circle(1), new Rect(2, 3) };
foreach (var s in shapes) Console.WriteLine($"{s.Name}: {s.Area():F2}");
interface IShape { string Name { get; } double Area(); }
class Circle(double r) : IShape { public string Name => "circle"; public double Area() => Math.PI * r * r; }
class Rect(double w, double h) : IShape { public string Name => "rect"; public double Area() => w * h; }
