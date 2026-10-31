// [DynamicDependency(All, typeof(T))] keeps T's methods for reflection, as ILLink and NativeAOT do (RazorSlices'
// proxies name their slice types this way): a type found by name lists its methods, invokes them and makes
// delegates of them. The methods' invokers exist only because the program invokes through reflection.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

Probe.Run();

static class Probe
{
    private const string Name = "Slice";

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Slice))]
    public static void Run()
    {
        var type = Type.GetType(Name)!;
        var declared = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var m in declared.OrderBy(m => m.ToString(), StringComparer.Ordinal))
            Console.WriteLine($"{m} public {m.IsPublic} static {m.IsStatic}");
        var slice = Activator.CreateInstance(type)!;
        type.GetMethod("Write", [typeof(int)])!.Invoke(slice, [42]);
        type.GetMethod("Write", [typeof(string)])!.Invoke(slice, ["text"]);
        type.GetMethod("Write", [typeof(decimal)])!.Invoke(slice, [1.5m]);
        Console.WriteLine(type.GetMethod("Render")!.Invoke(slice, null));
        var twice = type.GetMethod("Twice", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate(typeof(Func<int, int>)) as Func<int, int>;
        Console.WriteLine(twice!(21));
        var render = (Func<string>)type.GetMethod("Render")!.CreateDelegate(typeof(Func<string>), slice);
        Console.WriteLine(render());
    }
}

public class Slice
{
    private readonly System.Text.StringBuilder _output = new();
    public void Write(int value) => _output.Append("int ").Append(value).Append(';');
    public void Write(string value) => _output.Append("string ").Append(value).Append(';');
    public void Write(decimal value) => _output.Append("decimal ").Append(value).Append(';');
    public void Write(double value) => _output.Append("double ").Append(value).Append(';');
    public string Render() => _output.ToString();
    private static int Twice(int x) => x * 2;
}
