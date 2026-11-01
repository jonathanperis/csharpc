// Activator.CreateInstance(Type) called repeatedly on the same types (RazorSlices creates every slice this
// way when code can't be generated): a new instance and a constructor run each time, a throwing constructor
// wrapped in TargetInvocationException every time, and the errors for types without a usable constructor
// reported on every call, not only the first.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

class Counted
{
    public static int Runs;
    public readonly int Id;
    public Counted() { Id = ++Runs; }
}

class Throws
{
    public static int Attempts;
    public Throws() { Attempts++; throw new InvalidOperationException("attempt " + Attempts); }
}

class NoDefault { public NoDefault(int x) { } }
abstract class Abstract { }
interface IThing { }

static class Program
{
    static object Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type) =>
        Activator.CreateInstance(type)!;

    static void Try(string label, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        try { Console.WriteLine(label + ": " + Activator.CreateInstance(type)); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message + (e.InnerException is { } inner ? " / " + inner.Message : "")); }
    }

    static void Main()
    {
        object first = Create(typeof(Counted));
        object second = Create(typeof(Counted));
        Console.WriteLine(((Counted)first).Id + " " + ((Counted)second).Id + " " + ReferenceEquals(first, second));
        int sum = 0;
        for (int i = 0; i < 1000; i++) sum += ((Counted)Create(typeof(Counted))).Id;
        Console.WriteLine(sum + " " + Counted.Runs);
        for (int i = 0; i < 3; i++)
        {
            Try("throws", typeof(Throws));
            Try("no default", typeof(NoDefault));
            Try("abstract", typeof(Abstract));
            Try("interface", typeof(IThing));
        }
        Console.WriteLine(Activator.CreateInstance(typeof(System.Text.StringBuilder)) is System.Text.StringBuilder);
        Console.WriteLine(Activator.CreateInstance(typeof(object)).GetType());
    }
}
