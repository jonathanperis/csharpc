// What Dictionary<string, V> and HashSet<string> report as their comparer, and the alternate (ReadOnlySpan<char>) lookups that need the
// collection's comparer to support them: the default comparer is handed back as it was, StringComparer.Ordinal / OrdinalIgnoreCase and
// a custom comparer stay as given (a custom one has no alternate side).
using System;
using System.Collections.Generic;

class Reverse : IEqualityComparer<string>
{
    public bool Equals(string x, string y) => x == y;
    public int GetHashCode(string obj) => obj.Length;
}

class Program
{
    static void Main()
    {
        var plain = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var explicitDefault = new Dictionary<string, int>(EqualityComparer<string>.Default) { ["a"] = 1 };
        var ordinal = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 };
        var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };
        var custom = new Dictionary<string, int>(new Reverse()) { ["a"] = 1 };
        Console.WriteLine($"{plain.Comparer == EqualityComparer<string>.Default} {explicitDefault.Comparer == EqualityComparer<string>.Default} {ordinal.Comparer == StringComparer.Ordinal} " +
            $"{ignoreCase.Comparer == StringComparer.OrdinalIgnoreCase} {custom.Comparer is Reverse}");

        var set = new HashSet<string> { "x", "y" };
        var setOrdinal = new HashSet<string>(StringComparer.Ordinal) { "x" };
        var setIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X" };
        var setCustom = new HashSet<string>(new Reverse()) { "x" };
        Console.WriteLine($"{set.Comparer == EqualityComparer<string>.Default} {setOrdinal.Comparer == StringComparer.Ordinal} {setIgnoreCase.Comparer == StringComparer.OrdinalIgnoreCase} {setCustom.Comparer is Reverse}");

        Console.WriteLine($"{plain.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup)} {explicitDefault.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)} " +
            $"{ordinal.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)} {ignoreCase.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)} {custom.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)}");
        Console.WriteLine($"{lookup.ContainsKey("a".AsSpan())} {lookup.ContainsKey("A".AsSpan())} {lookup.TryGetValue("b".AsSpan(), out var b)} {b}");
        Console.WriteLine($"{ignoreCase.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey("a".AsSpan())}");
        try { custom.GetAlternateLookup<ReadOnlySpan<char>>(); }
        catch (InvalidOperationException e) { Console.WriteLine("custom: " + e.Message); }

        Console.WriteLine($"{set.TryGetAlternateLookup<ReadOnlySpan<char>>(out var setLookup)} {setIgnoreCase.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)} {setCustom.TryGetAlternateLookup<ReadOnlySpan<char>>(out _)}");
        Console.WriteLine($"{setLookup.Contains("x".AsSpan())} {setLookup.Contains("z".AsSpan())} {setLookup.Add("z".AsSpan())} {setLookup.Add("z".AsSpan())} {set.Count}");
        Console.WriteLine($"{setLookup.TryGetValue("y".AsSpan(), out var actual)} {actual} {setLookup.Remove("x".AsSpan())} {set.Count}");

        // Equal keys under the wrapped default comparer: a span key hashes as the string with those chars.
        var keys = new Dictionary<string, int>();
        for (var i = 0; i < 100; i++) keys["k" + i] = i;
        var keyLookup = keys.GetAlternateLookup<ReadOnlySpan<char>>();
        var found = 0;
        for (var i = 0; i < 120; i++) if (keyLookup.TryGetValue(("k" + i).AsSpan(), out var v) && v == i) found++;
        Console.WriteLine(found);
    }
}
