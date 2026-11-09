// The attributes csc adds for `required` members, as reflection lists them: [RequiredMember] on the type and on each required member, and on every
// constructor that does not say [SetsRequiredMembers] the [Obsolete] and [CompilerFeatureRequired] that keep old compilers away.
#nullable enable
#pragma warning disable CS0649
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

public class Req
{
    public required string Name { get; set; }
    public required int Count;
    public int Plain { get; set; }
}

public class Req2
{
    public Req2(int x) { }
    [SetsRequiredMembers] public Req2() { Name = ""; }
    public required string Name { get; set; }
}

public class NoRequired
{
    public string Name { get; set; } = "";
}

static class Program
{
    static string Names(System.Collections.Generic.IEnumerable<CustomAttributeData> data) =>
        string.Join(",", data.Select(a => a.AttributeType.Name + (a.ConstructorArguments.Count > 0 ? "(" + string.Join(";", a.ConstructorArguments.Select(x => x.Value)) + ")" : "")));

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        Console.WriteLine($"{type.Name}: [{Names(type.GetCustomAttributesData())}]");
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(c => c.GetParameters().Length))
            Console.WriteLine($"  ctor({constructor.GetParameters().Length}): [{Names(constructor.GetCustomAttributesData())}]");
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(p => p.Name, StringComparer.Ordinal))
            Console.WriteLine($"  {property.Name}: [{Names(property.GetCustomAttributesData())}] required={property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false)}");
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Console.WriteLine($"  field {field.Name}: [{Names(field.GetCustomAttributesData())}]");
    }

    static void Main()
    {
        Show(typeof(Req));
        Show(typeof(Req2));
        Show(typeof(NoRequired));
    }
}
