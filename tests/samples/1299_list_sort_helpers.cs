// List<T>.Sort, Array.Sort and the default array sort helper (ArraySortHelper<T> / GenericArraySortHelper<T> in the framework's IL: the
// IComparable<T> struct, the class, the plain struct with a Comparison, double with NaN, strings, a larger list).
using System;
using System.Collections.Generic;

struct Cmp : IComparable<Cmp>
{
    public int V;
    public Cmp(int v) => V = v;
    public int CompareTo(Cmp o) => V.CompareTo(o.V);
    public override string ToString() => "C" + V;
}
struct Plain { public int V; public override string ToString() => "P" + V; }
class Ref : IComparable<Ref>
{
    public string S;
    public Ref(string s) => S = s;
    public int CompareTo(Ref? o) => string.CompareOrdinal(S, o?.S);
    public override string ToString() => S;
}

class P
{
    static void Main()
    {
        var a = new List<int> { 5, 3, 9, 1, 7, 2, 8 }; a.Sort(); Console.WriteLine(string.Join(",", a));
        var s = new List<string> { "pear", "apple", "fig", "banana" }; s.Sort(StringComparer.Ordinal); Console.WriteLine(string.Join(",", s));
        var c = new List<Cmp> { new(4), new(2), new(8), new(1) }; c.Sort(); Console.WriteLine(string.Join(",", c));
        var r = new List<Ref> { new("b"), new("c"), new("a") }; r.Sort(); Console.WriteLine(string.Join(",", r));
        var d = new List<double> { 2.5, double.NaN, -1, 0 }; d.Sort(); Console.WriteLine(string.Join(",", d));
        var p = new List<Plain> { new() { V = 2 }, new() { V = 1 } };
        p.Sort((x, y) => x.V.CompareTo(y.V)); Console.WriteLine(string.Join(",", p));
        var arr = new Cmp[] { new(3), new(1), new(2) }; Array.Sort(arr); Console.WriteLine(string.Join(",", arr));
        var big = new List<int>(); for (int i = 0; i < 100; i++) big.Add((i * 37) % 101); big.Sort(); Console.WriteLine(big[0] + " " + big[50] + " " + big[99]);
    }
}
