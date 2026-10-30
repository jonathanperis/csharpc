// Type.GetInterfaces lists every implemented interface in CoreCLR's order (not only those the program
// uses), and Activator.CreateInstance(Type) reports .NET's exceptions and messages.
#nullable disable
#pragma warning disable IL2067, IL2072, IL2075
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

interface IShape { }
interface INamedShape : IShape, IComparable<INamedShape> { }
class Base : IDisposable { public void Dispose() { } }
class Square : Base, INamedShape, ICloneable
{
    public int CompareTo(INamedShape other) => 0;
    public object Clone() => this;
}
abstract class Abstract { }
class NoDefault { public NoDefault(int x) { } }
class Thrower { public Thrower() { throw new InvalidOperationException("ctor failed"); } }
class Plain { public override string ToString() => "Plain()"; }

class Program
{
    static string Names(Type t) => string.Join(",", Array.ConvertAll(t.GetInterfaces(), i => i.Name));

    static Type K<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() => typeof(T);

    static void Create(Type t)
    {
        try { Console.WriteLine(Activator.CreateInstance(t)); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message + (e.InnerException != null ? " / " + e.InnerException.Message : "")); }
    }

    static void Main()
    {
        Console.WriteLine(Names(typeof(string)));
        Console.WriteLine(Names(typeof(List<int>)));
        Console.WriteLine(Names(typeof(Dictionary<string, int>)));
        Console.WriteLine(Names(typeof(int[])));
        Console.WriteLine(Names(typeof(Square)));
        Console.WriteLine(Names(typeof(INamedShape)));
        Console.WriteLine(Names(typeof(Base)));
        Console.WriteLine(Names(typeof(object)).Length);

        Create(K<Plain>());
        Create(K<NoDefault>());
        Create(K<Abstract>());
        Create(K<IShape>());
        Create(K<Thrower>());
    }
}
