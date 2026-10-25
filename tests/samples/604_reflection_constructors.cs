// Constructor reflection the way dependency injection uses it: types reach [DynamicallyAccessedMembers]
// locations (a typeof argument, a generic parameter), their constructors and parameters are listed and
// invoked; open generic registrations are closed with MakeGenericType; arrays are created by element type.
#nullable disable
#pragma warning disable IL2026, IL2055, IL2060, IL2070, IL2072, IL2075, IL2080, IL3050
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

[AttributeUsage(AttributeTargets.Parameter)]
sealed class TagAttribute : Attribute
{
    public TagAttribute(string name) { Name = name; }
    public string Name { get; }
    public int Order { get; set; }
}

interface IRepository<T> { string Describe(); }
sealed class Repository<T> : IRepository<T>
{
    private readonly Clock _clock;
    public Repository(Clock clock) { _clock = clock; }
    public string Describe() => "Repository<" + typeof(T).Name + "> at " + _clock.Now;
}

sealed class Clock { public int Now => 42; }

class Service
{
    public string Text;
    public Service() { Text = "default"; }
    public Service(string name, int count = 3, [Tag("opt", Order = 2)] string suffix = null) { Text = name + "x" + count + suffix; }
    internal Service(int hidden) { Text = "hidden" + hidden; }
}

class Failing { public Failing(int value) { if (value < 0) throw new InvalidOperationException("negative: " + value); } }

static class Registry
{
    public static readonly List<(Type Service, Type Implementation)> Entries = new();
    public static void Add(Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementation) =>
        Entries.Add((service, implementation));
}

class Program
{
    static void Describe([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type type)
    {
        foreach (var c in type.GetConstructors())
        {
            Console.WriteLine($"{c} public={c.IsPublic} static={c.IsStatic} attributes={c.Attributes} {c.MemberType} {c.Name} {c.DeclaringType}");
            foreach (var p in c.GetParameters())
            {
                var all = p.GetCustomAttributes(true);
                var tags = (TagAttribute[])p.GetCustomAttributes(typeof(TagAttribute), false);
                Console.WriteLine($"  {p.Position} {p.Name}: {p.ParameterType} optional={p.IsOptional} default={p.HasDefaultValue} [{p.DefaultValue ?? "null"}] {p.Attributes} {all.Length} {string.Join(",", Array.ConvertAll(all, a => a.GetType().Name))} {(tags.Length > 0 ? tags[0].Name + "/" + tags[0].Order : "-")} {p.IsDefined(typeof(TagAttribute), false)} {p}");
            }
        }
    }

    static Type Keep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() => typeof(T);

    static void Main()
    {
        Describe(typeof(Service));
        var ctors = typeof(Service).GetConstructors();
        Console.WriteLine(ReferenceEquals(ctors[1], typeof(Service).GetConstructors()[1]) + " " + (ctors[1] == typeof(Service).GetConstructors()[1]));
        Console.WriteLine(((Service)ctors[0].Invoke(null)).Text);
        Console.WriteLine(((Service)ctors[1].Invoke(new object[] { "abc", 7, "!" })).Text);
        Console.WriteLine(((Service)ctors[1].Invoke(BindingFlags.DoNotWrapExceptions, null, new object[] { "abc", 7, null }, null)).Text);
        Console.WriteLine(typeof(Service).GetConstructor(new[] { typeof(string), typeof(int), typeof(string) }) == ctors[1]);
        Console.WriteLine(typeof(Service).GetConstructor(Type.EmptyTypes) == ctors[0]);
        Console.WriteLine(typeof(Service).GetConstructor(new[] { typeof(int) }) == null);
        Console.WriteLine(typeof(Service).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Length);
        try { ctors[1].Invoke(new object[] { "a" }); } catch (TargetParameterCountException e) { Console.WriteLine(e.Message); }
        try { ctors[1].Invoke(new object[] { 1, 2, "x" }); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

        var failing = Keep<Failing>().GetConstructors()[0];
        try { failing.Invoke(new object[] { -1 }); }
        catch (TargetInvocationException e) { Console.WriteLine($"{e.Message} -> {e.InnerException.GetType().Name}: {e.InnerException.Message}"); }
        try { failing.Invoke(BindingFlags.DoNotWrapExceptions, null, new object[] { -2 }, null); }
        catch (InvalidOperationException e) { Console.WriteLine("unwrapped: " + e.Message); }

        Registry.Add(typeof(IRepository<>), typeof(Repository<>));
        Registry.Add(typeof(Clock), typeof(Clock));
        Type requested = typeof(IRepository<string>);
        foreach (var (service, implementation) in Registry.Entries)
        {
            if (!service.IsGenericTypeDefinition || requested.GetGenericTypeDefinition() != service) continue;
            var closed = implementation.MakeGenericType(requested.GenericTypeArguments);
            var ctor = closed.GetConstructors()[0];
            var parameter = ctor.GetParameters()[0].ParameterType;
            var dependency = Keep<Clock>().GetConstructors()[0].Invoke(Array.Empty<object>());
            Console.WriteLine($"{closed} {ctor} {parameter} -> {((IRepository<string>)ctor.Invoke(new[] { dependency })).Describe()}");
        }
        try { typeof(Repository<>).MakeGenericType(typeof(int), typeof(int)); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { typeof(Clock).MakeGenericType(typeof(int)); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        var array = Array.CreateInstance(typeof(Service), 2);
        array.SetValue(new Service("in-array"), 1);
        Console.WriteLine($"{array.GetType()} {array.Length} {((Service[])array)[1].Text} {((Service[])array)[0] == null}");
        try { array.SetValue("not a service", 0); } catch (InvalidCastException e) { Console.WriteLine(e.Message); }
        try { Array.CreateInstance(typeof(Service), -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

        Func<string, int> parse = int.Parse;
        Func<int> answer = Answer;
        Console.WriteLine($"{answer.Method.Name} {answer.Method.DeclaringType} {answer.Method} {answer.Target == null} {answer.Method.ReturnType}");
        Console.WriteLine(answer.GetMethodInfo().IsStatic);
    }

    static int Answer() => 42;
}
