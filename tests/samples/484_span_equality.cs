// MemoryExtensions searches and comparisons use element equality (EqualityComparer<T>.Default): strings
// by value, NaN == NaN and -0.0 == 0.0 for floating point, user Equals for structs and records; the
// IEqualityComparer<T> overloads; integers, chars and enums keep the byte-wise paths.
#nullable disable
using System;
using System.Collections.Generic;

// Strings compare by value, not by reference.
string a1 = "ab", b1 = new string(new[] { 'a', 'b' });
ReadOnlySpan<string> s1 = new[] { a1, "c" };
ReadOnlySpan<string> s2 = new[] { b1, "c" };
Console.WriteLine(s1.SequenceEqual(s2));
Console.WriteLine(s1.IndexOf(b1) + " " + s1.Contains(b1) + " " + s1.LastIndexOf(b1));
Console.WriteLine(s1.StartsWith(s2[..1]) + " " + s1.EndsWith(s2[1..]) + " " + s1.IndexOf(s2[1..]));
Console.WriteLine(s1.StartsWith(b1) + " " + s1.EndsWith("c") + " " + s1.EndsWith(b1) + " " + ReadOnlySpan<string>.Empty.StartsWith("x"));
string[] withNull = { "x", null, "y" };
Console.WriteLine(withNull.AsSpan().IndexOf((string)null) + " " + withNull.AsSpan().Contains("y") + " " + withNull.AsSpan().SequenceEqual(new[] { "x", null, "y" }));
// Floating point: NaN equals NaN, -0.0 equals 0.0 (double.Equals).
ReadOnlySpan<double> d1 = new[] { double.NaN, -0.0 };
ReadOnlySpan<double> d2 = new[] { double.NaN, 0.0 };
Console.WriteLine(d1.SequenceEqual(d2) + " " + d1.IndexOf(0.0) + " " + d1.IndexOf(double.NaN) + " " + new[] { 1.5f, -0f }.AsSpan().Contains(0f));
// Structs and records use their Equals.
var points = new[] { new P(1, 2), new P(3, 4) };
Console.WriteLine(points.AsSpan().IndexOf(new P(3, 4)) + " " + points.AsSpan().SequenceEqual(new[] { new P(1, 2), new P(3, 4) }));
var people = new[] { new R("ann"), new R("bob") };
Console.WriteLine(people.AsSpan().Contains(new R("bob")) + " " + people.AsSpan().LastIndexOf(new R("ann")));
var loose = new[] { new Loose(5), new Loose(-5) };
Console.WriteLine(loose.AsSpan().IndexOf(new Loose(-5)) + " " + loose.AsSpan().IndexOf(new Loose(7)));
// Comparer overloads (.NET 10).
Console.WriteLine(s1.SequenceEqual(new[] { "AB", "C" }, StringComparer.OrdinalIgnoreCase) + " " + s1.SequenceEqual(new[] { "AB", "C" }, null));
Console.WriteLine(MemoryExtensions.Contains(s1, "C", StringComparer.OrdinalIgnoreCase) + " " + s1.IndexOf("C", StringComparer.OrdinalIgnoreCase)
    + " " + s1.LastIndexOf("AB", StringComparer.OrdinalIgnoreCase) + " " + s1.IndexOf(new[] { "C" }, StringComparer.OrdinalIgnoreCase));
Console.WriteLine(s1.StartsWith("AB", StringComparer.OrdinalIgnoreCase) + " " + s1.EndsWith(new[] { "C" }, StringComparer.OrdinalIgnoreCase)
    + " " + s1.CommonPrefixLength(new[] { "AB", "d" }, StringComparer.OrdinalIgnoreCase) + " " + s1.Count("C", StringComparer.OrdinalIgnoreCase));
Console.WriteLine(new[] { 1, 2, 3 }.AsSpan().SequenceEqual(new[] { -1, -2, -3 }, new AbsComparer()));
// Integers, chars and enums keep the byte-wise fast paths.
int[] ints = { 1, 2, 3, 2 };
Console.WriteLine(ints.AsSpan().SequenceEqual(new[] { 1, 2, 3, 2 }) + " " + ints.AsSpan().LastIndexOf(2) + " " + ints.AsSpan().IndexOf(new[] { 3, 2 }));
Console.WriteLine("hello".AsSpan().IndexOf('l') + " " + "hello".AsSpan().StartsWith("he") + " " + "hello".AsSpan().EndsWith('o'));
var days = new[] { DayOfWeek.Monday, DayOfWeek.Friday };
Console.WriteLine(days.AsSpan().IndexOf(DayOfWeek.Friday) + " " + days.AsSpan().Contains(DayOfWeek.Sunday));

record struct P(int X, int Y);
record R(string Name);
readonly struct Loose : IEquatable<Loose>
{
    private readonly int _v;
    public Loose(int v) { _v = v; }
    public bool Equals(Loose other) => Math.Abs(_v) == Math.Abs(other._v);
    public override bool Equals(object o) => o is Loose l && Equals(l);
    public override int GetHashCode() => Math.Abs(_v);
}
sealed class AbsComparer : IEqualityComparer<int>
{
    public bool Equals(int x, int y) => Math.Abs(x) == Math.Abs(y);
    public int GetHashCode(int x) => Math.Abs(x);
}
