// ConcurrentDictionary enumerates in .NET's bucket order: growth, removals, updates, string keys
// (non-randomized hashing, Ordinal/OrdinalIgnoreCase), Keys/Values/ToArray, Clear, and the
// constructors' and members' exceptions. Tables with more than 7 items use an explicit concurrency
// level: with the default one (Environment.ProcessorCount) .NET's own order depends on the machine.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

static string Show<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items) =>
    string.Join(" ", items.Select(kv => $"{kv.Key}={kv.Value}"));

// Default constructor, few items (no resize on machines with up to 4 cores).
var small = new ConcurrentDictionary<int, string>();
foreach (var k in new[] { 40, 3, 75, 31, 62, 9, 33 }) small[k] = "v" + k;
Console.WriteLine(Show(small));
Console.WriteLine(string.Join(",", small.Keys) + " | " + string.Join(",", small.Values) + " | " + Show(small.ToArray()));
Console.WriteLine($"{small.Count} {small.IsEmpty} {small.ContainsKey(31)} {small.TryGetValue(9, out var nine)} {nine}");

var words = new ConcurrentDictionary<string, int>();
foreach (var w in new[] { "pear", "apple", "fig", "kiwi", "banana", "plum", "cherry" }) words.TryAdd(w, w.Length);
Console.WriteLine(Show(words));

// Growth with an explicit concurrency level: 2 locks, capacity 3 (prime), budget per lock.
var grow = new ConcurrentDictionary<int, int>(2, 3);
for (var i = 0; i < 60; i++) grow[i * 7 % 61] = i;
Console.WriteLine(Show(grow));
for (var i = 0; i < 60; i += 3) grow.TryRemove(i, out _);
grow.AddOrUpdate(5, 500, (k, v) => v * 100);
grow.AddOrUpdate(1000, 1, (k, v) => v + 1);
Console.WriteLine(grow.GetOrAdd(2000, k => k / 10) + " " + grow.GetOrAdd(5, -1) + " " + grow.GetOrAdd(3000, (k, a) => k + a, 7));
Console.WriteLine(Show(grow));
Console.WriteLine($"{grow.TryUpdate(2000, 1, 200)} {grow.TryUpdate(2000, 1, 0)} {grow.TryRemove(new KeyValuePair<int, int>(1000, 2))} {grow.TryRemove(new KeyValuePair<int, int>(1000, 3))}");
Console.WriteLine(Show(grow));

// String keys: non-randomized hashing for the default, Ordinal and OrdinalIgnoreCase comparers.
foreach (var comparer in new IEqualityComparer<string>?[] { null, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase })
{
    var names = new ConcurrentDictionary<string, int>(1, 0, comparer ?? EqualityComparer<string>.Default);
    var n = 0;
    foreach (var s in new[] { "Alpha", "beta", "GAMMA", "delta", "Epsilon", "zeta", "Eta", "theta", "iota", "Kappa", "lambda", "MU", "nu", "xi",
                 "omicron", "Pi", "rho", "sigma", "TAU", "upsilon", "phi", "chi", "psi", "omega", "ALPHA", "Beta", "a", "ab", "abc", "abcd",
                 "abcde", "", "Ärger", "ärger", "straße", "STRASSE", "ǅemal", "émile", "Émile", "日本" })
        names.TryAdd(s, n++);
    Console.WriteLine($"{names.Count} {names.Comparer.GetType() == (comparer ?? EqualityComparer<string>.Default).GetType()}: {string.Join(" ", names.Keys)}");
}

