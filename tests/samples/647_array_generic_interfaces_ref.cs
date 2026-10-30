// Generic interfaces (IList<T>, ICollection<T>, IReadOnlyList<T>, IEnumerable<T>) on arrays of reference
// types, called through the interface: csharpc shares one implementation across reference element types,
// except GetEnumerator and, for types implementing IEquatable<>, Contains/IndexOf (EqualityComparer<T>
// uses IEquatable<T>.Equals, which may disagree with Equals(object)).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

class Animal { public string Name; public Animal(string n) { Name = n; } public override string ToString() => Name; }
class Dog : Animal { public Dog(string n) : base(n) { } }
class Cat : Animal { public Cat(string n) : base(n) { } }

// IEquatable<T> says "same Id"; Equals(object) is reference equality: the two disagree on purpose.
sealed class Key : IEquatable<Key>
{
    public int Id;
    public Key(int id) { Id = id; }
    public bool Equals(Key other) => other != null && other.Id == Id;
}

sealed class Plain
{
    public int Id;
    public Plain(int id) { Id = id; }
    public override bool Equals(object o) => o is Plain p && p.Id == Id;
    public override int GetHashCode() => Id;
}

class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)] static T As<T>(object o) => (T)o;

    static void Main()
    {
        IList<string> words = As<IList<string>>(new[] { "b", "a", "c" });
        Console.WriteLine($"{words.Count} {words[1]} {words.IndexOf("c")} {words.Contains("zz")} {words.IsReadOnly}");
        words[0] = "B";
        string[] copy = new string[5];
        words.CopyTo(copy, 1);
        Console.WriteLine(string.Join("|", copy));
        try { words.Add("x"); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
        try { words.RemoveAt(0); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
        try { As<ICollection<string>>(words).Clear(); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
        try { words.CopyTo(new string[2], 0); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
        try { Console.WriteLine(words[3]); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

        IReadOnlyList<string> ro = As<IReadOnlyList<string>>(words);
        Console.WriteLine(ro.Count + " " + ro[2]);
        foreach (string w in As<IEnumerable<string>>(words)) Console.Write(w + ";");
        Console.WriteLine();
        IEnumerator<string> e1 = As<IEnumerable<string>>(words).GetEnumerator();
        Console.WriteLine(e1 is IEnumerator<string>);

        IList<Key> keys = As<IList<Key>>(new[] { new Key(1), new Key(2) });
        Console.WriteLine(keys.IndexOf(new Key(2)) + " " + keys.Contains(new Key(1)) + " " + As<ICollection<Key>>(keys).Contains(new Key(3)));
        IList<Plain> plains = As<IList<Plain>>(new[] { new Plain(1), new Plain(2) });
        Console.WriteLine(plains.IndexOf(new Plain(2)) + " " + plains.Contains(new Plain(5)));

        Animal[] animals = new Dog[] { new Dog("rex"), new Dog("fido") };
        IList<Animal> al = As<IList<Animal>>(animals);
        Console.WriteLine(al[1] + " " + al.IndexOf(animals[0]) + " " + al.Count);
        al[0] = new Dog("max");
        Console.WriteLine(animals[0]);
        Animal[] target = new Animal[3];
        al.CopyTo(target, 1);
        Console.WriteLine(target[0] == null ? "null" : "set" + "," + target[1] + "," + target[2]);
    }
}
