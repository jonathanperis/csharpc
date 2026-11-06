// ReadOnlyCollection<T> through the non-generic IList/ICollection interfaces (what AggregateException.InnerExceptions
// returns): casts, IsFixedSize/IsReadOnly/SyncRoot, Contains/IndexOf with compatible and incompatible objects,
// CopyTo(Array, int) into typed, object and incompatible arrays with .NET's exceptions, and every mutator refusing.
#pragma warning disable CS8600, CS8602, CS8604, CS8625
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

var backing = new List<string> { "a", "b", "c" };
var ro = new ReadOnlyCollection<string>(backing);
IList list = ro;
ICollection collection = ro;
Console.WriteLine(list.Count + " " + list.IsFixedSize + " " + list.IsReadOnly + " " + collection.IsSynchronized);
Console.WriteLine(ReferenceEquals(collection.SyncRoot, ((ICollection)backing).SyncRoot));
Console.WriteLine(ReferenceEquals(collection.SyncRoot, collection.SyncRoot));
var plain = new ReadOnlyCollection<int>(new[] { 1, 2, 3 });
Console.WriteLine(((ICollection)plain).SyncRoot is not null);
Console.WriteLine(list[1] + " " + list.Contains("c") + " " + list.Contains("z") + " " + list.Contains(5) + " " + list.Contains(null));
Console.WriteLine(list.IndexOf("b") + " " + list.IndexOf(3) + " " + list.IndexOf(null));
Console.WriteLine(((IList)plain).Contains(2) + " " + ((IList)plain).Contains("2") + " " + ((IList)plain).IndexOf(3) + " " + ((IList)plain).Contains(null));
backing.Add("d");
Console.WriteLine(list.Count + " " + string.Join(",", ro) + " (changes show through)");

Console.WriteLine("-- mutators");
Try("set", () => list[0] = "x");
Try("add", () => list.Add("x"));
Try("clear", () => list.Clear());
Try("insert", () => list.Insert(0, "x"));
Try("remove", () => list.Remove("a"));
Try("removeAt", () => list.RemoveAt(0));
Try("generic add", () => ((ICollection<string>)ro).Add("x"));
Try("generic set", () => ((IList<string>)ro)[0] = "x");
Try("generic remove", () => ((ICollection<string>)ro).Remove("a"));
Console.WriteLine(((ICollection<string>)ro).IsReadOnly);

Console.WriteLine("-- CopyTo");
var strings = new string[6];
collection.CopyTo(strings, 1);
Console.WriteLine(string.Join("|", strings.Select(s => s ?? "-")));
var objects = new object[5];
collection.CopyTo(objects, 0);
Console.WriteLine(string.Join("|", objects.Select(s => s ?? "-")));
Try("null", () => collection.CopyTo(null, 0));
Try("negative", () => collection.CopyTo(new string[4], -1));
Try("too small", () => collection.CopyTo(new string[3], 0));
Try("offset too big", () => collection.CopyTo(new string[4], 2));
Try("multi-dimensional", () => collection.CopyTo(new string[2, 2], 0));
Try("wrong element type", () => collection.CopyTo(new int[4], 0));
Try("covariant mismatch", () => collection.CopyTo(new Uri[4], 0));
Try("base element type", () => ((ICollection)new ReadOnlyCollection<Uri>(new[] { new Uri("http://x/") })).CopyTo(new object[1], 0));

Console.WriteLine("-- empty and wrapped");
Console.WriteLine(((IList)new ReadOnlyCollection<int>(new int[0])).Count);
Try("null list", () => new ReadOnlyCollection<int>(null));
IList asList = new ReadOnlyCollection<int>(new int[] { 7, 8 });
foreach (var item in asList) Console.Write(item + ";");
Console.WriteLine();
Console.WriteLine(asList is IList<int> && asList is IReadOnlyList<int> && asList is IEnumerable<int>);

Console.WriteLine("-- AggregateException.InnerExceptions");
var ae = new AggregateException("agg", new InvalidOperationException("one"), new ArgumentException("two"));
IList inner = ae.InnerExceptions;
Console.WriteLine(inner.Count + " " + inner.IsReadOnly + " " + inner.IsFixedSize + " " + ((Exception)inner[1]).Message);
Console.WriteLine(inner.Contains(ae.InnerException) + " " + inner.IndexOf(ae.InnerExceptions[1]));
Try("inner add", () => inner.Add(new Exception()));
var copy = new Exception[2];
((ICollection)ae.InnerExceptions).CopyTo(copy, 0);
Console.WriteLine(string.Join(",", copy.Select(e => e.GetType().Name)));
Console.WriteLine(ReferenceEquals(ae.InnerExceptions, ae.InnerExceptions));
