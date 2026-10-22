// User-defined structs: value semantics, constructors, methods, properties, operators, boxing.
var a = new Vec2(3, 4);
var b = a;                 // copy
b.X = 10;                  // mutates the copy only
Console.WriteLine($"a = {a}, b = {b}");
Console.WriteLine(a.Length);
Console.WriteLine(a + b);
Console.WriteLine(a * 2);
Console.WriteLine(a == new Vec2(3, 4));
Console.WriteLine(a != b);

Vec2 zero = default;
Console.WriteLine(zero);
var empty = new Vec2();
Console.WriteLine(empty.X + empty.Y);

a.Scale(3);                // mutating method on a local
Console.WriteLine(a);
Double(ref a);
Console.WriteLine(a);
Console.WriteLine(Dot(in a, b));

var p = new Particle("dust", new Vec2(1, 1), new Vec2(0.5, -0.25));
for (int i = 0; i < 4; i++) p.Step();
Console.WriteLine(p);

object boxed = a;          // boxing copies
a.X = -1;
Console.WriteLine(boxed);
Console.WriteLine(((Vec2)boxed).X);
Console.WriteLine(boxed is Vec2);
if (boxed is Vec2 v) Console.WriteLine(v.Y);
Console.WriteLine(new Plain { Value = 7 }.Value);
Console.WriteLine(new Plain());          // default ValueType.ToString(): type name
object o1 = new Plain { Value = 1 }, o2 = new Plain { Value = 1 };
Console.WriteLine(o1.Equals(o2));

var c = new Counter();
c.Increment();
c.Increment();
Console.WriteLine(c.Count);
Console.WriteLine(Counter.Created);

static void Double(ref Vec2 v) => v = v * 2;
static double Dot(in Vec2 x, Vec2 y) => x.X * y.X + x.Y * y.Y;

struct Vec2
{
    public double X;
    public double Y;
    public Vec2(double x, double y) { X = x; Y = y; }
    public readonly double Length => Math.Sqrt(X * X + Y * Y);
    public void Scale(double factor) { X *= factor; Y *= factor; }
    public static Vec2 operator +(Vec2 l, Vec2 r) => new Vec2(l.X + r.X, l.Y + r.Y);
    public static Vec2 operator *(Vec2 v, double k) => new Vec2(v.X * k, v.Y * k);
    public static bool operator ==(Vec2 l, Vec2 r) => l.X == r.X && l.Y == r.Y;
    public static bool operator !=(Vec2 l, Vec2 r) => !(l == r);
    public override readonly string ToString() => $"({X}, {Y})";
    public override readonly bool Equals(object? obj) => obj is Vec2 other && this == other;
    public override readonly int GetHashCode() => (int)X ^ (int)Y;
}

class Particle(string name, Vec2 position, Vec2 velocity)
{
    private Vec2 _position = position;
    public void Step() => _position = _position + velocity;
    public override string ToString() => $"{name} at {_position}";
}

struct Plain
{
    public int Value { get; set; }
}

struct Counter
{
    public static int Created;
    public int Count { get; private set; }
    public Counter() { Count = 0; Created++; }
    public void Increment() => Count++;
}