// Other key types.
var longs = new ConcurrentDictionary<long, char>(3, 5);
foreach (var v in new long[] { 1L << 40, -1, 0, long.MaxValue, 12345678901, -98765, 7, 1L << 33 }) longs[v] = 'x';
Console.WriteLine(string.Join(" ", longs.Keys));
var chars = new ConcurrentDictionary<char, int>(2, 7);
foreach (var c in "the quick brown fox jumps over the lazy dog") chars.AddOrUpdate(c, 1, (_, count) => count + 1);
Console.WriteLine(Show(chars));
var days = new ConcurrentDictionary<DayOfWeek, int>(1, 2);
foreach (var d in new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Thursday, DayOfWeek.Tuesday }) days[d] = (int)d;
Console.WriteLine(Show(days));
var doubles = new ConcurrentDictionary<double, int>(2, 11);
foreach (var x in new[] { 0.5, -0.0, 0.0, 1e100, 3.25, -7.5, double.MaxValue, 2.0 }) doubles.TryAdd(x, 1);
Console.WriteLine(string.Join(" ", doubles.Keys));
var points = new ConcurrentDictionary<Point, string>(2, 5);
for (var i = 0; i < 12; i++) points[new Point(i % 4, i / 4)] = $"p{i}";
Console.WriteLine(Show(points));

// From a collection (capacity: max(31, count)), with an explicit concurrency level.
var source = Enumerable.Range(0, 40).Select(i => new KeyValuePair<int, string>(i * 13 % 97, "s" + i)).ToList();
var copied = new ConcurrentDictionary<int, string>(2, source, null);
Console.WriteLine(Show(copied));
copied.Clear();
Console.WriteLine($"{copied.Count} {copied.IsEmpty}");
for (var i = 0; i < 40; i++) copied[i * 5] = "c" + i;
Console.WriteLine(Show(copied));

// Enumerating while adding and removing (allowed; sees the current table).
var live = new ConcurrentDictionary<int, int>(1, 7);
for (var i = 0; i < 6; i++) live[i] = i;
var seen = new List<int>();
foreach (var kv in live)
{
    seen.Add(kv.Key);
    if (kv.Key == 2) { live.TryRemove(4, out _); live[100] = 0; }
}
Console.WriteLine(string.Join(",", seen) + " | " + Show(live));

// Interfaces.
IDictionary<int, string> generic = new ConcurrentDictionary<int, string>(1, 3);
generic.Add(1, "one");
generic.Add(4, "four");
try { generic.Add(1, "again"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"{generic.Remove(4)} {generic.Remove(4)} {generic.Count} {generic.IsReadOnly}");
var pairs = new KeyValuePair<int, string>[3];
generic.CopyTo(pairs, 1);
Console.WriteLine(string.Join(" ", pairs.Select(p => p.Key + ":" + (p.Value ?? "null"))));
IDictionary untyped = new ConcurrentDictionary<string, int>(1, 3);
untyped.Add("x", 1);
untyped["y"] = 2;
Console.WriteLine($"{untyped["x"]} {untyped["missing"] is null} {untyped.Contains("y")} {untyped.Contains(5)} {untyped.IsFixedSize}");
foreach (DictionaryEntry e in untyped) Console.Write($"{e.Key}->{e.Value} ");
Console.WriteLine();
try { untyped.Add(5, 1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { untyped["z"] = "text"; } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { _ = ((ICollection)untyped).SyncRoot; } catch (NotSupportedException e) { Console.WriteLine(e.Message); }

// Exceptions.
try { _ = new ConcurrentDictionary<int, int>(0, 10); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { _ = new ConcurrentDictionary<int, int>(1, -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
Console.WriteLine(new ConcurrentDictionary<int, int>(-1, 0).Count);
try { _ = new ConcurrentDictionary<int, int>(1, new[] { KeyValuePair.Create(1, 1), KeyValuePair.Create(1, 2) }, null); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { _ = small[999]; } catch (KeyNotFoundException e) { Console.WriteLine(e.Message); }
try { words.TryAdd(null!, 1); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { words.TryRemove(new KeyValuePair<string, int>(null!, 1)); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { words.GetOrAdd("x", (Func<string, int>)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

readonly struct Point(int x, int y) : IEquatable<Point>
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point p && Equals(p);
    public override int GetHashCode() => X * 31 + Y;
    public override string ToString() => $"({X},{Y})";
}
