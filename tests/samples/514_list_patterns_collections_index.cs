// List patterns on List<T> and user collections (Count + int indexer); new Index(value, fromEnd).
using System;
using System.Collections.Generic;

sealed class Bag
{
    private readonly int[] _items;
    public Bag(params int[] items) { _items = items; }
    public int Count { get { Console.Write("[Count]"); return _items.Length; } }
    public int this[int i] { get { Console.Write($"[{i}]"); return _items[i]; } }
}

class Program
{
    static string Describe(List<string> words) => words switch
    {
        [] => "empty",
        [var only] => $"one: {only}",
        ["a", ..] => "starts with a",
        [.., "z"] => "ends with z",
        [var first, _, var last] => $"three: {first}..{last}",
        _ => $"many ({words.Count})",
    };

    static void Main()
    {
        Console.WriteLine(Describe(new List<string>()));
        Console.WriteLine(Describe(new List<string> { "x" }));
        Console.WriteLine(Describe(new List<string> { "a", "b", "c", "d" }));
        Console.WriteLine(Describe(new List<string> { "b", "y", "z" }));
        Console.WriteLine(Describe(new List<string> { "p", "q", "r" }));
        Console.WriteLine(Describe(new List<string> { "p", "q", "r", "s" }));
        List<int>? none = null;
        Console.WriteLine(none is [] ? "matched" : "null does not match");
        var numbers = new List<int> { 1, 2, 3, 4, 5 };
        if (numbers is [1, .., > 4] list) Console.WriteLine($"1..>4 ({list.Count})");
        Console.WriteLine(new Bag(1, 2, 3) is [_, 2, _] ? " yes" : " no");
        Console.WriteLine(new Bag(7) is [> 5, ..] ? " big first" : " small");

        var ix = new Index(2, fromEnd: true);
        Console.WriteLine($"{ix} {new Index(3)} {new Index(0, true).IsFromEnd} {"abcdef"[new Index(1, true)]}");
        try { _ = new Index(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
        Range implicitRange = 3..^5;
        Range explicitRange = new(new Index(3, false), new Index(5, true));
        Console.WriteLine($"{implicitRange.Equals(explicitRange)} {implicitRange.Equals(1..2)} {ix.Equals(^2)} {ix.Equals(new Index(2))}");
    }
}
