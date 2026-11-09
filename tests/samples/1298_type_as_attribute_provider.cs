// A Type used as an ICustomAttributeProvider (what System.Text.Json's schema exporter does with the type it describes): the
// interface's members answer for the type as for any member, whether the provider is a Type or a MethodInfo.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
class TagAttribute : Attribute { public string Name; public TagAttribute(string name) { Name = name; } }

[Tag("type")]
class Tagged { [Tag("method")] public void Run() { } }

class Untagged { public void Run() { } }

static class Program
{
    static string Describe(ICustomAttributeProvider provider) =>
        $"defined={provider.IsDefined(typeof(TagAttribute), false)} all=[{string.Join(",", provider.GetCustomAttributes(false).OfType<TagAttribute>().Select(t => t.Name))}] typed={provider.GetCustomAttributes(typeof(TagAttribute), true).Length}";

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        Console.WriteLine($"{type.Name}: {Describe(type)}");
        Console.WriteLine($"{type.Name}.Run: {Describe(type.GetMethod("Run")!)}");
    }

    static void Main()
    {
        Show(typeof(Tagged));
        Show(typeof(Untagged));
    }
}
