// System.Reflection.NullabilityInfoContext over records and types with a primary constructor: the positional parameters, their
// properties (and the [AllowNull]-family adjustments on them), the explicit members, and an oblivious record. csc's choice of
// [NullableContext] for these types involves members it synthesizes; the answers must still be csc's.
#nullable enable
#pragma warning disable CS9113
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

public record Person(string Name, string? Nickname, int Age);

public record Library(string Title, List<string?> Shelves, string[]? Tags, Dictionary<string, List<string>?> Index);

public record WithBody(string Id)
{
    public string? Note { get; init; }
    public List<string> Items { get; init; } = new();
    public string Label = "";
    public WithBody(string id, string? extra) : this(id) { Label = extra ?? ""; }
}

public record struct Point(string Label, double X, string? Tag);

public class Service(string name, string? description, List<string?> parts)
{
    public string Name { get; } = name;
    public string? Description => description;
    public int Count => parts.Count;
}

public record Annotated([property: AllowNull] string Value, [property: MaybeNull] string Other);

#nullable disable
public record Oblivious(string Name, List<string> Items);
#nullable enable

static class Program
{
    static string Describe(NullabilityInfo info)
    {
        var sb = new StringBuilder();
        sb.Append(info.Type.Name).Append(" read=").Append(info.ReadState).Append(" write=").Append(info.WriteState);
        if (info.ElementType != null) sb.Append(" element=[").Append(Describe(info.ElementType)).Append(']');
        if (info.GenericTypeArguments.Length > 0)
        {
            sb.Append(" args=[");
            for (int i = 0; i < info.GenericTypeArguments.Length; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(Describe(info.GenericTypeArguments[i]));
            }
            sb.Append(']');
        }
        return sb.ToString();
    }

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var context = new NullabilityInfoContext();
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            foreach (var parameter in constructor.GetParameters())
                Console.WriteLine($"{type.Name} ctor({constructor.GetParameters().Length}).{parameter.Name}: {Describe(context.Create(parameter))}");
        var properties = new List<PropertyInfo>(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var property in properties)
            Console.WriteLine($"{type.Name}.{property.Name}: {Describe(context.Create(property))}");
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Console.WriteLine($"{type.Name}.{field.Name}: {Describe(context.Create(field))}");
    }

    static void Main()
    {
        Show(typeof(Person));
        Show(typeof(Library));
        Show(typeof(WithBody));
        Show(typeof(Point));
        Show(typeof(Service));
        Show(typeof(Annotated));
        Show(typeof(Oblivious));
    }
}
