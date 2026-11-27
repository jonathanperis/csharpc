// [UnsafeAccessor(UnsafeAccessorKind.Constructor)] returning a struct: the private constructor runs on a fresh value (fields it
// doesn't set stay zero), overloads are told apart by their parameter types, and the result is an independent copy (#423: the
// corelib builds CoreLib's StringRuneEnumerator this way). A class constructor through the same kind, for comparison.
using System;
using System.Runtime.CompilerServices;

struct Point
{
    public int X, Y;
    public string Tag;
    private Point(int x) { X = x; Y = -x; Tag = "one"; }
    private Point(int x, int y) { X = x; Y = y; Tag = null; }
    private Point(string tag) { Tag = tag; }
    public override string ToString() => $"({X}, {Y}, {Tag ?? "null"})";
}

class Box
{
    public readonly int Value;
    private Box(int value) => Value = value * 2;
}

static class Program
{
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)] static extern Point NewPoint(int x);
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)] static extern Point NewPoint(int x, int y);
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)] static extern Point NewPoint(string tag);
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)] static extern Box NewBox(int value);

    static void Main()
    {
        var a = NewPoint(5);
        var b = NewPoint(3, 4);
        var c = NewPoint("tagged");
        b.X = 30;
        Console.WriteLine($"{a} {b} {c} {NewPoint(3, 4)}");
        Console.WriteLine(NewBox(21).Value);
        foreach (var r in "a😀".EnumerateRunes()) Console.Write(r.Value.ToString("X") + " ");
        Console.WriteLine();
    }
}
