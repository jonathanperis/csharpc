// Type tests and casts against System.ValueType and System.Enum on references: boxed primitives,
// structs, enums, tuples, decimal/DateTime/TimeSpan, nullable values; strings, arrays and classes are not.
using System;

object?[] items =
{
    "text", 1, 2.5, null, new object(), DayOfWeek.Monday, (1, "two"), new int[0], 'c', 3.5m,
    new DateTime(2020, 1, 2), TimeSpan.FromSeconds(3), (int?)5, (long?)null, new Point(1, 2), new Shape(),
    true, (byte)7, ConsoleColor.Red, new Guid("00000000-0000-0000-0000-000000000001"),
};
foreach (var o in items)
{
    var name = o is null ? "null" : o.GetType().Name;
    Console.WriteLine($"{name,-12} {o is ValueType} {o is Enum} {(o as ValueType) is not null} {(o as Enum) is not null} {Generic(o)} {GenericEnum(o)}");
}

ValueType boxed = 42;
Console.WriteLine($"{boxed} {boxed.Equals(42)} {boxed is int} {boxed.GetHashCode()}");
Enum day = DayOfWeek.Friday;
Console.WriteLine($"{day} {day is DayOfWeek} {day is ValueType} {(int)(DayOfWeek)day}");

foreach (var o in new object[] { "text", new Shape(), 7, DayOfWeek.Sunday })
{
    try { var v = (ValueType)o; Console.WriteLine("ValueType cast ok: " + v); }
    catch (InvalidCastException e) { Console.WriteLine(e.Message); }
    try { var e = (Enum)o; Console.WriteLine("Enum cast ok: " + e); }
    catch (InvalidCastException e) { Console.WriteLine(e.Message); }
}

Console.WriteLine(Describe(5) + " " + Describe("s") + " " + Describe(DayOfWeek.Tuesday) + " " + Describe(new Shape()));

static bool Generic<T>(T value) => value is ValueType;
static bool GenericEnum<T>(T value) => value is Enum;
static string Describe(object o) => o switch
{
    Enum e => "enum:" + e,
    ValueType v => "value:" + v,
    _ => "ref:" + o,
};

struct Point
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public override string ToString() => $"({X}, {Y})";
}

class Shape { public override string ToString() => "shape"; }
