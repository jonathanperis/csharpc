// Boxed System.Index / System.Range (ToString, Equals, GetHashCode, as Dictionary and HashSet keys) and boxed structs that hold an Int128
// (a Dictionary<Int128, V> enumerator boxed through IEnumerable, KeyValuePair<Int128, V> in an object): the payload of a box sits 8 bytes
// into the object, so the 16-byte field must be read and written without assuming 16-byte alignment.
using System;
using System.Collections;
using System.Collections.Generic;

struct Holder
{
    public int Tag;
    public Int128 Big;
    public UInt128 Bigger;
}

class Program
{
    static void Main()
    {
        object index = ^3, fromStart = (Index)3, range = 1..^2, all = Range.All;
        Console.WriteLine($"{index} {fromStart} {range} {all}");
        Console.WriteLine($"{index.Equals(^3)} {index.Equals(fromStart)} {index.Equals(null)} {index.Equals(3)} {range.Equals(1..^2)} {range.Equals(1..2)} {range.Equals(index)}");
        Console.WriteLine($"{index.GetHashCode() == ((Index)(^3)).GetHashCode()} {range.GetHashCode() == (1..^2).GetHashCode()} {index.GetType()} {range.GetType()}");
        Console.WriteLine($"{index is Index} {index is Range} {range is Range} {range is Index} {(index is Index i ? i.Value : -1)}");

        var byIndex = new Dictionary<Index, string> { [^1] = "last", [0] = "first" };
        byIndex[Index.FromEnd(1)] = "again";
        Console.WriteLine($"{byIndex.Count} {byIndex[^1]} {byIndex[Index.Start]} {byIndex.ContainsKey(^2)}");
        var byRange = new Dictionary<Range, int> { [1..3] = 1, [..] = 2, [1..3] = 3 };
        Console.WriteLine($"{byRange.Count} {byRange[1..3]} {byRange[Range.All]} {byRange.ContainsKey(0..3)}");
        var set = new HashSet<Index> { ^1, 1, ^1, 2 };
        Console.WriteLine($"{set.Count} {set.Contains(^1)} {set.Contains(^2)} {set.Contains(2)}");
        var ranges = new HashSet<Range> { 0..1, 0..1, 1..2 };
        Console.WriteLine($"{ranges.Count} {ranges.Contains(1..2)} {ranges.Contains(2..3)}");

        var big = new Dictionary<Int128, int> { [Int128.MinValue] = 1, [Int128.MaxValue] = 2, [-5] = 3 };
        IEnumerable<KeyValuePair<Int128, int>> pairs = big;
        var keys = new List<Int128>();
        foreach (var pair in pairs) keys.Add(pair.Key);
        keys.Sort();
        Console.WriteLine(string.Join(",", keys));
        IEnumerator<KeyValuePair<Int128, int>> boxed = big.GetEnumerator();
        boxed.MoveNext();
        var current = boxed.Current;
        Console.WriteLine($"{current.Key} {current.Value} {((IEnumerator)boxed).Current}");

        object holder = new Holder { Tag = 7, Big = Int128.MinValue + 3, Bigger = UInt128.MaxValue - 4 };
        var unboxed = (Holder)holder;
        Console.WriteLine($"{unboxed.Tag} {unboxed.Big} {unboxed.Bigger}");
        var holders = new Dictionary<Int128, Holder> { [1] = unboxed };
        Console.WriteLine($"{holders[1].Big} {holders[1].Bigger} {new KeyValuePair<Int128, Holder>(2, unboxed)}");
        var set128 = new HashSet<UInt128> { UInt128.MaxValue, 0, UInt128.MaxValue };
        Console.WriteLine($"{set128.Count} {set128.Contains(UInt128.MaxValue)}");
    }
}
