// A Type local that holds typeof(T) wherever it is read stands for T in the calls lowered per type:
// Assembly.GetCustomAttributes(t, inherit), Enum.GetName(t, value), Marshal.SizeOf(t), RuntimeHelpers.GetUninitializedObject(t)
// (`Type t = typeof(AssemblyProductAttribute); assembly.GetCustomAttributes(t, false)`, as WinForms' Control.ProductName does).
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: Tag("first")]
[assembly: Tag("second")]

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
sealed class TagAttribute(string name) : Attribute { public string Name { get; } = name; }

enum Color { Red, Green, Blue }

[StructLayout(LayoutKind.Sequential)]
struct Pair { public int A; public long B; }

sealed class Widget { public int Count = 42; public override string ToString() => "Widget " + Count; }

static class Program
{
    static string Tags()
    {
        Type t = typeof(TagAttribute);
        Assembly assembly = typeof(Program).Module.Assembly;
        object[] attrs = assembly.GetCustomAttributes(t, false);
        var names = new string[attrs.Length];
        for (int i = 0; i < attrs.Length; i++) names[i] = ((TagAttribute)attrs[i]).Name;
        Array.Sort(names, StringComparer.Ordinal);
        return attrs.Length + ": " + string.Join(", ", names);
    }

    static void Main()
    {
        Console.WriteLine(Tags());

        var colors = typeof(Color);
        Console.WriteLine(Enum.GetName(colors, 2));
        Console.WriteLine(Enum.GetName(colors, 7) ?? "(none)");

        Type pair = typeof(Pair);
        Console.WriteLine(Marshal.SizeOf(pair));

        Type widget = typeof(Widget);
        var raw = (Widget)RuntimeHelpers.GetUninitializedObject(widget);
        Console.WriteLine(raw.Count);
    }
}
