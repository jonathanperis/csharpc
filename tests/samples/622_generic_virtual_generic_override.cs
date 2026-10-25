// A generic virtual method overridden in generic classes (as FrozenDictionary's alternate lookups are):
// calls dispatch to the receiver's instantiation, including instantiations created after the call site.
#nullable disable
using System;
using System.Collections.Generic;

abstract class Store
{
    public abstract string Describe<T>(T probe);
    public virtual string Tag<T>() => "base " + typeof(T).Name;
}

class Cell<TValue> : Store
{
    private readonly TValue _value;
    public Cell(TValue value) { _value = value; }
    public override string Describe<T>(T probe) => $"Cell<{typeof(TValue).Name}>({_value}) probed with {typeof(T).Name} {probe}";
    public override string Tag<T>() => "cell " + typeof(TValue).Name + "/" + typeof(T).Name;
}

class Pair<TA, TB> : Cell<TA>
{
    private readonly TB _second;
    public Pair(TA first, TB second) : base(first) { _second = second; }
    public override string Describe<T>(T probe) => $"Pair<{typeof(TA).Name},{typeof(TB).Name}>({_second}) " + base.Describe(probe);
}

sealed class Plain : Store
{
    public override string Describe<T>(T probe) => "plain " + probe;
}

class Program
{
    static void Show(Store store)
    {
        Console.WriteLine(store.Describe(42));
        Console.WriteLine(store.Describe("text"));
        Console.WriteLine(store.Tag<double>());
    }

    static Store Later() => new Pair<long, List<int>>(7L, null);

    static void Main()
    {
        Show(new Cell<string>("s"));
        Show(new Plain());
        Show(new Pair<int, string>(1, "two"));
        Show(Later());
        Store none = null;
        try { none.Describe(1); } catch (NullReferenceException) { Console.WriteLine("null receiver"); }
    }
}
