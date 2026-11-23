// typeof(T).IsX in generic code, folded to constants once T is known (#466), must match .NET's answers.
using System;
using System.Collections.Generic;

static class Program
{
    enum Color { Red, Green }
    interface IShape { }
    sealed class Sealed { }
    abstract class Abstract { }
    struct Point { public int X; }
    public class Outer { public class Inner { } }

    static string Flags<T>() =>
        $"{typeof(T).Name,-12} enum={typeof(T).IsEnum} value={typeof(T).IsValueType} class={typeof(T).IsClass} " +
        $"interface={typeof(T).IsInterface} array={typeof(T).IsArray} primitive={typeof(T).IsPrimitive} " +
        $"sealed={typeof(T).IsSealed} abstract={typeof(T).IsAbstract} generic={typeof(T).IsGenericType} " +
        $"nested={typeof(T).IsNested} public={typeof(T).IsPublic} szarray={typeof(T).IsSZArray}";

    static int Count<T>(T[] items)
    {
        // a guard like the BCL's: only one branch should matter per instantiation
        if (typeof(T).IsEnum) return -items.Length;
        return typeof(T).IsValueType ? items.Length : items.Length * 10;
    }

    static void Main()
    {
        Console.WriteLine(Flags<int>());
        Console.WriteLine(Flags<Color>());
        Console.WriteLine(Flags<string>());
        Console.WriteLine(Flags<IShape>());
        Console.WriteLine(Flags<Sealed>());
        Console.WriteLine(Flags<Abstract>());
        Console.WriteLine(Flags<Point>());
        Console.WriteLine(Flags<int[]>());
        Console.WriteLine(Flags<List<int>>());
        Console.WriteLine(Flags<Outer.Inner>());
        Console.WriteLine(Flags<double>());
        Console.WriteLine(Flags<decimal>());
        Console.WriteLine(Count(new[] { Color.Red, Color.Green }));
        Console.WriteLine(Count(new[] { 1, 2, 3 }));
        Console.WriteLine(Count(new[] { "a" }));
        Type runtime = args().GetType();
        Console.WriteLine($"{runtime.IsArray} {runtime.IsEnum} {runtime.IsValueType}");
        Console.WriteLine($"{Color.Green} {1.5} {42}");
    }

    static object args() => new[] { 1 };
}
