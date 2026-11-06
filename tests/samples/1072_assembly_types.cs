// Assembly.GetTypes / GetExportedTypes / DefinedTypes / ExportedTypes and Module.GetTypes for the program's own
// assembly: the declared types plus the ones csc synthesizes (closure classes, <>c, method-group caches, state
// machines), in metadata order, with their kind, visibility, nesting and base type;
// Assembly.GetType / Module.GetType find them by name; Assembly.EntryPoint, GetModules and Type.Module.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Shapes
{
    public interface IShape { double Area(); }
    public abstract class Shape : IShape { public abstract double Area(); public class Metrics { } protected class Hidden { } }
    public sealed class Circle : Shape { public double R; public override double Area() => 3 * R * R; }
    public struct Point { public int X, Y; }
    public enum Color { Red, Green }
    public delegate int Transform(int value);
    public static class Helpers { public static int Twice(int x) => x * 2; }
    internal class Registry<T> where T : class { internal class Entry { } }
    public class Outer { public class Middle { public class Inner { } private class Secret { } } internal struct Packed { } }
}

public class Top
{
    public int Run(int seed)
    {
        Func<int, int> capture = x => x + seed;
        Func<int, int> plain = x => x * 2;
        Func<int, int> group = Shapes.Helpers.Twice;
        return capture(1) + plain(2) + group(3);
    }

    public async Task<int> Wait() { await Task.Yield(); return 1; }
    public IEnumerable<int> Numbers() { yield return 1; yield return 2; }
}


static class Program
{
    static string Describe(Type t)
    {
        var kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : typeof(Delegate).IsAssignableFrom(t) ? "delegate" : "class";
        var flags = string.Join(" ", new[]
        {
            t.IsPublic ? "public" : t.IsNotPublic ? "internal" : t.IsNestedPublic ? "nested-public" : t.IsNestedPrivate ? "nested-private" : t.IsNestedAssembly ? "nested-internal"
                : t.IsNestedFamily ? "nested-protected" : "nested-other",
            t.IsAbstract ? "abstract" : "", t.IsSealed ? "sealed" : "", t.IsGenericTypeDefinition ? "generic" : "", t.IsVisible ? "visible" : "",
        }.Where(f => f.Length > 0));
        return $"{t.FullName ?? t.Name} | {kind} | {flags} | ns={t.Namespace ?? "-"} | declaring={t.DeclaringType?.Name ?? "-"} | base={t.BaseType?.Name ?? "-"}";
    }

    static void Main(string[] args)
    {
        var assembly = typeof(Top).Assembly;
        Console.WriteLine("-- GetTypes");
        var types = assembly.GetTypes();
        foreach (var t in types) Console.WriteLine(Describe(t));

        Console.WriteLine("-- GetExportedTypes");
        foreach (var t in assembly.GetExportedTypes()) Console.WriteLine(t.FullName);
        Console.WriteLine(assembly.ExportedTypes.Count() == assembly.GetExportedTypes().Length);

        Console.WriteLine("-- DefinedTypes / Module.GetTypes");
        Console.WriteLine(assembly.DefinedTypes.Select(t => t.FullName).SequenceEqual(types.Select(t => t.FullName)));
        Console.WriteLine(assembly.ManifestModule.GetTypes().SequenceEqual(types));
        Console.WriteLine(assembly.GetTypes() != assembly.GetTypes());
        Console.WriteLine(typeof(Top).Assembly.GetTypes().Contains(typeof(Top)));
        Console.WriteLine(types.Contains(typeof(Shapes.Circle)) && types.Contains(typeof(Shapes.Outer.Middle.Inner)) && types.Contains(typeof(Shapes.Color)));
        Console.WriteLine(types.Contains(typeof(Shapes.Registry<>)));

        Console.WriteLine("-- GetType by name");
        foreach (var name in new[] { "Top", "Shapes.Circle", "Shapes.Outer+Middle+Inner", "Shapes.Registry`1", "Shapes.Registry`1+Entry", "Nope", "Shapes.Outer+Nope" })
            Console.WriteLine($"{name}: {assembly.GetType(name)?.FullName ?? "null"} / {assembly.ManifestModule.GetType(name)?.FullName ?? "null"}");
        Console.WriteLine(assembly.GetType("shapes.circle", false, true)?.FullName);
        foreach (var t in types.Where(t => t.Name.Contains('<')).Take(3))
            Console.WriteLine($"synthesized {t.FullName}: {assembly.GetType(t.FullName!) == t}");

        Console.WriteLine("-- entry point and modules");
        var entry = assembly.EntryPoint!;
        Console.WriteLine($"{entry.DeclaringType} {entry.Name} {entry.ReturnType} {entry.GetParameters().Length} {entry.IsStatic}");
        Console.WriteLine(entry.Module == assembly.ManifestModule);
        var modules = assembly.GetModules();
        Console.WriteLine($"{modules.Length} {modules[0] == assembly.ManifestModule} {assembly.GetLoadedModules().Length} {assembly.Modules.Count()}");
        Console.WriteLine($"{assembly.ManifestModule.Name} {assembly.ManifestModule.ScopeName} {assembly.ManifestModule.Assembly == assembly}");
        Console.WriteLine(typeof(Top).Module == assembly.ManifestModule);
        Console.WriteLine(typeof(Shapes.Circle).Module == typeof(Top).Module);
        Console.WriteLine(typeof(Shapes.Circle).GetMethod("Area")!.Module == typeof(Top).Module);
        Console.WriteLine($"{typeof(int).Module.Name} {typeof(List<int>).Module.ScopeName} {typeof(Uri).Module.Name}");
        Console.WriteLine(typeof(int).Module != typeof(Top).Module);
        Console.WriteLine(typeof(int).Module == typeof(string).Module);
        Console.WriteLine(assembly.ManifestModule.MetadataToken);
        Console.WriteLine(assembly.GetModule(assembly.ManifestModule.Name)?.Name);
        Console.WriteLine(assembly.GetModule("other.dll") is null);
        Console.WriteLine(Assembly.GetAssembly(typeof(Top)) == assembly);
        Console.WriteLine(assembly.IsFullyTrusted + " " + assembly.ImageRuntimeVersion + " " + assembly.IsDynamic + " " + assembly.ReflectionOnly);
    }
}
