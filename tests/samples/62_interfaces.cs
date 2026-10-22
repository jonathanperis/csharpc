// Interfaces: dispatch through per-class interface maps, multiple interfaces, interface
// inheritance, explicit implementations, default interface methods and type tests.
IShape[] shapes = [new Circle(1), new Square(2), new Circle(0.5)];
double total = 0;
foreach (IShape s in shapes)
{
    Console.WriteLine($"{s.Name}: {s.Area()}");
    total += s.Area();
}
Console.WriteLine($"total {total}");

IShape first = shapes[0];
Console.WriteLine(first.Describe());              // default interface method
Console.WriteLine(shapes[1].Describe());           // overridden default method

object o = shapes[1];
Console.WriteLine(o is IShape);
Console.WriteLine(o is IResizable);
Console.WriteLine(o is Circle);
if (o is IResizable r)
{
    r.Scale(3);
    Console.WriteLine(((IShape)o).Area());
}
IResizable? none = shapes[0] as IResizable;
Console.WriteLine(none == null);

var logger = new ConsoleLogger();
ILogger log = logger;
log.Log("hello");
log.Log("world");
Console.WriteLine(logger.Count);
ICountingLogger counting = logger;
Console.WriteLine(counting.Count);
counting.Log("third");
Console.WriteLine(((ILogger)counting).Prefix);

INamed named = new Person("Ada");
Console.WriteLine(named.Name);                     // explicit implementation
Console.WriteLine(new Person("Grace").Name);       // class's own member
var stringy = new Person("Linus");
Console.WriteLine(Shout(stringy));

static string Shout(INamed n) => n.Name + "!";

interface IShape
{
    string Name { get; }
    double Area();
    string Describe() => $"a {Name} with area {Area()}";
}

interface IResizable { void Scale(double factor); }

class Circle(double radius) : IShape
{
    public string Name => "circle";
    public double Area() => 3 * radius * radius;
}

class Square(double side) : IShape, IResizable
{
    private double _side = side;
    public string Name => "square";
    public double Area() => _side * _side;
    public void Scale(double factor) => _side *= factor;
    public string Describe() => "a square!";
}

interface ILogger
{
    void Log(string message);
    string Prefix => "[log]";
}

interface ICountingLogger : ILogger { int Count { get; } }

class ConsoleLogger : ICountingLogger
{
    public int Count { get; private set; }
    public void Log(string message)
    {
        Count++;
        Console.WriteLine($"{Count}: {message}");
    }
}

interface INamed { string Name { get; } }

class Person(string name) : INamed
{
    string INamed.Name => "Named " + name;
    public string Name => name.ToUpper2();
}

static class StringHelpers
{
    public static string ToUpper2(this string s)
    {
        string result = "";
        foreach (var c in s) result += c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
        return result;
    }
}
