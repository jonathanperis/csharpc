// MarshalByValueComponent (System.Data's base class), CharUnicodeInfo.GetDecimalDigitValue and
// Activator.CreateInstance(Type, BindingFlags, Binder, object[], CultureInfo).
#nullable disable
#pragma warning disable IL2067, IL2072
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

class Widget : MarshalByValueComponent
{
    protected override void Dispose(bool disposing) { Console.WriteLine("Dispose(" + disposing + ")"); base.Dispose(disposing); }
}

class Point
{
    public int X, Y; public string Label;
    public Point() { Label = "origin"; }
    public Point(int x, int y) { X = x; Y = y; Label = "xy"; }
    private Point(string label) { Label = "private " + label; }
    public override string ToString() => $"{Label} ({X},{Y})";
}

class Program
{
    static Type K<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>() => typeof(T);

    static void Main()
    {
        var w = new Widget();
        w.Disposed += (s, e) => Console.WriteLine("Disposed event, sender is widget: " + ReferenceEquals(s, w));
        Console.WriteLine(w.ToString());
        Console.WriteLine($"{w.Site == null} {w.Container == null} {w.DesignMode} {w.GetService(typeof(string)) == null}");
        w.Dispose();

        foreach (string s in new[] { "7", "٣", "۹", "५", "x", "²", "Ⅻ", "０" })
            Console.Write(CharUnicodeInfo.GetDecimalDigitValue(s, 0) + " ");
        Console.WriteLine();
        try { CharUnicodeInfo.GetDecimalDigitValue("ab", 2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        Type t = K<Point>();
        BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;
        Console.WriteLine(Activator.CreateInstance(t, pub, null, null, CultureInfo.InvariantCulture));
        Console.WriteLine(Activator.CreateInstance(t, pub, null, new object[] { 3, 4 }, CultureInfo.InvariantCulture));
        Console.WriteLine(Activator.CreateInstance(t, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "p" }, null));
        try { Activator.CreateInstance(t, pub, null, new object[] { "nope" }, null); }
        catch (MissingMethodException e) { Console.WriteLine(e.Message); }
    }
}
