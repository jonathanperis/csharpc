// Comparer<T>.Default for a T without IComparable / IComparable<T> (#246): the direct call throws .NET's ArgumentException,
// same references and nulls compare without throwing, and sorts wrap the failure in InvalidOperationException.
using System;
using System.Collections.Generic;

static class Program
{
    struct Plain { public int A; }
    sealed class Node { public int A; }

    static void Run(string label, Action action)
    {
        try { action(); Console.WriteLine($"{label}: ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message} | inner {e.InnerException?.GetType().Name ?? "none"}: {e.InnerException?.Message}"); }
    }

    static int Compare<T>(T a, T b) => Comparer<T>.Default.Compare(a, b);

    static void Main()
    {
        var node = new Node();
        Run("struct", () => Comparer<Plain>.Default.Compare(new Plain(), new Plain()));
        Run("class", () => Comparer<Node>.Default.Compare(new Node(), new Node()));
        Console.WriteLine($"same reference: {Comparer<Node>.Default.Compare(node, node)}");
        Console.WriteLine($"null first: {Comparer<Node>.Default.Compare(null, node)}, null second: {Comparer<Node>.Default.Compare(node, null)}, both: {Comparer<Node>.Default.Compare(null, null)}");
        Run("generic struct", () => Compare(new Plain { A = 1 }, new Plain { A = 2 }));
        Run("Array.Sort", () => Array.Sort(new[] { new Plain { A = 2 }, new Plain { A = 1 } }));
        Run("List.Sort", () => new List<Node> { new Node(), new Node() }.Sort());
        Run("BinarySearch", () => Array.BinarySearch(new[] { new Plain(), new Plain() }, new Plain()));
        Run("single element sort", () => Array.Sort(new[] { new Plain() }));
    }
}
