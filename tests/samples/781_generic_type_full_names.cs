// Type.FullName / AssemblyQualifiedName of generic instantiations (csharpc composes them at run time
// from the definition's and the arguments' names), nested, over arrays, BCL and program types.
using System;
using System.Collections.Generic;

class Outer<T>
{
    public class Inner<U> { }
    public class Plain { }
}

struct Pair<A, B> { }

static class Program
{
    static void Show(Type t)
    {
        Console.WriteLine(t.FullName);
        Console.WriteLine("  " + t.AssemblyQualifiedName);
        Console.WriteLine("  " + t);
    }

    static void Main()
    {
        Show(typeof(List<int>));
        Show(typeof(Dictionary<string, List<int>>));
        Show(typeof(List<int[]>));
        Show(typeof(List<Dictionary<string, int>[]>));
        Show(typeof(Outer<string>.Inner<long>));
        Show(typeof(Outer<Pair<int, string>>.Plain));
        Show(typeof(Pair<Outer<int>, KeyValuePair<string, double>>));
        Show(typeof(Func<int, Pair<byte, sbyte>, List<Outer<char>>>));
        Show(new List<Pair<int, int>>().GetType());
        Show(typeof(Pair<int, int>?));
        Show(typeof(List<>));
        Show(typeof(IEnumerable<Pair<string, Outer<int>.Plain>>));
    }
}
