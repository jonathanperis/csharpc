// MemberInfo members called on a System.Type: compilers reference MemberInfo's declaration
// (System.Type doesn't redeclare GetCustomAttributesData, GetCustomAttributes or IsDefined), and they
// must reach the Type implementation, as dependency injection's trimming validation does for the
// [DynamicallyAccessedMembers] attribute of an open generic's type parameter.
#nullable disable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.GenericParameter)]
sealed class TagAttribute : Attribute
{
    public TagAttribute(string name) { Name = name; }
    public string Name { get; }
}

[Tag("box")]
class Box<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)][Tag("t")] T> { }

class Plain { }

class Program
{
    static int Kinds(Type parameter)
    {
        foreach (CustomAttributeData data in parameter.GetCustomAttributesData())
            if (data.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute" && data.ConstructorArguments.Count == 1)
                return (int)data.ConstructorArguments[0].Value;
        return 0;
    }

    static void Main()
    {
        Type box = typeof(Box<>);
        Console.WriteLine(string.Join(",", box.GetCustomAttributesData().Select(d => d.AttributeType.Name)));
        Console.WriteLine(((TagAttribute)box.GetCustomAttributes(false).Single(a => a is TagAttribute)).Name);
        Console.WriteLine($"{box.IsDefined(typeof(TagAttribute), false)} {typeof(Plain).IsDefined(typeof(TagAttribute), false)}");
        Type parameter = box.GetGenericArguments()[0];
        Console.WriteLine(Kinds(parameter));
        Console.WriteLine(Kinds(typeof(Plain)));
        Console.WriteLine(string.Join(",", parameter.GetCustomAttributes(typeof(TagAttribute), false).Cast<TagAttribute>().Select(a => a.Name)));
        Console.WriteLine(typeof(Plain).GetCustomAttributesData().Count);
    }
}
