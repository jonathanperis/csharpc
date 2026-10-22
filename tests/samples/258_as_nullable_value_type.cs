// `x as T?` from object / ValueType / interfaces: a box of another type (or null) gives null rather
// than throwing InvalidCastException.
using System;

enum Color { Red, Green }
struct Pt { public int X; public Pt(int x) { X = x; } }

class Program
{
    static string Show<T>(T? v) where T : struct => v.HasValue ? v.Value.ToString()! : "null";

    static void UseValueType(ValueType val)
    {
        int? j = val as int?;
        Console.WriteLine(j is null ? "Could not convert " + val.ToString() : j.ToString());
    }

    static void Main()
    {
        UseValueType(5);
        UseValueType(3.5);
        object[] values = { 2.5, "x", 4, 4L, Color.Green, new Pt(7), true, 'c' };
        foreach (var o in values)
            Console.WriteLine($"{Show(o as double?)} {Show(o as int?)} {Show(o as long?)} {Show(o as Color?)} {(o as Pt?)?.X} {Show(o as bool?)} {Show(o as char?)}");
        object? none = null;
        Console.WriteLine(Show(none as int?));
        IComparable c = 42;
        Console.WriteLine(Show(c as int?) + " " + Show(c as double?));
    }
}
