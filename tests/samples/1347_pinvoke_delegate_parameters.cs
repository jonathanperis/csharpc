#:property AllowUnsafeBlocks=true
// Delegates passed to P/Invokes: libc's qsort and bsearch call back into managed code through the pointer the marshaller
// makes for the delegate (a lambda, a method group, a stored delegate, a closure), during the call.
using System;
using System.Runtime.InteropServices;

unsafe
{
    int[] values = { 5, -3, 9, 0, 2, 7 };
    fixed (int* p = values) Native.qsort(p, (nuint)values.Length, sizeof(int), (a, b) => (*(int*)a).CompareTo(*(int*)b));
    Console.WriteLine(string.Join(",", values));

    Native.Compare descending = (a, b) => (*(int*)b).CompareTo(*(int*)a);
    fixed (int* p = values) Native.qsort(p, (nuint)values.Length, sizeof(int), descending);
    Console.WriteLine(string.Join(",", values));

    fixed (int* p = values) Native.qsort(p, (nuint)values.Length, sizeof(int), Ordering.ByAbsoluteValue);
    Console.WriteLine(string.Join(",", values));

    // A closure: the order comes from captured state.
    string[] words = { "pear", "fig", "apple", "kiwi", "banana", "cherry" };
    var indexes = new int[] { 0, 1, 2, 3, 4, 5 };
    fixed (int* p = indexes) Native.qsort(p, (nuint)indexes.Length, sizeof(int), (a, b) => string.CompareOrdinal(words[*(int*)a], words[*(int*)b]));
    Console.WriteLine(string.Join(",", Array.ConvertAll(indexes, i => words[i])));

    int[] sorted = { -4, -1, 0, 3, 8, 21 };
    fixed (int* p = sorted)
    {
        int key = 8, missing = 5;
        var found = (int*)Native.bsearch(&key, p, (nuint)sorted.Length, sizeof(int), Ordering.Ascending);
        var notFound = Native.bsearch(&missing, p, (nuint)sorted.Length, sizeof(int), Ordering.Ascending);
        Console.WriteLine($"bsearch 8 at {found - p}, 5 {(notFound == null ? "not found" : "found")}");
    }
}

static unsafe class Ordering
{
    public static int Ascending(void* a, void* b) => (*(int*)a).CompareTo(*(int*)b);
    public static int ByAbsoluteValue(void* a, void* b)
    {
        int x = *(int*)a, y = *(int*)b;
        int byAbs = Math.Abs(x).CompareTo(Math.Abs(y));
        return byAbs != 0 ? byAbs : x.CompareTo(y);
    }
}

static unsafe class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Compare(void* a, void* b);
    [DllImport("libc")] public static extern void qsort(void* items, nuint count, nuint size, Compare compare);
    [DllImport("libc")] public static extern void* bsearch(void* key, void* items, nuint count, nuint size, Compare compare);
}
