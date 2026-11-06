// Attributes of source members in a nullable-annotated file. csc writes [Nullable]/[NullableContext] into the metadata
// of members that mention reference types, which csharpc doesn't reproduce: asking such a member for *all* its
// attributes is an explicit NotSupportedException there (the JIT lists them). What stays exact, and is checked here:
// queries for an attribute csc never synthesizes (the program's own, [Obsolete]), and the complete lists of members
// whose signatures have no reference types (nothing is synthesized for them).
#pragma warning disable CS0649, CS0169
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
class MyAttribute : Attribute
{
    public int Value;
    public MyAttribute() { }
    public MyAttribute(int value) { Value = value; }
}

class Annotated
{
    public string Name = "";
    public string? Maybe;
    [My(1)] public string Tagged = "";
    [My(2), My(3)] public List<string> Items = new();
    public int Count;
    [My(4)] public int Number;

    [My(5)] public string Title { get; set; } = "";
    [Obsolete("old")] public string? Subtitle { get; set; }
    public int Size { get; set; }

    [My(6)] public string Echo([My(7)] string text, string? other = null) => text;
    [Obsolete] public int Twice(int x) => x * 2;
    public void Generic<T>([My(8)] T value) where T : class { }
    public int Plain(int a, long b) => (int)(a + b);
}

static class Program
{
    static string Typed(ICustomAttributeProvider provider)
    {
        var mine = provider.GetCustomAttributes(typeof(MyAttribute), false).Cast<MyAttribute>().Select(a => a.Value).ToArray();
        return $"mine=[{string.Join(",", mine)}] defined={provider.IsDefined(typeof(MyAttribute), false)} obsolete={provider.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length}";
    }

    static void Main() => Run(typeof(Annotated));

    static void Run([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        foreach (var name in new[] { "Name", "Maybe", "Tagged", "Items", "Count", "Number" })
            Console.WriteLine($"field {name}: {Typed(type.GetField(name)!)}");
        foreach (var name in new[] { "Title", "Subtitle", "Size" })
        {
            var property = type.GetProperty(name)!;
            Console.WriteLine($"property {name}: {Typed(property)} get: {Typed(property.GetMethod!)}");
        }
        foreach (var name in new[] { "Echo", "Twice", "Plain" })
        {
            var method = type.GetMethod(name)!;
            Console.WriteLine($"method {name}: {Typed(method)}");
            foreach (var parameter in method.GetParameters()) Console.WriteLine($"  {parameter.Name}: {Typed(parameter)}");
        }
        Console.WriteLine($"Generic: {Typed(type.GetMethod("Generic")!)}");
        Console.WriteLine($"Obsolete message: {type.GetProperty("Subtitle")!.GetCustomAttribute<ObsoleteAttribute>()!.Message}");

        Console.WriteLine("-- complete lists of members without reference types");
        foreach (var name in new[] { "Count", "Number" })
            Console.WriteLine($"{name}: [{string.Join(" | ", type.GetField(name)!.GetCustomAttributes(false).Select(a => a.GetType().Name))}]");
        Console.WriteLine($"get_Size: [{string.Join(" | ", type.GetProperty("Size")!.GetMethod!.GetCustomAttributes(false).Select(a => a.GetType().Name))}]");
        var twice = type.GetMethod("Twice")!;
        Console.WriteLine($"Twice: [{string.Join(" | ", twice.GetCustomAttributes(false).Select(a => a.GetType().Name))}] x: [{string.Join(" | ", twice.GetParameters()[0].GetCustomAttributes(false).Select(a => a.GetType().Name))}]");
        var plain = type.GetMethod("Plain")!;
        Console.WriteLine($"Plain: [{string.Join(" | ", plain.GetCustomAttributes(false).Select(a => a.GetType().Name))}] data {plain.GetCustomAttributesData().Count}");
        foreach (var p in plain.GetParameters()) Console.WriteLine($"  {p.Name}: [{string.Join(" | ", p.GetCustomAttributes(false).Select(a => a.GetType().Name))}]");
    }
}
