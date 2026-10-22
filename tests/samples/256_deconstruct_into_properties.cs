// Deconstruction into properties, indexers, static and get-only auto-properties (the (X, Y) = (x, y)
// constructor idiom), with C#'s order: receivers and indexer arguments, then values, then setters.
using System;
using System.Collections.Generic;

public class Customer
{
    public Customer(DateTime dateJoined, string name)
    {
        (DateJoined, Name) = (dateJoined, name);
    }
    public DateTime DateJoined { get; }
    public string Name { get; set; }
    public int Count;
    public static int Total { get; set; }
    private readonly Dictionary<string, int> _d = new();
    public int this[string key] { get => _d.TryGetValue(key, out var v) ? v : -1; set { Console.WriteLine($"set [{key}] = {value}"); _d[key] = value; } }
}

public struct Pt
{
    public Pt(int x, int y) => (X, Y) = (x, y);
    public int X { get; set; }
    public int Y { get; set; }
    public void Deconstruct(out int x, out int y) { x = X; y = Y; }
}

public class Tracer
{
    private int _v;
    public int V { get => _v; set { Console.WriteLine($"V <- {value}"); _v = value; } }
    public Tracer Self(string tag) { Console.WriteLine("receiver " + tag); return this; }
}

class Program
{
    static int Val(int v, string tag) { Console.WriteLine("value " + tag); return v; }
    static string Key(string k) { Console.WriteLine("key " + k); return k; }
    static void Main()
    {
        var c = new Customer(new DateTime(2020, 5, 1), "Ann");
        Console.WriteLine($"{c.Name} {c.DateJoined:yyyy-MM-dd}");
        (c.Name, c.Count, Customer.Total) = ("Bob", 3, 7);
        Console.WriteLine($"{c.Name} {c.Count} {Customer.Total}");
        (c[Key("a")], c[Key("b")]) = (Val(1, "1"), Val(2, "2"));
        Console.WriteLine($"{c["a"]} {c["b"]}");
        var p = new Pt(3, 4);
        Console.WriteLine($"{p.X} {p.Y}");
        (p.X, p.Y) = (p.Y, p.X);
        Console.WriteLine($"{p.X} {p.Y}");
        var t1 = new Tracer(); var t2 = new Tracer();
        (t1.Self("1").V, t2.Self("2").V) = (Val(10, "a"), Val(20, "b"));
        Console.WriteLine($"{t1.V} {t2.V}");
        var tuple = (5, "five");
        (c.Count, c.Name) = tuple;
        Console.WriteLine($"{c.Count} {c.Name}");
        (t1.V, _) = new Pt(8, 9);
        Console.WriteLine(t1.V);
        (c.Name, c.Count) = (c.Count.ToString(), c.Name.Length);
        Console.WriteLine($"{c.Name} {c.Count}");
        Tracer? nt = null;
        try { (nt!.V, t2.V) = (Val(1, "x"), Val(2, "y")); } catch (NullReferenceException) { Console.WriteLine("NRE " + t2.V); }
    }
}
