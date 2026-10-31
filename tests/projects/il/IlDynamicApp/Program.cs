using IlDynamicLib;

Console.WriteLine(Late.Twice(21));
Console.WriteLine(Late.Twice("ab"));
Console.WriteLine(Late.Twice(1.25));
Console.WriteLine(Late.Describe(new Square()));
Console.WriteLine(Late.Describe(new Shape()));
Console.WriteLine(Late.Describe(new AppThing()));
Console.WriteLine(Late.Describe(42));
Console.WriteLine(Late.Sum(new List<int> { 1, 2, 3 }));
Console.WriteLine(Late.Sum(new[] { 4, 5 }));
Console.WriteLine(Late.Tripled(7));
Console.WriteLine(Late.Tripled("x"));
var counter = new Counter(10);
Console.WriteLine(counter.Bump(5));
Console.WriteLine(counter.Bump("tag"));
Console.WriteLine(counter.Value);
Console.WriteLine(Shapes.Area(new Square()));
Console.WriteLine(Shapes.Secret(new Square()));
try { Shapes.Area(new Shape()); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

// Internal to the app: the library's call sites see it as object.
class AppThing { public string Describe() => "app thing"; }
