// Type equality against a typeof, Type.IsInstanceOfType, Type.BaseType and interface tests (#349): a Type compared with a
// typeof is tested against that type's method table, the metadata is cached in the Type object, and failed interface tests
// look builtin registrations up by hash. The results must stay .NET's for every kind of operand.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

interface IShape { }
class Shape : IShape { }
sealed class Circle : Shape { }
struct Point : IComparable<Point> { public int CompareTo(Point other) => 0; }

static class Program
{
    static Type? Pick(int i) => i switch { 0 => typeof(int), 1 => typeof(string), 2 => null, 3 => typeof(long), 4 => typeof(int?), _ => typeof(Circle) };

    static void Main()
    {
        for (int i = 0; i < 6; i++)
        {
            Type? t = Pick(i);
            Console.WriteLine($"{i}: ==int {t == typeof(int)} int== {typeof(int) == t} !=string {t != typeof(string)} string!= {typeof(string) != t} =={(object?)t == typeof(Circle)}");
        }
        IFormatProvider culture = CultureInfo.InvariantCulture;
        Console.WriteLine("GetFormat NumberFormatInfo " + (culture.GetFormat(typeof(NumberFormatInfo)) is NumberFormatInfo) + ", DateTimeFormatInfo " + (culture.GetFormat(typeof(DateTimeFormatInfo)) is DateTimeFormatInfo) + ", string " + (culture.GetFormat(typeof(string)) is null));

        object?[] values = { 1, "s", new Circle(), new Shape(), new Point(), null, new int[1], new List<int>(), 2.5, DayOfWeek.Monday, (int?)3 };
        Type[] types = { typeof(object), typeof(int), typeof(string), typeof(Shape), typeof(Circle), typeof(IShape), typeof(IComparable), typeof(IComparable<Point>), typeof(IEnumerable), typeof(IList<int>), typeof(ValueType), typeof(Enum), typeof(int?), typeof(IFormattable), typeof(IConvertible), typeof(Array) };
        foreach (var v in values)
        {
            var line = (v?.GetType().Name ?? "null") + ":";
            foreach (var t in types) line += t.IsInstanceOfType(v) ? " 1" : " 0";
            line += " | is IShape " + (v is IShape) + ", IComparable " + (v is IComparable) + ", IFormattable " + (v is IFormattable) + ", IEnumerable " + (v is IEnumerable) + ", IDisposable " + (v is IDisposable);
            Console.WriteLine(line);
        }
        foreach (var t in new[] { typeof(Circle), typeof(Shape), typeof(object), typeof(int), typeof(IShape), typeof(int[]), typeof(DayOfWeek), typeof(string) })
            Console.WriteLine(t.Name + " base " + (t.BaseType?.Name ?? "null"));
        try { Type? none = null; Console.WriteLine(none!.IsInstanceOfType(1)); }
        catch (NullReferenceException) { Console.WriteLine("null Type: NullReferenceException"); }
    }
}
