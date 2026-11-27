// Type.GetCustomAttributes(inherit: true) over a class hierarchy: the base classes' attributes whose class is
// [AttributeUsage(Inherited = true)], a single-use attribute of a base hidden by the derived class's own, repeatable ones
// accumulated, the non-inheritable ones left out, and the same answers for typed queries and IsDefined.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
class OnceAttribute : Attribute { public string Name; public OnceAttribute(string name) { Name = name; } }

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
class ManyAttribute : Attribute { public string Name; public ManyAttribute(string name) { Name = name; } }

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
class LocalAttribute : Attribute { public string Name; public LocalAttribute(string name) { Name = name; } }

[Once("base"), Many("base-1"), Many("base-2"), Local("base")]
class Base { }

[Many("mid")]
class Mid : Base { }

[Once("derived"), Local("derived")]
class Derived : Mid { }

class Plain { }

[Many("leaf")]
class Leaf : Plain { }

static class Program
{
    static string Names(object[] attributes) =>
        string.Join(" ", attributes.Select(a => a switch
        {
            OnceAttribute o => "Once:" + o.Name,
            ManyAttribute m => "Many:" + m.Name,
            LocalAttribute l => "Local:" + l.Name,
            _ => a.GetType().Name,
        }));

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        Console.WriteLine($"{type.Name} own      : {Names(type.GetCustomAttributes(false))}");
        Console.WriteLine($"{type.Name} inherited: {Names(type.GetCustomAttributes(true))}");
        Console.WriteLine($"{type.Name} Many     : {Names(type.GetCustomAttributes(typeof(ManyAttribute), true))} | {Names(type.GetCustomAttributes(typeof(ManyAttribute), false))}");
        Console.WriteLine($"{type.Name} defined  : Once={type.IsDefined(typeof(OnceAttribute), true)}/{type.IsDefined(typeof(OnceAttribute), false)} Local={type.IsDefined(typeof(LocalAttribute), true)}/{type.IsDefined(typeof(LocalAttribute), false)}");
    }

    static void Main()
    {
        Show(typeof(Base));
        Show(typeof(Mid));
        Show(typeof(Derived));
        Show(typeof(Plain));
        Show(typeof(Leaf));
    }
}
