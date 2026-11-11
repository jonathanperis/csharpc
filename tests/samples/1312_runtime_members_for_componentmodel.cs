// The run-time members System.ComponentModel's IL needs from the runtime (ADR 0023): Type.Attributes (visibility, interface, abstract,
// sealed), Enum.Format with a Type known only at run time, ArrayList.Adapter over an IList, the attribute lists of framework types
// (TypeForwardedFrom, Serializable pseudo-attribute) and COM's negative answers.
using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

public enum Mode { Off, On = 5 }
[Flags] public enum Edges : byte { None = 0, Top = 1, Left = 2 }
public interface IThing { }
public abstract class Base { }
public sealed class Leaf : Base, IThing { class Hidden { } public class Shown { } public static Type HiddenType => typeof(Hidden); }
struct Plain { }

static class Program
{
    static string Describe(Type t)
    {
        var a = t.Attributes;
        var visibility = (a & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.NotPublic => "NotPublic", TypeAttributes.Public => "Public", TypeAttributes.NestedPublic => "NestedPublic",
            TypeAttributes.NestedPrivate => "NestedPrivate", _ => ((int)(a & TypeAttributes.VisibilityMask)).ToString(),
        };
        return $"{t.Name}: {visibility} interface={(a & TypeAttributes.Interface) != 0} abstract={(a & TypeAttributes.Abstract) != 0} sealed={(a & TypeAttributes.Sealed) != 0}";
    }

    static void Show(string label, Func<object?> action)
    {
        try { Console.WriteLine($"{label}: {action()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        foreach (var t in new[] { typeof(Mode), typeof(IThing), typeof(Base), typeof(Leaf), typeof(Plain), typeof(Leaf.Shown), Leaf.HiddenType })
            Console.WriteLine(Describe(t));

        Type mode = typeof(Mode), edges = typeof(Edges);
        Show("format G", () => Enum.Format(mode, Mode.On, "G"));
        Show("format D", () => Enum.Format(mode, 5, "D"));
        Show("format X", () => Enum.Format(mode, 5, "X"));
        Show("format flags", () => Enum.Format(edges, (byte)3, "G"));
        Show("format F", () => Enum.Format(edges, Edges.Top | Edges.Left, "F"));
        Show("format bad format", () => Enum.Format(mode, 1, "Z"));
        Show("format long format", () => Enum.Format(mode, 1, "GG"));
        Show("format wrong underlying", () => Enum.Format(mode, (byte)1, "G"));
        Show("format wrong enum", () => Enum.Format(mode, Edges.Top, "G"));
        Show("format not an enum", () => Enum.Format(typeof(int), 1, "G"));
        Show("format null value", () => Enum.Format(mode, null!, "G"));

        var array = new object[] { 3, 1, 2 };
        ArrayList adapter = ArrayList.Adapter(array);
        adapter.Sort();
        Console.WriteLine($"adapter: {string.Join(",", array)} count={adapter.Count} fixed={adapter.IsFixedSize} index={adapter.IndexOf(2)} contains={adapter.Contains(9)}");
        adapter.Reverse();
        Console.WriteLine($"reversed: {string.Join(",", array)} range={string.Join(",", adapter.GetRange(1, 2).ToArray())}");
        Show("adapter add", () => adapter.Add(4));
        Show("adapter null", () => ArrayList.Adapter(null!));
        var list = new ArrayList { "b", "a" };
        ArrayList over = ArrayList.Adapter(list);
        over.Add("c"); over.Sort(); over.Insert(0, "z");
        Console.WriteLine($"adapter over a list: {string.Join(",", list.ToArray())}");

        Console.WriteLine($"point attributes: {string.Join(",", Array.ConvertAll(typeof(Point).GetCustomAttributes(false), a => a.GetType().Name))}");
        Console.WriteLine($"point converter: {TypeDescriptor.GetConverter(typeof(Point)).GetType().Name}");
        Console.WriteLine($"int converter: {TypeDescriptor.GetConverter(typeof(int)).GetType().Name}");
        Console.WriteLine($"enum converter: {TypeDescriptor.GetConverter(typeof(Mode)).GetType().Name}");
        Console.WriteLine($"is com object: {Marshal.IsComObject(new object())}");
        Show("is com object null", () => Marshal.IsComObject(null!));
    }
}
