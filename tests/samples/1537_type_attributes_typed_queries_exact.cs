// #199: a type csc adds attributes to (readonly struct, record, nullable context) has an attribute list csharpc marks
// incomplete; the untyped queries report that explicitly, while typed queries for the program's own attributes and for
// attributes csc never synthesizes stay exact.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
sealed class TagAttribute(string name) : Attribute { public string Name { get; } = name; }

[Obsolete("demo")]
[Tag("point")]
readonly struct Point(int x, int y) { public int X { get; } = x; public int Y { get; } = y; }

[Tag("record"), Tag("second")]
record Person(string Name);

#nullable enable
[Tag("nullable")]
class Holder { public string? Value; public string Name = ""; }
#nullable restore

static class Program
{
    static string Names(object[] attributes) => string.Join(",", attributes.Select(a => a is TagAttribute t ? "Tag:" + t.Name : a.GetType().Name));

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type t)
    {
        Console.WriteLine($"{t.Name}: tags={Names(t.GetCustomAttributes(typeof(TagAttribute), false))} " +
            $"obsolete={Names(t.GetCustomAttributes(typeof(ObsoleteAttribute), false))} " +
            $"defined={t.IsDefined(typeof(TagAttribute), false)} serializable={t.IsDefined(typeof(SerializableAttribute), false)} " +
            $"single={(Attribute.GetCustomAttribute(t, typeof(ObsoleteAttribute)) as ObsoleteAttribute)?.Message ?? "none"}");
    }

    static void Main()
    {
        Show(typeof(Point));
        Show(typeof(Person));
        Show(typeof(Holder));
    }
}
