// IEqualityComparer<T>.Equals/GetHashCode through an interface-typed variable that holds the default comparer, another comparer, or null (the way
// the framework's Dictionary and HashSet call their comparer: a type guard, then the default comparer's code inline, else the interface call).
// Results, call counts, the evaluation order of receiver and arguments, and the NullReferenceException must be .NET's.
using System;
using System.Collections.Generic;

class Counting<T> : IEqualityComparer<T>
{
    public int Equalses, Hashes;
    public bool Equals(T x, T y) { Equalses++; return EqualityComparer<T>.Default.Equals(x, y); }
    public int GetHashCode(T obj) { Hashes++; return EqualityComparer<T>.Default.GetHashCode(obj); }
}

class Key
{
    public int Id;
    public Key(int id) => Id = id;
    public override bool Equals(object o) => o is Key k && k.Id / 10 == Id / 10;
    public override int GetHashCode() => Id / 10;
}

class Program
{
    static string NullHash(IEqualityComparer<string> comparer)
    {
        try { return (comparer.GetHashCode(null) == 0).ToString(); }
        catch (ArgumentNullException) { return "ArgumentNullException"; }
    }

    static void Main()
    {
        var log = new List<string>();
        T Note<T>(string what, T value) { log.Add(what); return value; }

        IEqualityComparer<string> def = EqualityComparer<string>.Default;
        IEqualityComparer<string> ignoreCase = StringComparer.OrdinalIgnoreCase;
        var counting = new Counting<string>();
        IEqualityComparer<string> custom = counting;
        foreach (var (name, comparer) in new[] { ("default", def), ("ignore-case", ignoreCase), ("counting", custom) })
        {
            Console.WriteLine($"{name}: {comparer.Equals("abc", "abc")} {comparer.Equals("abc", "ABC")} {comparer.Equals("abc", null)} {comparer.Equals(null, null)} " +
                $"same-hash={comparer.GetHashCode("hello") == comparer.GetHashCode(new string(new[] { 'h', 'e', 'l', 'l', 'o' }))} null-hash={NullHash(comparer)}");
        }
        Console.WriteLine($"counting: equals={counting.Equalses} hashes={counting.Hashes}");

        IEqualityComparer<int> ints = EqualityComparer<int>.Default;
        IEqualityComparer<double> doubles = EqualityComparer<double>.Default;
        IEqualityComparer<Key> keys = EqualityComparer<Key>.Default;
        IEqualityComparer<object> objects = EqualityComparer<object>.Default;
        IEqualityComparer<int?> nullable = EqualityComparer<int?>.Default;
        Console.WriteLine($"{ints.Equals(3, 3)} {ints.Equals(3, 4)} {ints.GetHashCode(7)} {doubles.Equals(double.NaN, double.NaN)} {doubles.Equals(0.0, -0.0)} {doubles.GetHashCode(0.0) == doubles.GetHashCode(-0.0)}");
        Console.WriteLine($"{keys.Equals(new Key(11), new Key(19))} {keys.Equals(new Key(11), new Key(21))} {keys.GetHashCode(new Key(25))} {keys.Equals(null, null)} {keys.Equals(new Key(1), null)}");
        Console.WriteLine($"{objects.Equals(1, 1)} {objects.Equals(1, 1L)} {objects.Equals("a", "a")} {objects.GetHashCode(5)} {nullable.Equals(null, null)} {nullable.Equals(1, null)} {nullable.GetHashCode(null)}");

        // Receiver first, then the arguments, each once; a null comparer throws after the arguments were evaluated.
        IEqualityComparer<string> source = def;
        Console.WriteLine(Note("receiver", source).Equals(Note("left", "x"), Note("right", "x")) + " " + string.Join(",", log));
        log.Clear();
        IEqualityComparer<string> nothing = null;
        try { Console.WriteLine(Note("receiver", nothing).GetHashCode(Note("argument", "x"))); }
        catch (NullReferenceException) { Console.WriteLine("NullReferenceException after " + string.Join(",", log)); }

        // The comparers a collection holds
        var set = new HashSet<string>(new[] { "a", "b", "A" });
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1 };
        dict["A"] = 2;
        Console.WriteLine($"{set.Count} {dict.Count} {dict["a"]} {set.Comparer.Equals("a", "a")} {dict.Comparer.Equals("a", "A")} {new Dictionary<string, int>().Comparer.Equals("a", "A")}");
    }
}
