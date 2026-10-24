// An array's GetEnumerator() called through IEnumerable<T>/IEnumerable outside a foreach returns a
// real enumerator object (it used to be the foreach loop's stack-allocated one: a crash).
using System;
using System.Collections;
using System.Collections.Generic;

int[] numbers = { 1, 2, 3 };
IEnumerator<int> typed = ((IEnumerable<int>)numbers).GetEnumerator();
while (typed.MoveNext()) Console.Write(typed.Current + " ");
Console.WriteLine(typed.MoveNext());

IEnumerator untyped = ((IEnumerable)new[] { "a", "b" }).GetEnumerator();
while (untyped.MoveNext()) Console.Write(untyped.Current + " ");
Console.WriteLine();

Console.WriteLine(Sum(Enumerate(new[] { 10, 20, 30 })));
Console.WriteLine(string.Join(",", Drain(Enumerate(new[] { "x", "y" }))));

var twice = ((IEnumerable<int>)numbers).GetEnumerator();
twice.MoveNext();
var other = ((IEnumerable<int>)numbers).GetEnumerator();
other.MoveNext();
other.MoveNext();
Console.WriteLine($"{twice.Current} {other.Current}");
using (var disposable = ((IEnumerable<int>)numbers).GetEnumerator()) { disposable.MoveNext(); Console.WriteLine(disposable.Current); }
foreach (var n in numbers) Console.Write(n * 2 + " "); // the foreach path is unchanged
Console.WriteLine();

static IEnumerator<T> Enumerate<T>(T[] items) => ((IEnumerable<T>)items).GetEnumerator();
static int Sum(IEnumerator<int> e) { var s = 0; while (e.MoveNext()) s += e.Current; return s; }
static List<T> Drain<T>(IEnumerator<T> e) { var list = new List<T>(); while (e.MoveNext()) list.Add(e.Current); return list; }
