// Type.GetType(string[, throwOnError[, ignoreCase]]) for types the program uses: user, nested, BCL,
// generic and array names, assembly-qualified names, case folding, not-found and TypeLoadException.
#pragma warning disable IL2057
using System;
using System.Collections.Generic;

namespace Shapes
{
    public class Circle { public class Center { } }
}

class Program
{
    static void Show(string name)
    {
        Type? t = Type.GetType(name);
        Console.WriteLine($"[{name}] -> {(t == null ? "null" : t.FullName)}");
    }

    static void Main()
    {
        // Make sure the program uses these types.
        object[] keep = { new Shapes.Circle(), new Shapes.Circle.Center(), new List<int>(), new int[0], new Dictionary<string, int>() };
        Console.WriteLine(keep.Length);

        Show("Shapes.Circle");
        Show("Shapes.Circle+Center");
        Show("System.Int32");
        Show("System.String, System.Private.CoreLib");
        Show("System.Collections.Generic.List`1[System.Int32]");
        Show("System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]");
        Show("System.Collections.Generic.Dictionary`2[System.String,System.Int32]");
        Show("System.Int32[]");
        Show("Shapes.Square");
        Show("shapes.circle");
        Console.WriteLine(Type.GetType("shapes.circle", false, true)?.FullName ?? "null");
        Console.WriteLine(Type.GetType("Shapes.Circle", true) == typeof(Shapes.Circle));
        try { Type.GetType("Shapes.Square", true); } catch (TypeLoadException e) { Console.WriteLine(e.GetType().Name); }
        try { Type.GetType(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }
}
