// What generic code over reference types reports about T must be the exact instantiation, never a stand-in for shared code (ADR 0026:
// csharpc compiles such code once per canonical form under --shared-generics): typeof(T), GetType(), casts and type tests, default(T),
// the type name a generic type prints, and the frames of an exception thrown inside generic code.
#nullable disable
using System;
using System.Collections.Generic;

class Customer { public override string ToString() => "customer"; }
class Order { }

class Box<T>
{
    public T Value;
    public Box(T value) => Value = value;
    public string Describe() => $"{typeof(T).Name} {typeof(T).FullName} {Value?.GetType().Name ?? "null"} {GetType().Name} {GetType()} {default(T) == null}";
    public bool Holds(object o) => o is T;
    public T Cast(object o) => (T)o;
    public T[] Array(int n) => new T[n];
    public List<T> List() => new List<T> { Value };
    public static string Static() => typeof(Box<T>).ToString();
    public void Throw() => throw new InvalidOperationException("from " + typeof(T).Name);
    public override string ToString() => $"Box<{typeof(T).Name}>({Value})";
}

static class Generic
{
    public static string Name<T>() => typeof(T).ToString();
    public static string Pair<T, U>(T t, U u) => $"{typeof(T).Name}/{typeof(U).Name} {t?.GetType().Name}/{u?.GetType().Name}";
    public static T Make<T>() where T : new() => new T();
}

class Program
{
    static void Show<T>(T value)
    {
        var box = new Box<T>(value);
        Console.WriteLine(box.Describe());
        Console.WriteLine($"{box.Holds(value)} {box.Holds("text")} {box.Holds(new Order())} {box.Array(2).GetType()} {box.List().GetType()} {Box<T>.Static()}");
        Console.WriteLine(box);
        try { box.Cast(new Order()); } catch (InvalidCastException) { Console.WriteLine("InvalidCastException"); }
        try { box.Throw(); }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
            var frames = e.StackTrace.Split('\n');
            Console.WriteLine(frames[0].Trim().Split(" in ")[0]);
        }
    }

    static void Main()
    {
        Show("text");
        Show(new Customer());
        Show(new Order());
        Show<object>(null);
        Show(42);
        Console.WriteLine(Generic.Name<string>() + " " + Generic.Name<Customer>() + " " + Generic.Name<List<Customer>>() + " " + Generic.Name<int>());
        Console.WriteLine(Generic.Pair("a", new Customer()) + " " + Generic.Pair(new Order(), 3));
        Console.WriteLine(Generic.Make<Customer>() + " " + Generic.Make<List<string>>().GetType());
        Console.WriteLine(new Dictionary<string, Customer>().GetType() + " " + new KeyValuePair<Customer, string>(new Customer(), "v"));
    }
}
