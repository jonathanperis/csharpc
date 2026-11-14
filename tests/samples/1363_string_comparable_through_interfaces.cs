// A string used through IComparable<string> and IEquatable<string> (an interface-typed variable or parameter, not a constrained
// generic call): the calls dispatch through string's interface map, which must hold both.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

static class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)] static int Compare(IComparable<string> c, string other) => c.CompareTo(other);
    [MethodImpl(MethodImplOptions.NoInlining)] static bool Same(IEquatable<string> e, string? other) => e.Equals(other);

    static void Main()
    {
        Console.WriteLine(Compare("pear", "apple") > 0);
        Console.WriteLine(Compare("apple", "pear") < 0);
        Console.WriteLine(Compare("same", "same"));
        Console.WriteLine(Compare("a", null!) > 0);
        Console.WriteLine(Same("kiwi", "kiwi") + " " + Same("kiwi", "Kiwi") + " " + Same("kiwi", null));
        var comparables = new List<IComparable<string>> { "b", "a", "c" };
        comparables.Sort((x, y) => x.CompareTo((string)y));
        foreach (var c in comparables) Console.Write(c + " ");
        Console.WriteLine();
        IEquatable<string>[] equatables = { "x", "y" };
        Console.WriteLine(equatables[1].Equals("y"));
    }
}
