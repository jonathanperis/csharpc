// A grab bag of modern C# (10-14): extension members, field keyword, required/init, using declarations,
// covariant returns, conversion operators, indexers, static local functions, target-typed new.
Console.WriteLine("csharpc".Shout());
Console.WriteLine(41.Next);
Console.WriteLine(string.Banner.Length);

var t = new Temperature { Celsius = -300 };
Console.WriteLine(t.Celsius);
var cfg = new Config { Name = "app", Retries = 3 };
Console.WriteLine($"{cfg.Name} {cfg.Retries} {cfg.Mode}");

using (var r = new Resource("outer"))
{
    using var inner = new Resource("inner");
    Console.WriteLine("working");
}
Console.WriteLine("after using");

Animal a = new Dog();
Console.WriteLine(a.Create().Speak());
Dog d = new Dog().Create();
Console.WriteLine(d.Speak());

Meters m = 5.5;
double raw = m;
Console.WriteLine($"{m.Value} {raw} {(int)(Meters)2.9}");

var grid = new Grid(3);
grid[1, 2] = 7;
Console.WriteLine(grid[1, 2] + grid[0, 0]);
Console.WriteLine(grid["center"]);

Point p = new(3, 4);
Console.WriteLine(Square(p.X) + Square(p.Y));
static int Square(int v) => v * v;

public static class StringExt
{
    extension(string s)
    {
        public string Shout() => s.ToUpper() + "!";
        public static string Banner => "=====";
    }
    extension(int n)
    {
        public int Next => n + 1;
    }
}

class Temperature
{
    public double Celsius
    {
        get;
        set => field = value < -273.15 ? -273.15 : value;
    }
}

class Config
{
    public required string Name { get; init; }
    public int Retries { get; init; } = 1;
    public string Mode { get; init; } = "default";
}

class Resource(string name) : IDisposable
{
    public void Dispose() => Console.WriteLine($"dispose {name}");
}

class Animal { public virtual Animal Create() => new Animal(); public virtual string Speak() => "..."; }
class Dog : Animal { public override Dog Create() => new Dog(); public override string Speak() => "woof"; }

readonly struct Meters(double value)
{
    public double Value => value;
    public static implicit operator Meters(double v) => new(v);
    public static implicit operator double(Meters m) => m.Value;
    public static explicit operator int(Meters m) => (int)m.Value;
}

class Grid(int size)
{
    private readonly int[] _cells = new int[size * size];
    public int this[int row, int col] { get => _cells[row * size + col]; set => _cells[row * size + col] = value; }
    public string this[string where] => where == "center" ? $"{size / 2},{size / 2}" : "?";
}

record struct Point(int X, int Y);
