// Array.IndexOf / LastIndexOf / Contains-style searches (issue #256): the vectorized search for bitwise-equatable element types and the
// comparer loop for the rest must agree on every start index and count, on not found, on empty arrays, and on the exceptions.
using System;
using System.Collections.Generic;

Helpers.Report("int", new[] { 5, 3, 9, 3, 7, 3, 1 }, 3, 42);
Helpers.Report<byte>("byte", new byte[] { 5, 3, 9, 3, 7, 3, 1, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 3 }, 3, 42);
Helpers.Report("char", "hello world".ToCharArray(), 'o', 'z');
Helpers.Report<long>("long", new[] { 5L, 1L << 40, 9L, 1L << 40 }, 1L << 40, -1L);
Helpers.Report<short>("short", new short[] { -5, 3, -9, 3 }, -9, 4);
Helpers.Report<bool>("bool", new[] { false, false, true, false }, true, false && true);
Helpers.Report("enum", new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday, DayOfWeek.Friday }, DayOfWeek.Friday, DayOfWeek.Saturday);
Helpers.Report("string", new[] { "a", "b", null, "b" }, "b", "z");
Helpers.Report<string>("string null", new[] { "a", "b", null, "b" }, null, "z");
Helpers.Report("double", new[] { 1.5, double.NaN, 3.0, double.NaN, -0.0 }, double.NaN, 9.0);
Helpers.Report("double zero", new[] { 1.5, double.NaN, 3.0, double.NaN, -0.0 }, 0.0, 9.0);
Helpers.Report<float>("float", new[] { 1.5f, 2.5f, 1.5f }, 1.5f, 0f);
Helpers.Report("empty int", new int[0], 1, 2);
Helpers.Report("one int", new[] { 4 }, 4, 5);

Console.WriteLine(Helpers.Try(() => Array.IndexOf((int[])null, 1)));
Console.WriteLine(Array.IndexOf(new object[] { 1, "a", 2.5 }, "a") + " " + Array.IndexOf(new object[] { 1, "a", 2.5 }, 2.5));

// Long inputs around the vector widths, the needle at every position.
int mismatches = 0, total = 0;
for (int length = 0; length <= 70; length++)
{
    var bytes = new byte[length];
    for (int i = 0; i < length; i++) bytes[i] = (byte)(i % 7);
    var ints = new int[length];
    for (int i = 0; i < length; i++) ints[i] = i % 7;
    for (int needle = 0; needle < 9; needle++)
    {
        int expectedByte = -1, expectedInt = -1;
        for (int i = 0; i < length; i++) { if (expectedByte < 0 && bytes[i] == needle) expectedByte = i; if (expectedInt < 0 && ints[i] == needle) expectedInt = i; }
        total += 2;
        if (Array.IndexOf(bytes, (byte)needle) != expectedByte) mismatches++;
        if (Array.IndexOf(ints, needle) != expectedInt) mismatches++;
    }
}
Console.WriteLine($"total={total} mismatches={mismatches}");

static class Helpers
{
    public static string Try(Func<int> f)
    {
        try { return f().ToString(); }
        catch (Exception e) { return e.GetType().Name; }
    }

    public static void Report<T>(string label, T[] array, T present, T absent)
    {
        Console.WriteLine($"{label}: {Array.IndexOf(array, present)} {Array.IndexOf(array, absent)} {Array.LastIndexOf(array, present)} " +
            $"{Try(() => Array.IndexOf(array, present, array.Length))} {Try(() => Array.IndexOf(array, present, array.Length + 1))} {Try(() => Array.IndexOf(array, present, -1))} " +
            $"{Try(() => Array.IndexOf(array, present, 0, 0))} {Try(() => Array.IndexOf(array, present, 1, array.Length))} {Try(() => Array.IndexOf(array, present, 1, array.Length - 1))} " +
            $"{Try(() => Array.LastIndexOf(array, present, array.Length - 1))} {Try(() => Array.LastIndexOf(array, present, 2, 2))}");
        var list = new List<T>(array);
        Console.WriteLine($"  list: {list.IndexOf(present)} {list.IndexOf(absent)} {Try(() => list.IndexOf(present, 2))} {list.LastIndexOf(present)} {list.Contains(absent)} {list.Contains(present)}");
    }

}
