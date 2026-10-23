using IlShapes;

// Source code using a library that csharpc compiles from IL: construction, virtual and interface calls,
// generics instantiated with source types, exceptions crossing the boundary in both directions.
var registry = new Registry<Shape>();
registry.Add(new Circle(1.5));
registry.Add(new Rect(2, 3.5));
registry.Add(new Square(4));
registry.Add(new Circle(0.25));
registry.Add(new Triangle(3, 4));

Console.WriteLine($"created {Shape.Created} shapes, registry holds {registry.Count}");
for (var i = 0; i < registry.Count; i++) Console.WriteLine(registry[i]);
Console.WriteLine($"total area {registry.TotalArea():F3}");
Console.WriteLine($"largest: {registry.Largest().Name}");
Console.WriteLine("by area >= 2: " + string.Join(", ", registry.NamesByArea(2)));
Console.WriteLine("summary: " + registry.Summary());
Console.WriteLine("big ones: " + registry.CountWhere(s => s.Area() > 10));
registry.Paint(Color.Blue, 5);
Console.WriteLine($"painted {registry.LastPainted}: {registry[2]}");

IShape shape = registry[0];
shape.Color = Color.Red;
Console.WriteLine($"{shape.Name} is {shape.Color}, area {shape.Area():F1}");

var shapes = new List<Shape> { new Square(1), new Circle(1), new Rect(1, 2) };
shapes.Sort();
Console.WriteLine("sorted: " + string.Join(" < ", shapes.Select(s => s.Name)));
Console.WriteLine("max: " + Geometry.Max(shapes[0], shapes[2]).Name + ", " + Geometry.Max(3, 9) + ", " + Geometry.Max("pear", "apple"));

var p = new Point(3, -4) + new Point(1, 1);
Console.WriteLine($"{p} length {p.ManhattanLength}, equal: {p.Equals(new Point(4, -3))}, hash {p.GetHashCode()}");

Console.WriteLine("primes: " + Geometry.SumPrimes(4) + " " + Geometry.SumPrimes(10));
Console.WriteLine("checked: " + Geometry.Checked(1000, 1000) + " " + Geometry.Checked(100000, 100000));

var log = new List<string>();
var result = Geometry.TryAll(["circle 2", "rect 1 x", "hexagon 3", "square", "rect -1 2", "square 2.5"], log);
foreach (var line in log) Console.WriteLine("  " + line);
Console.WriteLine("parsed " + result);

foreach (var value in new object?[] { null, -5, 42, "", "hello", new Point(1, 2), new Circle(1), 2.5 })
    Console.WriteLine(Geometry.Classify(value));

Console.WriteLine(string.Join(",", new[] { "mon", "sun", "xyz", "wed" }.Select(Geometry.Weekday)));
Console.WriteLine("fib: " + string.Join(" ", Geometry.Fibonacci(12)));

try
{
    new Registry<Circle>().Largest();
}
catch (InvalidOperationException e)
{
    Console.WriteLine("caught: " + e.Message);
}

try
{
    Geometry.Parse("rect 0 1");
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine($"caught {e.GetType().Name}: {e.ParamName}");
}

// A source class deriving from a class compiled from IL.
sealed class Triangle(double b, double h) : Shape("triangle")
{
    public override double Area() => b * h / 2;
    public override string Describe() => "a triangle: " + base.Describe();
}
