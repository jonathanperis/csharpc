// Calls through interfaces over T from generic classes used with several reference types: the interface each call dispatches on
// is the exact instantiation's (IComparer<string> is not IComparer<Animal>), including contravariant ones and inherited classes.
using System;
using System.Collections.Generic;

interface IShape<T> { T Scale(T value, int factor); string Describe(T value); }

abstract class Animal { public abstract string Sound { get; } }
sealed class Dog : Animal { public override string Sound => "woof"; }
sealed class Cat : Animal { public override string Sound => "meow"; }

sealed class ByLength : IComparer<string>, IEqualityComparer<string>
{
    public int Compare(string? a, string? b) => (a?.Length ?? 0).CompareTo(b?.Length ?? 0);
    public bool Equals(string? a, string? b) => a?.Length == b?.Length;
    public int GetHashCode(string s) => s.Length;
}

sealed class BySound : IComparer<Animal>
{
    public int Compare(Animal? a, Animal? b) => string.CompareOrdinal(a?.Sound, b?.Sound);
}

sealed class ObjectOrder : IComparer<object>
{
    public int Compare(object? a, object? b) => string.CompareOrdinal(a?.ToString(), b?.ToString());
}

sealed class Repeat : IShape<string>
{
    public string Scale(string value, int factor) => string.Concat(System.Linq.Enumerable.Repeat(value, factor));
    public string Describe(string value) => "text " + value;
}

sealed class Pack : IShape<Animal[]>
{
    public Animal[] Scale(Animal[] value, int factor) { var r = new Animal[value.Length * factor]; for (int i = 0; i < r.Length; i++) r[i] = value[i % value.Length]; return r; }
    public string Describe(Animal[] value) => "pack of " + value.Length;
}

class Sorter<T>
{
    private readonly IComparer<T> _comparer;
    public Sorter(IComparer<T> comparer) { _comparer = comparer; }
    public T Max(IEnumerable<T> items)
    {
        T best = default!;
        bool first = true;
        foreach (var item in items)
            if (first || _comparer.Compare(item, best) > 0) { best = item; first = false; }
        return best;
    }
    public virtual string Name => "sorter";
}

sealed class NamedSorter<T> : Sorter<T>
{
    public NamedSorter(IComparer<T> comparer) : base(comparer) { }
    public override string Name => "named";
}

class Shaper<T>
{
    private readonly IShape<T> _shape;
    public Shaper(IShape<T> shape) { _shape = shape; }
    public string Run(T value) => _shape.Describe(_shape.Scale(value, 3));
}

class Bag<T>
{
    private readonly IEqualityComparer<T> _eq;
    private readonly List<T> _items = new();
    public Bag(IEqualityComparer<T> eq) { _eq = eq; }
    public bool Add(T item)
    {
        foreach (var x in _items) if (_eq.Equals(x, item)) return false;
        _items.Add(item);
        return true;
    }
    public int Count => _items.Count;
    public int Hash(T item) => _eq.GetHashCode(item!);
}

static class Program
{
    static void Main()
    {
        var words = new[] { "pear", "fig", "banana", "kiwi" };
        Animal[] animals = { new Dog(), new Cat(), new Dog() };
        Console.WriteLine(new Sorter<string>(new ByLength()).Max(words));
        Console.WriteLine(new NamedSorter<string>(StringComparer.Ordinal).Max(words));
        Console.WriteLine(new Sorter<Animal>(new BySound()).Max(animals).Sound);
        // IComparer<object> is an IComparer<string> and an IComparer<Animal> (contravariance).
        Console.WriteLine(new Sorter<string>(new ObjectOrder()).Max(words));
        Console.WriteLine(new NamedSorter<Animal>(new ObjectOrder()).Max(animals).GetType().Name);
        Console.WriteLine(new Shaper<string>(new Repeat()).Run("ab"));
        Console.WriteLine(new Shaper<Animal[]>(new Pack()).Run(animals));
        var bag = new Bag<string>(new ByLength());
        foreach (var w in words) Console.Write(bag.Add(w) ? "+" : "-");
        Console.WriteLine(" " + bag.Count + " " + bag.Hash("seven"));
        var animalsBag = new Bag<Animal>(EqualityComparer<Animal>.Default);
        foreach (var a in animals) Console.Write(animalsBag.Add(a) ? "+" : "-");
        Console.WriteLine(" " + animalsBag.Count);
        Sorter<string> s = new NamedSorter<string>(new ByLength());
        Console.WriteLine(s.Name + " " + s.Max(new List<string> { "a", "abc", "ab" }));
    }
}
