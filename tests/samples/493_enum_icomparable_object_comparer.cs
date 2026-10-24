// Boxed enums are IComparable (System.Enum.CompareTo), and Comparer<object>.Default / Comparer<I>.Default
// compare like .NET's non-generic Comparer.Default (List<object>.Sort, OrderBy over objects).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

enum Color { Red, Green, Blue }
enum Flags : byte { None = 0, High = 200 }
enum Signed : long { Low = -5, High = 5 }

interface IShape { double Area { get; } }
sealed class Square : IShape, IComparable
{
    public double Side;
    public Square(double s) { Side = s; }
    public double Area => Side * Side;
    public int CompareTo(object? other) => other is Square s ? Area.CompareTo(s.Area) : 1;
    public override string ToString() => $"Square({Side})";
}
sealed class Blob : IShape { public double Area => 1; }

class Program
{
    static void Main()
    {
        object o = Color.Green;
        Console.WriteLine($"{o is IComparable} {o is IFormattable} {o is Enum} {o is ValueType}");
        IComparable c = Color.Blue;
        Console.WriteLine($"{c.CompareTo(Color.Red)} {c.CompareTo(Color.Blue)} {c.CompareTo(null)}");
        Console.WriteLine($"{((IComparable)Flags.High).CompareTo(Flags.None)} {((IComparable)Signed.Low).CompareTo(Signed.High)}");
        try { c.CompareTo(1); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { c.CompareTo(Flags.None); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }

        var list = new List<object> { Color.Blue, Color.Red, Color.Green };
        list.Sort();
        Console.WriteLine(string.Join(",", list));
        var numbers = new List<object> { 3, 1, 2, null! };
        numbers.Sort();
        Console.WriteLine(string.Join(",", numbers.Select(n => n ?? "null")));
        var words = new List<object> { "pear", "Apple", "apple", "banana" };
        words.Sort();
        Console.WriteLine(string.Join(",", words));
        Console.WriteLine(Comparer<object>.Default.Compare(Color.Red, Color.Blue));
        Console.WriteLine(Comparer<object>.Default.Compare(2.5m, 2.25m));
        Console.WriteLine(((Enum)Color.Blue).CompareTo(Color.Green));
        var sorted = new object[] { 10L, 3L, 7L }.OrderBy(x => x).ToArray();
        Console.WriteLine(string.Join(",", sorted));
        try { Comparer<object>.Default.Compare(1, "x"); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { Comparer<object>.Default.Compare(new object(), new object()); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }

        var shapes = new List<IShape> { new Square(3), new Square(1), new Square(2) };
        shapes.Sort();
        Console.WriteLine(string.Join(",", shapes));
        Console.WriteLine(Comparer<IShape>.Default.Compare(new Blob(), new Square(1)) == 0 ? "blob" : "other");
    }
}
