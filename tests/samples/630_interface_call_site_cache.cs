// One interface call site seeing receivers of many types in turn (each with a different interface
// map layout), variant and runtime-implemented interfaces, and an invalid cast at a cached site.
using System;
using System.Collections.Generic;

interface IShape { string Name(); double Area(); }
interface IExtra { int Rank(); }
interface IMore { int Level(); }

class Square : IShape { public string Name() => "square"; public double Area() => 4; }
class Circle : IExtra, IMore, IShape { public string Name() => "circle"; public double Area() => 3.25; public int Rank() => 1; public int Level() => 7; }
class Tri : IMore, IShape, IExtra { public string Name() => "tri"; public double Area() => 1.5; public int Rank() => 2; public int Level() => 9; }
class Big : Tri, IComparable<Big> { public new string Name() => "big"; public int CompareTo(Big? other) => 0; }

static class Program
{
    static string Describe(IShape s) => $"{s.Name()}:{s.Area()}";
    static int Rank(object o) => ((IExtra)o).Rank();
    static int Count<T>(IEnumerable<T> items) { int n = 0; foreach (var _ in items) n++; return n; }

    static void Main()
    {
        IShape[] shapes = [new Square(), new Circle(), new Tri(), new Big(), new Circle(), new Square(), new Big()];
        for (int round = 0; round < 3; round++)
        {
            var parts = new List<string>();
            foreach (var s in shapes) parts.Add(Describe(s));
            Console.WriteLine(string.Join(" ", parts));
        }
        Console.WriteLine($"{Rank(new Circle())} {Rank(new Tri())} {Rank(new Big())} {Rank(new Circle())}");
        IEnumerable<object> variant = new List<string> { "a", "b" };
        Console.WriteLine($"{Count(variant)} {Count(new[] { 1, 2, 3 })} {Count(new List<int> { 4 })} {Count("xyz")} {Count(variant)}");
        try { Console.WriteLine(Rank(new Square())); }
        catch (InvalidCastException e) { Console.WriteLine(e.GetType().Name); }
        Console.WriteLine(Rank(new Tri()));
    }
}
