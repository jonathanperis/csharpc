// Dictionary<TKey, TValue> through the non-generic IDictionary and ICollection, as System.Data's
// DbConnectionStringBuilder uses its Dictionary<string, object>: indexer, Add, Contains, Remove, the
// DictionaryEntry enumerator, Keys/Values as ICollection, CopyTo, and the argument errors.
using System.Collections;

var generic = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
IDictionary d = generic;
d["three"] = 3;
d.Add("four", 4);
Console.WriteLine($"{d.Count} {d["two"]} {d["missing"] ?? "null"} {d[42] ?? "null"} {d.Contains("one")} {d.Contains(1)}");
d.Remove("one");
d.Remove(17);
Console.WriteLine($"{d.IsFixedSize} {d.IsReadOnly} {((ICollection)d).IsSynchronized} {ReferenceEquals(((ICollection)d).SyncRoot, d)}");
foreach (DictionaryEntry e in d) Console.WriteLine($"  {e.Key} = {e.Value}");
var en = d.GetEnumerator();
while (en.MoveNext()) Console.WriteLine($"  {en.Key}/{en.Value}/{en.Current.GetType().Name}");
foreach (var k in d.Keys) Console.Write(k + " ");
Console.WriteLine();
foreach (var v in d.Values) Console.Write(v + " ");
Console.WriteLine();

var objects = new object[4];
((ICollection)d).CopyTo(objects, 1);
Console.WriteLine(string.Join(", ", objects.Select(o => o?.ToString() ?? "null")));
var entries = new DictionaryEntry[3];
((ICollection)d).CopyTo(entries, 0);
Console.WriteLine(string.Join(", ", entries.Select(e => $"{e.Key}:{e.Value}")));
var keys = new object[3];
d.Keys.CopyTo(keys, 0);
Console.WriteLine(string.Join(",", keys));
var values = new int[3];
d.Values.CopyTo(values, 0);
Console.WriteLine(string.Join(",", values));

Try(() => d[5] = 1);
Try(() => d["x"] = "not an int");
Try(() => d["x"] = null);
Try(() => d.Add(null!, 1));
Try(() => d.Add("two", 2));
Try(() => ((ICollection)d).CopyTo(new object[2], 0));
Try(() => ((ICollection)d).CopyTo(new string[3], 0));
Try(() => ((ICollection)d).CopyTo(new object[3], 4));
Try(() => ((ICollection)d).CopyTo(new object[2, 2], 0));
Try(() => d.Contains(null!));

IDictionary nullable = new Dictionary<string, string?>();
nullable["a"] = null;
Console.WriteLine(nullable.Contains("a") + " " + (nullable["a"] ?? "null"));

static void Try(Action a)
{
    try { a(); Console.WriteLine("ok"); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}
