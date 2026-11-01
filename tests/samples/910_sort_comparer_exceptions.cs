// What a comparison throws during a sort surfaces wrapped, as .NET's ArraySortHelper does: an
// InvalidOperationException("Failed to compare two elements in the array.") with the original as its
// inner exception, and an IndexOutOfRangeException as an ArgumentException naming the comparer.
using System;
using System.Collections.Generic;

Show("List<T>.Sort(Comparison<T>)", () => new List<int> { 3, 1, 2 }.Sort((a, b) => throw new FormatException("cmp")));
Show("List<T>.Sort(IComparer<T>)", () => new List<int> { 3, 1, 2 }.Sort(new Throwing()));
Show("List<T>.Sort() of non-comparable objects", () => new List<object> { new object(), new object() }.Sort());
Show("List<T>.Sort() with a throwing CompareTo", () => new List<Bad> { new Bad(), new Bad() }.Sort());
Show("Array.Sort(T[], Comparison<T>)", () => Array.Sort(new[] { "b", "a" }, (x, y) => throw new InvalidCastException("strings")));
Show("Array.Sort(T[], int, int, IComparer<T>)", () => Array.Sort(new[] { 4, 3, 2, 1 }, 1, 3, new Throwing()));
Show("comparison throwing IndexOutOfRangeException", () => new List<int> { 3, 1, 2 }.Sort((a, b) => throw new IndexOutOfRangeException()));
Show("IComparer throwing IndexOutOfRangeException", () => new List<int> { 3, 1, 2 }.Sort(new OutOfRange()));
Show("nothing to compare", () => { new List<int> { 1 }.Sort((a, b) => throw new FormatException()); Console.WriteLine("one element: not compared"); });

static void Show(string name, Action action)
{
    try { action(); }
    catch (Exception e)
    {
        Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}");
        if (e.InnerException is { } inner) Console.WriteLine($"  inner {inner.GetType().Name}: {inner.Message}");
    }
}

sealed class Throwing : IComparer<int>
{
    public int Compare(int x, int y) => throw new NotSupportedException("comparer");
    public override string ToString() => "Throwing comparer";
}

sealed class OutOfRange : IComparer<int>
{
    public int Compare(int x, int y) => throw new IndexOutOfRangeException();
    public override string ToString() => "OutOfRange comparer";
}

sealed class Bad : IComparable<Bad>
{
    public int CompareTo(Bad? other) => throw new ApplicationException("CompareTo");
}
