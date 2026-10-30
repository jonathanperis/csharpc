// dynamic: reference equality between run-time types (the binder rejects unrelated classes), enums, explicit
// interface implementations, dynamic properties, dynamic as a type argument, numeric promotion and binder errors.
using System;
using System.Collections.Generic;
using Microsoft.CSharp.RuntimeBinder;

class Animal { public virtual string Sound() => "..."; public string Name = "animal"; }
class Dog : Animal { public override string Sound() => "woof"; public new string Name = "dog"; }
class Cat : Animal { public override string Sound() => "meow"; }
class Unrelated { }
enum Color { Red, Green, Blue }
interface IShape { double Area(); }
class Square : IShape { public double Side = 2; public double Area() => Side * Side; double IShape.Area() => -1; }
class Holder { public dynamic Value { get; set; } public dynamic Get() => Value; }

class Program
{
    static void T(string label, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }
    static void Main()
    {
        dynamic dog = new Dog(), cat = new Cat(), animal = new Animal(), other = new Unrelated();
        Console.WriteLine(dog.Sound() + cat.Sound() + animal.Sound());
        Console.WriteLine(dog.Name);
        Animal asAnimal = dog;
        Console.WriteLine(asAnimal.Name);
        T("dog == cat", () => Console.WriteLine(dog == cat));
        T("dog == animal", () => Console.WriteLine(dog == animal));
        T("dog == other", () => Console.WriteLine(dog == other));
        T("dog == dog", () => Console.WriteLine(dog == dog));
        T("dog != 'x'", () => Console.WriteLine(dog != "x"));
        T("other == null", () => Console.WriteLine(other == null));
        object o = new object();
        T("dog == o", () => Console.WriteLine(dog == o));
        dynamic color = Color.Green;
        Console.WriteLine(color);
        Console.WriteLine(color + 1);
        Console.WriteLine(color | Color.Blue);
        Console.WriteLine(color == Color.Green);
        T("color + color", () => Console.WriteLine(color + color));
        Color c2 = color;
        Console.WriteLine(c2);
        T("int to enum", () => { Color c3 = (dynamic)1; Console.WriteLine(c3); });
        dynamic sq = new Square();
        Console.WriteLine(sq.Area());
        IShape shape = sq;
        Console.WriteLine(shape.Area());
        var holder = new Holder { Value = 5 };
        Console.WriteLine(holder.Value + 1);
        Console.WriteLine(holder.Get() * 2);
        holder.Value = "text";
        Console.WriteLine(holder.Value.Length);
        dynamic[] arr = { 1, "two", 3.0 };
        foreach (var item in arr) Console.Write(item.GetType().Name + " ");
        Console.WriteLine();
        var list = new List<dynamic> { 1, 2.5, "x" };
        Console.WriteLine(list[0] + list[1]);
        dynamic x = 10;
        switch (x)
        {
            case 10: Console.WriteLine("ten"); break;
            default: Console.WriteLine("other"); break;
        }
        Console.WriteLine(x is int);
        Console.WriteLine(x > 5 ? "gt" : "le");
        dynamic ch = 'a';
        Console.WriteLine(ch + 1);
        Console.WriteLine((char)(ch + 1));
        dynamic f = 1.5f;
        Console.WriteLine(f * 2);
        dynamic u = 5u;
        Console.WriteLine(u - 10);
        T("ulong + long", () => { dynamic a = 5UL; dynamic b = -1L; Console.WriteLine(a + b); });
        dynamic big = long.MaxValue;
        T("overflow", () => Console.WriteLine(checked(big + 1)));
        Console.WriteLine(unchecked(big + 1));
        dynamic s = "abc";
        Console.Write(s);
        Console.WriteLine();
        Console.WriteLine(s.Contains("b") && s.StartsWith("a"));
        T("string - int", () => Console.WriteLine(s - 1));
        T("method group", () => { var m = s.ToUpper; });
        T("write-only", () => { dynamic dd = new Dictionary<int, int>(); dd.Count = 3; });
        T("indexer missing key", () => { dynamic dd = new Dictionary<string, int>(); Console.WriteLine(dd["nope"]); });
        T("array index out of range", () => { dynamic a = new int[2]; Console.WriteLine(a[5]); });
        T("array wrong index count", () => { dynamic a = new int[2]; Console.WriteLine(a[1, 2]); });
        T("null to int", () => { dynamic n = null; int i = n; });
        T("cast fails", () => { dynamic d = "s"; var dd = (Dog)d; });
        T("bad explicit", () => { dynamic d = new Dog(); var dd = (Cat)d; });
    }
}
