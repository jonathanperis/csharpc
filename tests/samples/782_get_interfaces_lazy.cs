// Type.GetInterfaces() on types whose interfaces the program never uses otherwise (csharpc describes
// those without method tables and makes their Type objects on demand): names, generic shape,
// identity, assignability, their own interfaces, MakeGenericType and GetType by name.
using System;
using System.Collections.Generic;
using System.Linq;

interface IShape<T> { }
interface IArea : IComparable<IArea> { }
struct Square : IShape<double>, IArea, IEquatable<Square>
{
    public int CompareTo(IArea? other) => 0;
    public bool Equals(Square other) => true;
}

static class Program
{
    static void Describe(Type type)
    {
        var interfaces = type.GetInterfaces();
        Console.WriteLine($"{type.Name}: {interfaces.Length}");
        foreach (var itf in interfaces)
            Console.WriteLine($"  {itf.FullName} | {itf.Name} | {itf.Namespace} | {itf.IsGenericType} {itf.IsInterface} {itf.IsPublic} {itf.ContainsGenericParameters}"
                + (itf.IsGenericType ? $" | {itf.GetGenericTypeDefinition().Name} [{string.Join(",", itf.GetGenericArguments().Select(a => a.Name))}]" : "")
                + $" | {itf.IsAssignableFrom(type)} {type.IsAssignableTo(itf)} {itf.GetInterfaces().Length}");
    }

    static void Main()
    {
        // Not int itself: CoreLib implements internal interfaces the reference assemblies csharpc compiles against omit.
        Describe(typeof(string[]));
        Describe(typeof(Square));
        Describe(typeof(List<Square>));

        var a = typeof(int).GetInterfaces();
        var b = typeof(int).GetInterfaces();
        Console.WriteLine(a.Zip(b).All(p => ReferenceEquals(p.First, p.Second)));
        var comparable = a.First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComparable<>));
        Console.WriteLine($"{comparable == typeof(IComparable<int>)} {comparable.GetGenericArguments()[0] == typeof(int)}");
        var adder = a.First(i => i.Name == "IAdditionOperators`3");
        Console.WriteLine($"{adder} {adder.AssemblyQualifiedName}");
        Console.WriteLine(adder.GetGenericTypeDefinition().MakeGenericType(typeof(int), typeof(int), typeof(int)) == adder);
        Console.WriteLine(typeof(long).GetInterfaces().Any(i => i == adder));
        var list = typeof(string[]).GetInterfaces().First(i => i.Name == "IList`1");
        Console.WriteLine($"{list} {list.IsAssignableFrom(typeof(string[]))} {list.IsAssignableFrom(typeof(object[]))} {typeof(IEnumerable<object>).IsAssignableFrom(list)}");
        foreach (var inner in list.GetInterfaces()) Console.WriteLine($"  {inner}");
        Console.WriteLine(Type.GetType("System.Numerics.IAdditionOperators`3[System.Int32,System.Int32,System.Int32]") == adder);
    }
}
