// A generic class over a struct type parameter and a reference one, calling through an interface over the reference one on the struct
// (`constrained.` in IL): Roslyn's OverloadResolution.RemoveLowerPriorityMembers<TMemberResolution, TMember> does this. With shared
// generic code the class's canonical form keeps the struct and shares the reference argument, so the call must still find the struct's
// own implementation of the exact interface.
using System;
using System.Collections.Generic;
using System.Linq;

interface IWithPriority<TMember> { TMember Member { get; } int Priority { get; } }

abstract class Symbol { public abstract string Name { get; } }
sealed class MethodSym(string name) : Symbol { public override string Name => name; }
sealed class PropertySym(string name) : Symbol { public override string Name => "prop " + name; }

struct MethodResult(MethodSym m, int p) : IWithPriority<MethodSym> { public MethodSym Member => m; public int Priority => p; }
// Explicit implementations (as Roslyn's BinaryOperatorAnalysisResult has), next to public members of the same names that are not them.
struct PropertyResult(PropertySym m, int p) : IWithPriority<PropertySym>
{
    public string Member => "not the interface's";
    public int Priority => -1;
    PropertySym IWithPriority<PropertySym>.Member => m;
    int IWithPriority<PropertySym>.Priority => p;
}

class Resolver<TResult, TMember> where TResult : IWithPriority<TMember> where TMember : Symbol
{
    // Source body: a direct constrained call.
    public TMember Best(List<TResult> results)
    {
        var best = results[0];
        for (int i = 1; i < results.Count; i++) if (results[i].Priority > best.Priority) best = results[i];
        return best.Member;
    }

    // A lambda: its closure class is generic over both parameters (what the IL front-end shares).
    public string Names(List<TResult> results) => string.Join(",", results.Where(r => r.Priority > 0).Select(r => r.Member.Name));
}

static class Program
{
    static void Main()
    {
        var methods = new List<MethodResult> { new(new MethodSym("a"), 1), new(new MethodSym("b"), 3), new(new MethodSym("c"), 0) };
        var properties = new List<PropertyResult> { new(new PropertySym("x"), 2), new(new PropertySym("y"), 0) };
        var byMethod = new Resolver<MethodResult, MethodSym>();
        var byProperty = new Resolver<PropertyResult, PropertySym>();
        Console.WriteLine(byMethod.Best(methods).Name + " " + byProperty.Best(properties).Name);
        Console.WriteLine(byMethod.Names(methods) + " | " + byProperty.Names(properties));
    }
}
