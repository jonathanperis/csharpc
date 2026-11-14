// Array.IndexOf(Array, object) where the array's element type is only known at run time (an Array from Enum.GetValues,
// a T[] typed as Array): object.Equals element by element, as .NET does, for value-type and reference-type elements.
using System;

enum Layout { None, Tile, Center, Stretch, Zoom }

static class Program
{
    static Array Values() => Enum.GetValues(typeof(Layout));

    static void Main()
    {
        Console.WriteLine(Array.IndexOf(Values(), Layout.Stretch));
        Console.WriteLine(Array.IndexOf(Values(), (Layout)17));
        Console.WriteLine(Array.IndexOf(Values(), 3)); // a boxed int is not equal to the boxed enum

        Array ints = new[] { 4, 5, 6, 5 };
        Console.WriteLine(Array.IndexOf(ints, 5));
        Console.WriteLine(Array.IndexOf(ints, 5L));
        Console.WriteLine(Array.IndexOf(ints, "6"));
        Console.WriteLine(Array.IndexOf(ints, null));

        Array strings = new[] { "a", null, "c" };
        Console.WriteLine(Array.IndexOf(strings, null));
        Console.WriteLine(Array.IndexOf(strings, "c"));
        Console.WriteLine(Array.IndexOf(strings, new string('c', 1)));

        int[] typed = { 7, 8 };
        Console.WriteLine(Array.IndexOf((Array)typed, (object)8));
        Console.WriteLine(Array.IndexOf(typed, 8));

        Array empty = Array.Empty<double>();
        Console.WriteLine(Array.IndexOf(empty, 0.0));

        try { Array.IndexOf(new int[2, 2], 0); } catch (RankException e) { Console.WriteLine(e.Message); }
        try { Array.IndexOf((Array)null!, 0); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    }
}
