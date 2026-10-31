// dynamic operations in the shapes csc compiles them to (docs/adr/0019-dynamic.md, "From IL"): simple-name calls
// through `this` (classes and structs), static calls with dynamic arguments, constants of every kind (decimal
// literals arrive as constructor calls), named and by-reference arguments, constructor calls of structs, events,
// delegates, compound assignments and increments, explicit type arguments, nested sites as operands of others,
// lambdas and generic methods.
using System;
using System.Collections.Generic;
using Microsoft.CSharp.RuntimeBinder;

enum Mode { Off, On, Auto }

struct Meter
{
    public int Total;
    public void Add(int amount) => Total += amount;
    public void Add(string label) => Total += label.Length;
    public void Feed(dynamic value) => Add(value);
}

struct Pair
{
    public int A, B;
    public Pair(int a, int b) { A = a; B = b; }
    public override string ToString() => $"Pair({A}, {B})";
}

class Light
{
    public Mode Mode;
    public event EventHandler? Changed;
    public void Set(Mode mode) { Mode = mode; Changed?.Invoke(this, EventArgs.Empty); }
    public string Describe(int level = 1, string unit = "lx") => $"{Mode} {level}{unit}";
    public int Sum(params int[] values) { var s = 0; foreach (var v in values) s += v; return s; }
    public bool TryRead(string text, out int value, ref int calls) { calls++; return int.TryParse(text, out value); }
    public T Echo<T>(T value) => value;
}

class Program
{
    int _bias = 100;

    static string Kind(int x) => "int " + x;
    static string Kind(long x) => "long " + x;
    static string Kind(decimal x) => "decimal " + x;
    static string Kind(string s) => "string " + s;
    static string Kind(Mode m) => "mode " + m;
    static string Kind(char c) => "char " + c;
    static string Kind(object o) => "object " + o;

    int Biased(int x) => x + _bias;
    string Biased(string s) => s + _bias;

    void Instance(dynamic d)
    {
        Console.WriteLine(Biased(d));
        Func<object> later = () => Biased(d) + Biased(d);
        Console.WriteLine(later());
    }

    static T Twice<T>(dynamic value) => (T)(value + value);

    static void T(string label, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        dynamic seven = 7, text = "abc", big = 3000000000L, money = 1.5m, mode = Mode.On, ch = 'q';
        foreach (var v in new dynamic[] { seven, text, big, money, mode, ch, 2.5 }) Console.WriteLine(Kind(v));
        // Constants as the binder sees them: an int constant converts to a narrower parameter.
        dynamic light = new Light();
        Console.WriteLine(light.Describe());
        Console.WriteLine(light.Describe(3));
        Console.WriteLine(light.Describe(unit: "cd", level: seven));
        Console.WriteLine(light.Sum(1, 2, seven));
        Console.WriteLine(light.Sum());
        Console.WriteLine(seven * 2.5m);
        Console.WriteLine(money + 1);
        Console.WriteLine(mode == Mode.On);
        Console.WriteLine(mode | Mode.Auto);
        Console.WriteLine(ch + 1);
        Console.WriteLine(light.Echo<long>(seven));
        Console.WriteLine(light.Echo(text));
        Console.WriteLine(Twice<int>(seven) + Twice<string>(text));

        // By-reference arguments.
        int calls = 0;
        Console.WriteLine(light.TryRead("42", out int read, ref calls) + " " + read + " " + calls);
        Console.WriteLine(light.TryRead(text, out read, ref calls) + " " + read + " " + calls);

        // Members of the context: private instance overloads through `this`, inside a lambda too.
        new Program().Instance(seven);
        new Program().Instance(text);

        // Struct receivers: a simple-name call through a struct's `this`, and a struct variable.
        var meter = new Meter();
        meter.Feed(5);
        meter.Feed("four");
        meter.Add(seven);
        Console.WriteLine(meter.Total);
        dynamic pair = new Pair(seven, 2);
        Console.WriteLine(pair);
        object boxed = new Pair(1, seven);
        Console.WriteLine(boxed);
        pair.A += 10;
        pair.B++;
        Console.WriteLine(pair.A + pair.B);

        // Events and delegates.
        var count = 0;
        light.Changed += (EventHandler)((s, e) => count++);
        light.Set(Mode.Auto);
        light.Set(mode);
        Console.WriteLine($"{count} {light.Mode}");
        dynamic square = (Func<int, int>)(x => x * x);
        Console.WriteLine(square(seven));
        Func<int, string> format = x => "#" + x;
        Console.WriteLine(format(seven));

        // Compound assignment to static variables, increments, checked arithmetic.
        int total = 1;
        total += seven;
        total *= seven;
        long wide = 1;
        wide += big;
        Console.WriteLine($"{total} {wide}");
        dynamic counter = 0;
        counter++;
        ++counter;
        counter += 0.5;
        Console.WriteLine(counter);
        dynamic max = int.MaxValue;
        T("checked", () => Console.WriteLine(checked(max + 1)));
        Console.WriteLine(unchecked(max + 1));
        var list = new List<int> { seven, 3 };
        dynamic items = list;
        items[0] += 1;
        items[1]++;
        Console.WriteLine(string.Join(",", list));

        // Binder errors from code compiled from IL.
        T("no member", () => light.Missing());
        T("bad arg", () => light.Describe("x"));
        T("no ctor", () => new Pair(text, 1));
        T("conversion", () => { Mode m = text; });
        T("operator", () => Console.WriteLine(text - seven));
        T("null", () => ((dynamic)null!).Foo());
    }
}
