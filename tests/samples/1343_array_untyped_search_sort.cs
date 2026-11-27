// The untyped Array.IndexOf / LastIndexOf / Sort(Array, int, int, IComparer) that ArrayList's framework IL calls (#372), on arrays whose
// element type the call site doesn't know: object[], string[], int[], double[] (NaN and -0.0), an enum array, a struct array; the
// argument checks and their messages; and ArrayList's own IndexOf/LastIndexOf/Sort/BinarySearch, ranged and through Adapter.
using System;
using System.Collections;

static class Program
{
    static void T(string label, Func<object?> f)
    {
        try { Console.WriteLine(label + ": " + (f() ?? "null")); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + " " + e.Message); }
    }

    sealed class Reverse : IComparer
    {
        public int Compare(object? x, object? y) => Comparer.Default.Compare(y, x);
    }

    sealed class ByLength : IComparer
    {
        public int Compare(object? x, object? y) => ((string)x!).Length.CompareTo(((string)y!).Length);
    }

    sealed class Throwing : IComparer
    {
        public int Compare(object? x, object? y) => throw new InvalidCastException("nope");
    }

    enum Color { Red, Green, Blue }
    struct Pair { public int A; public string B; public override string ToString() => A + B; }

    static void Main()
    {
        Array objects = new object?[] { 1, "two", null, 3.0, "two", null, 1L };
        Array strings = new[] { "a", "b", "c", "b", "a" };
        Array ints = new[] { 5, 3, 5, 1, 5 };
        Array doubles = new[] { 0.0, double.NaN, -0.0, 1.5 };
        Array colors = new[] { Color.Red, Color.Blue, Color.Green, Color.Blue };
        Array pairs = new[] { new Pair { A = 1, B = "x" }, new Pair { A = 2, B = "y" }, new Pair { A = 1, B = "x" } };

        T("objects IndexOf two", () => Array.IndexOf(objects, "two"));
        T("objects IndexOf null", () => Array.IndexOf(objects, null));
        T("objects IndexOf 1L", () => Array.IndexOf(objects, 1L));
        T("objects IndexOf (int)1 from 1", () => Array.IndexOf(objects, 1, 1));
        T("objects LastIndexOf two", () => Array.LastIndexOf(objects, "two"));
        T("objects LastIndexOf null 4", () => Array.LastIndexOf(objects, null, 4));
        T("objects LastIndexOf null 4 2", () => Array.LastIndexOf(objects, null, 4, 2));
        T("strings IndexOf b 2 3", () => Array.IndexOf(strings, "b", 2, 3));
        T("strings LastIndexOf a", () => Array.LastIndexOf(strings, "a"));
        T("ints IndexOf 5 1 3", () => Array.IndexOf(ints, 5, 1, 3));
        T("ints IndexOf 5L", () => Array.IndexOf(ints, 5L));
        T("ints IndexOf null", () => Array.IndexOf(ints, null));
        T("ints LastIndexOf 5 3", () => Array.LastIndexOf(ints, 5, 3));
        T("doubles IndexOf NaN", () => Array.IndexOf(doubles, double.NaN));
        T("doubles IndexOf -0.0", () => Array.IndexOf(doubles, -0.0));
        T("doubles LastIndexOf 0.0", () => Array.LastIndexOf(doubles, 0.0));
        T("colors IndexOf Blue", () => Array.IndexOf(colors, Color.Blue));
        T("colors LastIndexOf Blue", () => Array.LastIndexOf(colors, Color.Blue));
        T("colors IndexOf 2", () => Array.IndexOf(colors, 2));
        T("pairs IndexOf", () => Array.IndexOf(pairs, new Pair { A = 1, B = "x" }, 1));
        T("empty LastIndexOf", () => Array.LastIndexOf(new object[0], null, 5, 9));

        T("IndexOf null array", () => Array.IndexOf((Array)null!, 1, 0, 0));
        T("IndexOf start -1", () => Array.IndexOf(ints, 1, -1, 1));
        T("IndexOf start 6", () => Array.IndexOf(ints, 1, 6, 0));
        T("IndexOf start 5 count 0", () => Array.IndexOf(ints, 1, 5, 0));
        T("IndexOf count -1", () => Array.IndexOf(ints, 1, 0, -1));
        T("IndexOf count 6", () => Array.IndexOf(ints, 1, 0, 6));
        T("IndexOf 2D", () => Array.IndexOf(new int[2, 2], 0, 0, 1));
        T("LastIndexOf start 5", () => Array.LastIndexOf(ints, 1, 5, 1));
        T("LastIndexOf count -1", () => Array.LastIndexOf(ints, 1, 2, -1));
        T("LastIndexOf count 4 at 2", () => Array.LastIndexOf(ints, 1, 2, 4));

        var sortable = new object[] { 5, 1, 4, 2, 3, 0 };
        Array.Sort(sortable, 1, 4, null);
        Console.WriteLine("Sort 1..4: " + string.Join(",", sortable));
        Array.Sort(sortable, 0, 6, new Reverse());
        Console.WriteLine("Sort reverse: " + string.Join(",", sortable));
        var words = new object[] { "ccc", "a", "bb", "dd", "e", "fff", "g" };
        Array.Sort(words, 0, words.Length, new ByLength());
        Console.WriteLine("Sort by length (unstable order of equal keys): " + string.Join(",", words));
        var numbers = new[] { 9, 8, 7, 6, 5 };
        Array.Sort((Array)numbers, 1, 3, new Reverse());
        Console.WriteLine("int[] untyped ranged sort: " + string.Join(",", numbers));
        T("Sort length 6 at 1", () => { Array.Sort(sortable, 1, 6, null); return "sorted"; });
        T("Sort index -1", () => { Array.Sort(sortable, -1, 1, null); return "sorted"; });
        T("Sort length -1", () => { Array.Sort(sortable, 0, -1, null); return "sorted"; });
        T("Sort throwing comparer", () => { Array.Sort(new object[] { 1, 2 }, 0, 2, new Throwing()); return "sorted"; });
        T("Sort one element throwing comparer", () => { Array.Sort(new object[] { 1, 2 }, 0, 1, new Throwing()); return "sorted"; });
        T("Sort 2D", () => { Array.Sort(new int[2, 2], 0, 1, null); return "sorted"; });
        T("Sort 2D IComparer", () => { Array.Sort(new int[2, 2], (IComparer?)null); return "sorted"; });
        T("BinarySearch 2D", () => Array.BinarySearch(new int[2, 2], 0));
        T("Sort mixed types", () => { Array.Sort(new object[] { 1, "x" }, 0, 2, null); return "sorted"; });

        var list = new ArrayList { "pear", "apple", null, "fig", "apple", 7 };
        T("ArrayList IndexOf apple", () => list.IndexOf("apple"));
        T("ArrayList IndexOf apple 2", () => list.IndexOf("apple", 2));
        T("ArrayList IndexOf apple 2 2", () => list.IndexOf("apple", 2, 2));
        T("ArrayList LastIndexOf apple", () => list.LastIndexOf("apple"));
        T("ArrayList LastIndexOf null 3 2", () => list.LastIndexOf(null, 3, 2));
        T("ArrayList Contains 7", () => list.Contains(7));
        T("ArrayList IndexOf 9", () => list.IndexOf(9));
        T("ArrayList IndexOf out of range", () => list.IndexOf("x", 7));
        list.RemoveAt(5);
        list.Remove(null);
        list.Sort();
        Console.WriteLine("ArrayList sorted: " + string.Join(",", list.ToArray()));
        list.Sort(1, 2, new Reverse());
        Console.WriteLine("ArrayList ranged reverse: " + string.Join(",", list.ToArray()));
        list.Sort();
        T("ArrayList BinarySearch fig", () => list.BinarySearch("fig"));
        T("ArrayList BinarySearch grape", () => list.BinarySearch("grape"));
        T("ArrayList Sort mixed", () => { new ArrayList { 1, "a" }.Sort(); return "sorted"; });

        IList inner = new System.Collections.Generic.List<string> { "z", "y", "x", "y" };
        var adapter = ArrayList.Adapter(inner);
        T("Adapter IndexOf y", () => adapter.IndexOf("y"));
        T("Adapter LastIndexOf y", () => adapter.LastIndexOf("y"));
        adapter.Sort();
        Console.WriteLine("Adapter sorted: " + string.Join(",", inner.Cast()));
        adapter.Reverse(0, 2);
        Console.WriteLine("Adapter reversed 0..1: " + string.Join(",", inner.Cast()));
        var fixedList = ArrayList.FixedSize(new ArrayList { 3, 1, 2 });
        fixedList.Sort();
        Console.WriteLine("FixedSize sorted: " + string.Join(",", fixedList.ToArray()));
        T("FixedSize Add", () => fixedList.Add(4));
        var readOnly = ArrayList.ReadOnly(new ArrayList { 1 });
        T("ReadOnly Sort", () => { readOnly.Sort(); return "sorted"; });
        var range = new ArrayList { 10, 20, 30, 40 }.GetRange(1, 2);
        T("GetRange IndexOf 30", () => range.IndexOf(30));
    }

    static System.Collections.Generic.IEnumerable<object?> Cast(this IList list)
    {
        foreach (var item in list) yield return item;
    }
}
