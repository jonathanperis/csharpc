// Arrays implement the non-generic IEnumerable, ICollection and IList, and the generic
// collection interfaces of exactly their element type (plus array covariance for reference
// elements). OfType/Cast over arrays go through the non-generic IEnumerable.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

object[] objects = { 1, "a", null!, 2.5 };
string[] strings = { "b", "c" };
int[] ints = { 3, 4, 5 };
double[] doubles = { 1.5 };
Point[] points = { new Point(1, 2) };
Day[] days = { Day.Mon, Day.Tue };
int[][] jagged = { new[] { 1 }, new[] { 2, 3 } };

foreach (object o in new object[] { objects, strings, ints, doubles, points, days, jagged, args })
{
    Console.WriteLine($"{o.GetType().Name}: IEnumerable={o is IEnumerable} ICollection={o is ICollection} IList={o is IList} " +
        $"IEnumerable<int>={o is IEnumerable<int>} IEnumerable<object>={o is IEnumerable<object>} IList<string>={o is IList<string>} " +
        $"IReadOnlyList<int[]>={o is IReadOnlyList<int[]>}");
    var parts = new List<string>();
    foreach (var x in (IEnumerable)o) parts.Add(x?.ToString() ?? "null");
    Console.WriteLine("  items: " + string.Join(" ", parts) + " count=" + ((ICollection)o).Count);
}

object[] numbers = { null!, 1.0, "two", 3, "four", 5, "six", 7.0 };
Console.WriteLine(string.Join(",", numbers.OfType<double>()));
Console.WriteLine(string.Join(",", numbers.OfType<string>()));
Console.WriteLine(string.Join(",", numbers.OfType<int>()));
Console.WriteLine(string.Join(",", strings.Cast<object>()));
Console.WriteLine(string.Join(",", new object[] { 1, 2 }.Cast<int>()));
try { Console.WriteLine(string.Join(",", new object[] { 1.0, "x" }.Cast<double>())); }
catch (InvalidCastException) { Console.WriteLine("InvalidCastException"); }
Console.WriteLine(string.Join(",", ((IEnumerable)days).Cast<Day>()));

IList list = ints;
Console.WriteLine($"{list[1]} {list.Contains(4)} {list.Contains(9)} {list.IndexOf(5)} {list.IsFixedSize} {list.IsReadOnly} {list.Count}");
list[0] = 30;
Console.WriteLine(string.Join(",", ints));
try { list.Add(6); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { list.RemoveAt(0); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
IList slist = strings;
Console.WriteLine($"{slist.IndexOf("c")} {slist.Contains("b")} {slist.Contains(null)}");
slist.Clear();
Console.WriteLine(strings[0] == null);

// Element types the corelib shadows (List<T>, Dictionary<K,V>) work like any other.
List<int>[] lists = { new() { 1 }, new() { 2, 3 } };
IEnumerable<List<int>> listSequence = lists;
Console.WriteLine(listSequence.Sum(l => l.Count) + " " + ((object)lists is IEnumerable<List<int>>) + " " + ((object)lists is IReadOnlyList<Dictionary<int, int>>));
List<string>[] stringLists = { new() { "x" } }; // same display name as List<int>[], other interfaces
IEnumerable<List<string>> stringListSequence = stringLists;
Console.WriteLine(stringListSequence.First()[0] + " " + ((object)stringLists is IEnumerable<List<int>>) + " " + ((object)lists is IEnumerable<List<string>>));
Dictionary<string, int>[] maps = { new() { ["a"] = 1 } };
Console.WriteLine(maps.Select(m => m["a"]).First() + " " + ((object)maps is IEnumerable<IDictionary<string, int>>));

IEnumerable<object> covariant = strings;
Console.WriteLine(covariant.Count());

record struct Point(int X, int Y);
enum Day { Mon, Tue }
