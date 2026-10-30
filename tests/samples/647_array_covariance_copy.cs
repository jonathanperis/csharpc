// Array.Copy / CopyTo / Clone between reference arrays follow the runtime element types (Array.CopyImpl):
// a plain copy when the source's elements fit, a cast per element when they may (InvalidCastException at
// the first misfit, after copying the ones before it), else ArrayTypeMismatchException.
#nullable disable
using System;
using System.Collections.Generic;

class Animal { public override string ToString() => GetType().Name; }
class Dog : Animal { }
class Cat : Animal { }

static class Program
{
    static void Try(string name, Action action)
    {
        try { action(); Console.WriteLine(name + ": ok"); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().FullName + ": " + e.Message); }
    }

    static void Main()
    {
        object[] mixed = { "a", 1 };
        object[] texts = { "a", "b" };
        Try("object[] -> string[]", () => Array.Copy(mixed, new string[2], 2));
        Try("object[] of strings -> string[]", () => { var d = new string[2]; Array.Copy(texts, d, 2); Console.WriteLine(string.Join(",", d)); });
        Try("CopyTo object[] -> string[]", () => mixed.CopyTo(new string[2], 0));
        object[] covariant = new string[2];
        Try("object[] -> string[] seen as object[]", () => Array.Copy(mixed, covariant, 2));
        Console.WriteLine(covariant[0] ?? "null");
        Try("string[] -> object[]", () => { var d = new object[1]; Array.Copy(new string[] { "q" }, d, 1); Console.WriteLine(d[0]); });

        Animal[] dogs = new Dog[1], cats = new Cat[] { new Cat() };
        Try("Cat[] -> Dog[], none", () => Array.Copy(cats, dogs, 0));
        Try("Cat[] -> Dog[]", () => Array.Copy(cats, dogs, 1));
        Try("Cat[] -> Animal[]", () => { var d = new Animal[1]; Array.Copy(cats, d, 1); Console.WriteLine(d[0]); });
        Animal[] someCats = new Cat[2];
        Try("Animal[] of cats -> Cat[]", () => { Array.Copy(new Animal[] { new Cat() }, someCats, 1); Console.WriteLine(someCats[0]); });
        Try("Animal[] -> Cat[]", () => Array.Copy(new Animal[] { new Cat(), new Dog() }, someCats, 2));
        Try("Animal[] -> Cat[] static", () => Array.Copy(new Animal[] { new Cat(), new Dog() }, new Cat[2], 2));
        Try("same array", () => { Animal[] a = { new Dog(), new Cat() }; Array.Copy(a, 0, a, 1, 1); Console.WriteLine(a[1]); });

        Try("List<object>.CopyTo string[]", () => { var l = new List<object> { "a" }; object[] d = new string[1]; l.CopyTo(d); Console.WriteLine(d[0]); });
        Try("List<object>.CopyTo string[] bad", () => { var l = new List<object> { 1 }; object[] d = new string[1]; l.CopyTo(d); });

        object[] original = new string[] { "a" };
        Try("Clone keeps the type", () => { var c = (object[])original.Clone(); Console.WriteLine(c.GetType()); c[0] = 3; });
    }
}
