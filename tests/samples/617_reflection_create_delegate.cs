// MethodInfo.CreateDelegate over a method found by reflection (as UseMiddleware binds Invoke to a
// RequestDelegate), its signature checks, and GetCustomAttributes(typeof(A)) for an attribute no member has.
#nullable disable
#pragma warning disable IL2026, IL2067, IL2070, IL2072, IL2075, IL2080, IL3050
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;

delegate Task Handler(Context context);
delegate object Other(string s);

class Context { public string Path = "/"; }

[AttributeUsage(AttributeTargets.All)]
sealed class NeverUsedAttribute : Attribute { }

class Middleware
{
    private readonly string _name;
    public Middleware(string name) { _name = name; }
    public Task Invoke(Context context) { Console.WriteLine($"{_name} handles {context.Path}"); return Task.CompletedTask; }
    public string Describe(object value) => _name + ":" + value;
}

class Program
{
    static MethodInfo Find([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type, string name) => type.GetMethod(name);

    static async Task Main()
    {
        var invoke = Find(typeof(Middleware), "Invoke");
        var handler = (Handler)invoke.CreateDelegate(typeof(Handler), new Middleware("first"));
        await handler(new Context { Path = "/plaintext" });
        Console.WriteLine($"{handler.Target is Middleware} {handler.GetType().Name}");

        var describe = Find(typeof(Middleware), "Describe");
        var other = (Other)describe.CreateDelegate(typeof(Other), new Middleware("m"));
        Console.WriteLine(other("contravariant"));

        try { invoke.CreateDelegate(typeof(Other), new Middleware("x")); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { invoke.CreateDelegate(typeof(Handler), "not a middleware"); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { invoke.CreateDelegate(typeof(string), new Middleware("y")); }
        catch (ArgumentException e) { Console.WriteLine($"{e.Message} {e.ParamName}"); }

        var none = Attribute.GetCustomAttributes(invoke, typeof(NeverUsedAttribute));
        Console.WriteLine($"{none.Length} {none.GetType().Name} {invoke.IsDefined(typeof(NeverUsedAttribute))}");
    }
}
