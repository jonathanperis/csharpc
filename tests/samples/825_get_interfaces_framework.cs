// Type.GetInterfaces() of framework types lists what .NET's runtime reports: the implementation
// assembly's interfaces in its order (not the reference assemblies' alphabetical one), including the
// framework-internal ones (int's IBinaryIntegerParseAndFormatInfo<int>, Tuple's ITupleInternal).
#pragma warning disable IL2070 // GetInterfaces on a Type parameter: every type here is kept
using System;
using System.Collections.Generic;
using System.Linq;

enum Color { Red, Green }

class MyList : List<int>, IDisposable { public void Dispose() { } }

class MyException : InvalidOperationException { }

interface IShape : IComparable<IShape>, IDisposable { }

struct Pair : IEquatable<Pair>, IComparable
{
    public bool Equals(Pair other) => true;
    public int CompareTo(object? obj) => 0;
}

class Program
{
    static void Show(Type t)
    {
        var interfaces = t.GetInterfaces();
        Console.WriteLine($"{t} ({interfaces.Length}):");
        foreach (var i in interfaces)
            Console.WriteLine($"  {i}{(i.IsPublic ? "" : " [not public]")}");
    }

    static void Main()
    {
        Show(typeof(int));
        Show(typeof(long));
        Show(typeof(byte));
        Show(typeof(char));
        Show(typeof(bool));
        Show(typeof(double));
        Show(typeof(float));
        Show(typeof(decimal));
        Show(typeof(string));
        Show(typeof(object));
        Show(typeof(int[]));
        Show(typeof(string[]));
        Show(typeof(int[,]));
        Show(typeof(Color));
        Show(typeof(int?));
        Show(typeof(DateTime));
        Show(typeof(TimeSpan));
        Show(typeof(Guid));
        Show(typeof(List<int>));
        Show(typeof(List<string>));
        Show(typeof(Dictionary<string, int>));
        Show(typeof(HashSet<long>));
        Show(typeof(KeyValuePair<int, string>));
        Show(typeof(Tuple<int, string>));
        Show(typeof(ValueTuple<int, int>));
        Show(typeof(IEnumerable<int>));
        Show(typeof(IList<string>));
        Show(typeof(IDictionary<string, object>));
        Show(typeof(Exception));
        Show(typeof(ArgumentNullException));
        Show(typeof(MyList));
        Show(typeof(MyException));
        Show(typeof(IShape));
        Show(typeof(Pair));

        var hidden = typeof(int).GetInterfaces().Last();
        Console.WriteLine($"{hidden.Name} | {hidden.Namespace} | public {hidden.IsPublic} | interface {hidden.IsInterface} | generic {hidden.IsGenericType} | arguments {string.Join(",", hidden.GetGenericArguments().Select(a => a.Name))}");
        Console.WriteLine($"int implements it: {hidden.IsAssignableFrom(typeof(int))}, long does: {hidden.IsAssignableFrom(typeof(long))}");
        Console.WriteLine($"IComparable<int> found: {typeof(int).GetInterfaces().Contains(typeof(IComparable<int>))}");
    }
}
