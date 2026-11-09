// The messages of Dictionary's missing-key and duplicate-key exceptions, whose text formats the key like string.Format("{0}", key) does
// (IFormattable.ToString(null, culture) for formattable keys, else ToString(), null as the empty string), for the common key types and
// for the ones that need the general path: enums, structs, tuples, a formattable class whose ToString() and ToString(null, culture) differ.
using System;
using System.Collections.Generic;

enum Color { Red, Green }

struct Point
{
    public int X, Y;
    public override string ToString() => $"({X}, {Y})";
}

class Formattable : IFormattable
{
    public override string ToString() => "plain";
    public string ToString(string format, IFormatProvider provider) => "formatted:" + (format ?? "null");
}

class Program
{
    static void Missing<K>(K key)
    {
        var dictionary = new Dictionary<K, int>();
        try { _ = dictionary[key]; }
        catch (KeyNotFoundException e) { Console.WriteLine(e.Message); }
    }

    static void Duplicate<K>(K key)
    {
        var dictionary = new Dictionary<K, int>();
        dictionary.Add(key, 1);
        try { dictionary.Add(key, 2); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }

    static void Both<K>(K key) { Missing(key); Duplicate(key); }

    static void Main()
    {
        Both("text");
        Both(42);
        Both(-42);
        Both(5_000_000_000L);
        Both(7u);
        Both(ulong.MaxValue);
        Both('c');
        Both((short)-3);
        Both(Color.Green);
        Both(new Point { X = 1, Y = -2 });
        Both((1, "a"));
        Both(new Guid("11111111-2222-3333-4444-555555555555"));
        Both(1.5);
        Both(2.5m);
        Both(true);
        Both(new Formattable());
        Both(new object[] { 1 }.GetType());
        Both((object)"boxed");
        Both((object)12);
        try { Missing<string>(null); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        // The other collections that name the key in a message
        try { _ = new SortedDictionary<int, int>()[3]; } catch (KeyNotFoundException e) { Console.WriteLine(e.Message); }
        try { new HashSet<int>().Add(1); Console.WriteLine(new HashSet<int> { 1 }.Add(1)); } catch (Exception e) { Console.WriteLine(e.Message); }
    }
}
