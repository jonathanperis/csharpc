using System;
using System.Collections.Generic;

// A delegate to an instance method of a struct is bound to a boxed copy of the struct: later changes to
// the variable are not seen, and the delegate's calls change its own copy.
Counter counter = new Counter(10);
Func<int> next = counter.Next;
counter.Add(100);
Console.WriteLine($"variable {counter.Value}");
Console.WriteLine($"delegate {next()} {next()} {next()}");
Console.WriteLine($"variable {counter.Value}");
Console.WriteLine(next.Target!.GetType().Name);
Console.WriteLine(next.Method.Name);

// A readonly struct with several reference fields: the delegate must see its own fields, not the box header.
Registry registry = new Registry("first");
Action<string> register = registry.Register;
register("a");
register("b");
Console.WriteLine($"{registry.Names.Count} {string.Join(",", registry.Names)} {registry.Other.Count}");

// Through a class that holds the delegate, with a generic struct and arguments.
Holder holder = new Holder(new Pair<string, int>("k", 5).Combine);
Console.WriteLine(holder.Run(3, "x"));
Console.WriteLine(holder.Run(4, "y"));

// The struct's own `this`, and an overridden ToString.
Console.WriteLine(new Pair<int, int>(1, 2).Capture()());
Money money = new Money(1250);
Func<string> text = money.ToString;
money = new Money(0);
Console.WriteLine(text());

// A null receiver is not possible; a boxed struct converts the same way.
object boxed = new Counter(7);
Func<int> unboxedNext = ((Counter)boxed).Next;
Console.WriteLine($"{unboxedNext()} {unboxedNext()} {((Counter)boxed).Value}");

struct Counter
{
    private int _value;
    public Counter(int start) => _value = start;
    public int Value => _value;
    public void Add(int n) => _value += n;
    public int Next() => ++_value;
}

readonly struct Registry
{
    public readonly List<string> Other;
    public readonly List<string> Names;
    public Registry(string first) { Other = new List<string>(); Names = new List<string> { first }; }
    public void Register(string name) => Names.Add(name);
}

readonly struct Pair<TKey, TValue>(TKey key, TValue value)
{
    public string Combine(int n, string suffix) => $"{key}-{value}-{n}-{suffix}";
    public Func<string> Capture() => ToString;
    public override string ToString() => $"({key}, {value})";
}

readonly struct Money(int cents)
{
    public override string ToString() => $"{cents / 100}.{cents % 100:00}";
}

sealed class Holder(Func<int, string, string> run)
{
    public string Run(int n, string s) => run(n, s);
}
