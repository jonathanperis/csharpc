// Generic classes whose instance field and property initializers allocate over T: a constructor runs them, so they count as its
// body (shared generic code must see the `new List<T>()` an initializer makes).
using System;
using System.Collections.Generic;

class Holder<T>
{
    private readonly List<T> _items = new();
    private T[] _slots = new T[2];
    public Dictionary<string, T> ByName { get; } = new();
    public int Created = 1;

    public Holder() { }
    public Holder(T first) : this() { Add(first); }

    public void Add(T item)
    {
        _items.Add(item);
        if (_items.Count <= _slots.Length) _slots[_items.Count - 1] = item;
        ByName[item!.ToString()!] = item;
    }

    public string Describe() => _items.Count + " items, first slot " + _slots[0] + ", names " + string.Join(",", ByName.Keys) + ", " + Created;
}

sealed class Box(string name)
{
    public override string ToString() => "box:" + name;
}

static class Program
{
    static void Main()
    {
        var words = new Holder<string>("one");
        words.Add("two");
        Console.WriteLine(words.Describe());
        var boxes = new Holder<Box>();
        boxes.Add(new Box("a"));
        boxes.Add(new Box("b"));
        boxes.Add(new Box("c"));
        Console.WriteLine(boxes.Describe());
        var objects = new Holder<object>(42);
        Console.WriteLine(objects.Describe());
    }
}
