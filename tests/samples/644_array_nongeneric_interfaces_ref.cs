// Non-generic interfaces (IList, ICollection, IEnumerable, IStructuralComparable/Equatable) on arrays of
// reference types: csharpc shares one implementation across all reference element types, except the
// IList setter and ICollection.CopyTo, which must still check the destination's element type.
using System;
using System.Collections;
using System.Runtime.CompilerServices;

class Animal { public string Name; public Animal(string n) { Name = n; } public override string ToString() => Name; }
class Dog : Animal { public Dog(string n) : base(n) { } }

class Program
{
    // Hides the static array type so every call goes through the interface tables.
    [MethodImpl(MethodImplOptions.NoInlining)] static T As<T>(object o) => (T)o;

    static void Main()
    {
        string[] words = { "b", "a", "c" };
        IList list = As<IList>(words);
        Console.WriteLine($"{list.Count} {list[1]} {list.Contains("c")} {list.IndexOf("c")} {list.IndexOf("zz")} {list.IsFixedSize} {list.IsReadOnly}");
        list[0] = "B";
        Console.WriteLine(string.Join(",", words));
        try { list[0] = 42; } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
        try { list.Add("x"); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }

        Animal[] animals = { new Dog("rex"), new Animal("cat") };
        IList al = As<IList>(animals);
        al[1] = new Dog("fido");
        Console.WriteLine(al[1] + " " + al.IndexOf(animals[0]));
        try { al[0] = "not an animal"; } catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        foreach (object o in As<IEnumerable>(words)) Console.Write(o + ";");
        Console.WriteLine();
        IEnumerator en = As<IEnumerable>(animals).GetEnumerator();
        while (en.MoveNext()) Console.Write(en.Current + ";");
        Console.WriteLine();

        object[] target = new object[5];
        As<ICollection>(words).CopyTo(target, 1);
        Console.WriteLine(string.Join("|", target));
        string[] sameType = new string[3];
        As<ICollection>(words).CopyTo(sameType, 0);
        Console.WriteLine(string.Join("|", sameType));
        try { As<ICollection>(words).CopyTo(new object[2], 0); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        string[] copy = { "B", "a", "c" };
        Console.WriteLine(StructuralComparisons.StructuralEqualityComparer.Equals(words, copy));
        Console.WriteLine(StructuralComparisons.StructuralComparer.Compare(words, new[] { "B", "a", "d" }) < 0);
        Console.WriteLine(As<IStructuralEquatable>(words).GetHashCode(StructuralComparisons.StructuralEqualityComparer)
            == As<IStructuralEquatable>(copy).GetHashCode(StructuralComparisons.StructuralEqualityComparer));

        list.Clear();
        Console.WriteLine(words[0] == null && words[2] == null);
    }
}
