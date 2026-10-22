// Enum generic statics, slice sub-patterns, generic math over double/decimal, HashCode, tuple
// CompareTo/Equals, empty collection expressions, sealed nested records, tuple fields in generic types.
using System.Numerics;

Console.WriteLine(Enum.TryParse<Color>("green", ignoreCase: true, out var c1) + " " + c1);
Console.WriteLine(Enum.TryParse<Color>("Purple", out var c2) + " " + c2);
Console.WriteLine(Enum.Parse<Color>("Blue") + " " + Enum.Parse<Color>("2") + " " + Enum.Parse<Perm>("Read, Write"));
Console.WriteLine(string.Join(",", Enum.GetValues<Color>()) + " | " + string.Join(",", Enum.GetNames<Perm>()));
Console.WriteLine(Enum.IsDefined(Color.Red) + " " + Enum.IsDefined((Color)7) + " " + Enum.GetName(Color.Blue));
try { Enum.Parse<Color>("Mauve"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

int[] numbers = [1, 2, 3, 4, 5];
if (numbers is [var head, .. var middle, var last]) Console.WriteLine($"{head} [{string.Join(",", middle)}] {last}");
Console.WriteLine("hello" is [var h, .. var rest] ? $"{h}+{rest}" : "?");
string[] words = ["go", "north", "quickly"];
var (verb, noun) = words switch { [var v] => (v, ""), [var v, .. var tail] => (v, string.Join(' ', tail)), _ => ("", "") };
Console.WriteLine($"{verb}/{noun}");

Console.WriteLine($"{AbsSum<double>([-1.5, 2, -3])} {AbsSum<decimal>([-1.25m, 2m])} {Negate(4.5m)} {Negate(-2.0)} {CountZero<decimal>([0m, 1m, 0.0m])}");

int h1 = HashCode.Combine(1, "a"), h2 = HashCode.Combine(1, "a"), h3 = HashCode.Combine(2, "a");
Console.WriteLine($"{h1 == h2} {h1 == h3}");
var hc = new HashCode(); hc.Add(42); hc.Add("x");
Console.WriteLine(hc.ToHashCode() == new Func<int>(() => { var o = new HashCode(); o.Add(42); o.Add("x"); return o.ToHashCode(); })());

Console.WriteLine($"{(1, "b").CompareTo((1, "a"))} {(0, "z").CompareTo((1, "a"))} {(2, 3).Equals((2, 3))} {(2, 3).GetHashCode() == (2, 3).GetHashCode()}");

Dictionary<string, int> empty = [];
empty["k"] = 1;
Console.WriteLine(empty.Count);
var row = new Holder.Row("x", 1);
var row2 = row with { Count = 2 };
Console.WriteLine($"{row} {row2} {row == row2} {row == new Holder.Row("x", 1)}");
var cache = new Pairs<int, string>();
cache.Add(1, "one"); cache.Add(2, "two");
Console.WriteLine(cache.Describe() + " " + IsNullGeneric(5) + " " + IsNullGeneric<string?>(null));

static T AbsSum<T>(T[] values) where T : INumber<T> { var s = T.Zero; foreach (var v in values) s += T.Abs(v); return s; }
static T Negate<T>(T value) where T : INumber<T> => -value;
static int CountZero<T>(T[] values) where T : INumber<T> => values.Count(v => T.IsZero(v));
static bool IsNullGeneric<T>(T value) => value is null;

enum Color { Red, Green, Blue }
[Flags] enum Perm { None = 0, Read = 1, Write = 2, Exec = 4 }

class Holder { public sealed record Row(string Name, int Count); }

class Pairs<TKey, TValue>
{
    private readonly List<(TKey Key, TValue Value)> _items = [];
    public void Add(TKey key, TValue value) => _items.Add((key, value));
    public string Describe() => string.Join(";", _items.Select(p => $"{p.Key}={p.Value}"));
}
