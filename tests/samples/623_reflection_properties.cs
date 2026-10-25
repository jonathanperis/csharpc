// PropertyInfo over the kept metadata: GetProperties/GetProperty (binding flags, return type, index
// types), GetMethod/SetMethod, GetValue/SetValue, CanRead/CanWrite, indexers, attributes, inherited and
// static properties; typeof(X).GetProperty on a known type keeps X's properties as ILLink would.
#nullable disable
#pragma warning disable IL2026, IL2067, IL2070, IL2072, IL2075, IL2080, IL3050
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

[AttributeUsage(AttributeTargets.Property)]
sealed class LabelAttribute : Attribute { public LabelAttribute(string text) { Text = text; } public string Text { get; } }

class Base
{
    public virtual string Kind => "base";
    public int Inherited { get; set; } = 3;
}

class Person : Base
{
    private readonly int[] _scores = { 10, 20, 30 };
    [Label("full name")] public string Name { get; set; } = "Ada";
    public int Age { get; private set; } = 36;
    public override string Kind => "person";
    public static string Species { get; set; } = "human";
    internal string Secret { get; set; } = "s";
    public int this[int i] { get => _scores[i]; set => _scores[i] = value; }
    public string WriteOnly { set { } }
}

// Asked for by name first (typeof(Gadget).GetProperty("Size")), then listed in full by Keep: the full list
// keeps RuntimeType's order.
class Gadget
{
    public int Size { get; set; } = 5;
    public string Label { get; set; } = "g";
    public int Weight => 2;
    public int Twice(int x) => 2 * x;
    public int Thrice(int x) => 3 * x;
}

class Program
{
    static PropertyInfo[] Keep([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type t, BindingFlags flags) =>
        t.GetProperties(flags);

    static void Main()
    {
        var instance = BindingFlags.Public | BindingFlags.Instance;
        foreach (var p in Keep(typeof(Person), instance).OrderBy(p => p.Name))
            Console.WriteLine($"{p.Name} {p.PropertyType.Name} {p.CanRead} {p.CanWrite} {p.DeclaringType.Name} {p.GetIndexParameters().Length} {p.MemberType}");
        Console.WriteLine(string.Join(",", Keep(typeof(Person), BindingFlags.NonPublic | BindingFlags.Instance).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", Keep(typeof(Person), BindingFlags.Public | BindingFlags.Static).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", Keep(typeof(Person), instance | BindingFlags.DeclaredOnly).Select(p => p.Name).OrderBy(n => n)));

        var person = new Person();
        var name = typeof(Person).GetProperty("Name");
        Console.WriteLine($"{name.GetValue(person)} {name.GetMethod.Name} {name.SetMethod.Name} {name.GetCustomAttribute<LabelAttribute>().Text} {name}");
        name.SetValue(person, "Grace");
        Console.WriteLine(person.Name);
        var age = typeof(Person).GetProperty("Age");
        Console.WriteLine($"{age.GetValue(person)} {age.GetSetMethod() == null} {age.GetSetMethod(true) != null} {age.CanWrite}");
        age.SetValue(person, 37);
        Console.WriteLine(person.Age);
        var item = typeof(Person).GetProperty("Item", typeof(int), new[] { typeof(int) });
        Console.WriteLine($"{item.GetValue(person, new object[] { 1 })} {item.GetIndexParameters()[0].ParameterType.Name}");
        item.SetValue(person, 99, new object[] { 2 });
        Console.WriteLine(person[2]);
        Console.WriteLine($"{typeof(Person).GetProperty("Kind").GetValue(person)} {typeof(Person).GetProperty("Species").GetValue(null)} {typeof(Person).GetProperty("Missing") == null}");
        Console.WriteLine($"{typeof(Person).GetProperty("name", BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance)?.Name}");
        var writeOnly = typeof(Person).GetProperty("WriteOnly");
        try { writeOnly.GetValue(person); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { typeof(Person).GetProperty("Kind").SetValue(person, "x"); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        Console.WriteLine($"{typeof(Gadget).GetProperty("Size").GetValue(new Gadget())} {typeof(Gadget).GetMethod("Twice").Invoke(new Gadget(), new object[] { 21 })}");
        Console.WriteLine(string.Join(",", Keep(typeof(Gadget), BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)));
        Console.WriteLine(typeof(Person).GetProperty("Inherited", BindingFlags.Public | BindingFlags.Instance, null, typeof(int), Type.EmptyTypes, null).GetValue(person));
    }
}
