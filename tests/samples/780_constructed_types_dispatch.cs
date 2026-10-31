// Virtual, interface and generic virtual dispatch when only some classes of a hierarchy are ever
// constructed (csharpc keeps the slots of constructed classes only), objects created without a
// constructor call csharpc sees, and arrays / collections reached through invariant interfaces.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

abstract class Visitor
{
    public virtual string Visit<T>(T value) => $"base {value}";
    public abstract string Name { get; }
    public override string ToString() => "visitor " + Name;
}

class Printer : Visitor
{
    public override string Visit<T>(T value) => $"print {typeof(T).Name} {value}";
    public override string Name => "printer";
}

class Counter : Visitor
{
    public int Count;
    public override string Visit<T>(T value) { Count++; return $"count {Count}"; }
    public override string Name => "counter";
}

class Inheriting : Printer
{
    public override string Name => "inheriting";
}

// Never constructed with `new`: only created by RuntimeHelpers.GetUninitializedObject.
class Ghost : Visitor
{
    public string? Tag;
    public override string Visit<T>(T value) => $"ghost {value} {Tag ?? "null"}";
    public override string Name => "ghost";
}

class Generic<TItem> : Visitor
{
    public override string Visit<T>(T value) => $"generic<{typeof(TItem).Name}> {typeof(T).Name}";
    public override string Name => "generic";
}

class Bag : ICollection<int>
{
    private readonly List<int> _items = new();
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(int item) => _items.Add(item * 10);
    public void Clear() => _items.Clear();
    public bool Contains(int item) => _items.Contains(item);
    public void CopyTo(int[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(int item) => _items.Remove(item);
    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class Program
{
    static string Run(Visitor v) => v.Visit(42) + " | " + v.Visit("s") + " | " + v;

    static void Fill<T>(ICollection<T> collection, T item) { collection.Add(item); Console.WriteLine($"{collection.GetType().Name} {collection.Count}"); }

    static void Main()
    {
        Console.WriteLine(Run(new Printer()));
        Console.WriteLine(Run(new Counter()));
        Console.WriteLine(Run(new Inheriting()));
        Console.WriteLine(Run(new Generic<DateTime>()));
        var ghost = (Ghost)RuntimeHelpers.GetUninitializedObject(typeof(Ghost));
        Console.WriteLine(Run(ghost));
        ghost.Tag = "set";
        Console.WriteLine(ghost.Visit(1.5));

        // Invariant interfaces: each instantiation dispatched separately.
        Fill(new List<string>(), "a");
        Fill(new Bag(), 7);
        Fill(new HashSet<long>(), 3L);
        ICollection<KeyValuePair<string, int>> pairs = new Dictionary<string, int>();
        pairs.Add(new KeyValuePair<string, int>("k", 1));
        var copied = new KeyValuePair<string, int>[1];
        pairs.CopyTo(copied, 0);
        Console.WriteLine($"{copied[0].Key}={copied[0].Value} {pairs.Count}");

        // Arrays through interfaces, as object first.
        object numbers = new[] { 3, 1, 2 };
        var list = (IList<int>)numbers;
        list[0] = 9;
        Console.WriteLine($"{list.Count} {list[0]} {list.IndexOf(2)} {((ICollection<int>)numbers).Contains(1)}");
        var untyped = (IList)numbers;
        Console.WriteLine($"{untyped[1]} {untyped.Count} {untyped.Contains(9)}");
        var target = new int[4];
        ((ICollection)numbers).CopyTo(target, 1);
        Console.WriteLine(string.Join(",", target));
        object names = new[] { "x", "y" };
        foreach (var o in (IEnumerable<object>)names) Console.Write(o);
        Console.WriteLine(((IReadOnlyList<string>)names)[1]);
        try { ((IList)names)[0] = 5; } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
        IEnumerable<KeyValuePair<int, int>> entries = new KeyValuePair<int, int>[] { new(1, 2) };
        foreach (var e in entries) Console.WriteLine($"{e.Key}->{e.Value}");
    }
}
