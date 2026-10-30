// C# 15 union types: conversions into a union, the synthesized Value, and patterns on a union value —
// contents for type/constant/typed-property patterns, the instance for var/discard/untyped property
// patterns — plus hand-written unions (non-boxing HasValue/TryGetValue, which the compiler prefers for
// null and type tests) and class unions.
#nullable enable
using System;
using System.Runtime.CompilerServices;

public record Cat(string Name, int Lives = 9);
public record Dog(string Name, string Breed);
public record Bird(string Name);

public union Pet(Cat, Dog, Bird)
{
    public string Name => this switch { Cat c => c.Name, Dog d => d.Name, Bird b => b.Name, null => "(none)" };
}

public union IntOrString(int, string);
public record None;
public record Some<T>(T Item);
public union Option<T>(None, Some<T>);

[Union]
public struct Counted : IUnion
{
    public static int ValueCalls, HasValueCalls, TryIntCalls, TryStringCalls;
    private readonly int _i; private readonly string? _s; private readonly byte _tag;
    public Counted(int? value) { if (value.HasValue) { _i = value.Value; _tag = 1; } }
    public Counted(string? value) { if (value is not null) { _s = value; _tag = 2; } }
    public object? Value { get { ValueCalls++; return _tag switch { 1 => _i, 2 => _s, _ => null }; } }
    public bool HasValue { get { HasValueCalls++; return _tag != 0; } }
    public bool TryGetValue(out int value) { TryIntCalls++; value = _i; return _tag == 1; }
    public bool TryGetValue(out string value) { TryStringCalls++; value = _s!; return _tag == 2; }
}

[Union]
public class Result : IUnion
{
    private readonly object? _value;
    public Result(int value) { _value = value; }
    public Result(string? value) { _value = value; }
    public object? Value => _value;
}

class Program
{
    static string Describe(Pet pet) => pet switch
    {
        Cat { Lives: > 5 } c => "lucky cat " + c.Name,
        Cat c => "cat " + c.Name,
        Dog { Breed: "Labrador" } d => "lab " + d.Name,
        Dog d => "dog " + d.Name,
        Bird b => "bird " + b.Name,
        null => "empty",
    };

    static string Kind(IntOrString x) => x switch { int i when i > 10 => "big int " + i, int i => "int " + i, string s => "string " + s, null => "null" };

    static void Main()
    {
        Pet[] pets = { new Cat("tom"), new Cat("old", 2), new Dog("rex", "Labrador"), new Dog("fido", "Poodle"), new Bird("tweety"), default };
        foreach (var p in pets) Console.WriteLine($"{Describe(p)} | {p.Name} | {p.Value?.GetType().Name ?? "null"}");

        Pet rex = new Dog("Rex", "Poodle");
        Console.WriteLine(rex is { Name: "Rex" });          // untyped property pattern: the union's Name
        Console.WriteLine(rex is not null);                  // contents
        Console.WriteLine(rex is var whole && whole.Value is Dog);
        Console.WriteLine(default(Pet) is { });              // untyped: the instance, always matches a struct union
        Console.WriteLine(default(Pet) is null);

        Console.WriteLine(Kind(5) + "; " + Kind(42) + "; " + Kind("hi") + "; " + Kind(default));
        IntOrString n = 7;
        Console.WriteLine(n is 7);
        Console.WriteLine(n is > 3 and < 10);
        Console.WriteLine(n.Value is int);
        object boxed = 5, text = "5";   // relational patterns on a reference: type test, then compare
        Console.WriteLine($"{boxed is > 3} {boxed is > 9} {text is > 3} {boxed is >= 5 and <= 5} {boxed is not < 0}");

        Option<int> some = new Some<int>(3), none = new None();
        Console.WriteLine(some switch { Some<int> s => "some " + s.Item, None => "none", null => "null" });
        Console.WriteLine(none switch { Some<int> s => "some " + s.Item, None => "none", null => "null" });

        Pet? maybe = null;
        Console.WriteLine(maybe is null);
        maybe = new Cat("x");
        Console.WriteLine(maybe is Cat { Name: "x" });
        Pet? emptyInside = default(Pet);
        Console.WriteLine(emptyInside is null);

        Counted c1 = 5, c2 = "s", c3 = default;
        Console.WriteLine($"{c1 is int a && a == 5} {c2 is string t && t == "s"} {c3 is null} {c1 is null} {c2 is int}");
        Console.WriteLine($"Value={Counted.ValueCalls} HasValue={Counted.HasValueCalls} TryInt={Counted.TryIntCalls} TryString={Counted.TryStringCalls}");
        Console.WriteLine(c1 is 5);
        Console.WriteLine($"Value={Counted.ValueCalls}");

        Result? r0 = null;
        Result r1 = 3, r2 = (string?)null;
        Console.WriteLine($"{r0 is null} {r1 is null} {r2 is null} {r1 is int k && k == 3} {r2 is string}");

        try { Console.WriteLine(Describe2(default)); }
        catch (SwitchExpressionException e) { Console.WriteLine(e.GetType().Name); }
    }

    static string Describe2(Pet pet) => pet switch { Cat => "cat", Dog => "dog", Bird => "bird" };
}
