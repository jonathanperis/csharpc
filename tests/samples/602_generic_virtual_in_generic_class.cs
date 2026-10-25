// A virtual generic method declared in a generic class (Microsoft.Extensions.Options'
// OptionsBuilder<TOptions>.Configure<TDep1, ...>) called on an instance of that class and of
// non-generic classes deriving from it, with and without an override.
using System;
using System.Collections.Generic;

class Builder<T>
{
    public readonly List<string> Log = new();
    public virtual Builder<T> Configure<TDep>(TDep dependency) { Log.Add($"{typeof(T).Name}<-{typeof(TDep).Name} {dependency}"); return this; }
    public virtual string Describe<TA, TB>(TA a, TB b) => $"base {a} {b}";
}

class Overriding : Builder<int>
{
    public override Builder<int> Configure<TDep>(TDep dependency) { Log.Add("overridden " + dependency); return this; }
}

class Inheriting : Builder<string> { }

class Program
{
    static void Run<T>(Builder<T> builder)
    {
        builder.Configure(42).Configure("text");
        Console.WriteLine(string.Join(" | ", builder.Log) + " / " + builder.Describe(1.5, 'c'));
    }

    static void Main()
    {
        Run(new Builder<int>());
        Run(new Builder<string>());
        Run(new Overriding());
        Run(new Inheriting());
        Builder<int> b = new Overriding();
        Console.WriteLine(b.Configure(7).Log[0]);
    }
}
