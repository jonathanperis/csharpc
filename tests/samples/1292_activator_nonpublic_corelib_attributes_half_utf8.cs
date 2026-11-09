// Members the framework's System.Text.Json IL calls: Activator.CreateInstance(Type, bool nonPublic) (a private constructor, a
// struct's boxed default, the exceptions), the attributes of a System.Private.CoreLib type for an attribute class that is
// not CoreLib's (none, exactly), and Half.Parse / TryParse over UTF-8 bytes.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

static void Show(string label, Func<object> create)
{
    try { Console.WriteLine($"{label}: {create()}"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {(e is TargetInvocationException t ? t.InnerException!.Message : e.Message)}"); }
}

Show("Hidden True", () => Activator.CreateInstance(typeof(Hidden), true)!);
Show("Hidden False", () => Activator.CreateInstance(typeof(Hidden), false)!);
Show("Open False", () => Activator.CreateInstance(typeof(Open), false)!);
Show("Open True", () => Activator.CreateInstance(typeof(Open), true)!);
Show("Point False", () => Activator.CreateInstance(typeof(Point), false)!);
Show("Point True", () => Activator.CreateInstance(typeof(Point), true)!);
Show("NeedsArgs True", () => Activator.CreateInstance(typeof(NeedsArgs), true)!);
Show("NeedsArgs False", () => Activator.CreateInstance(typeof(NeedsArgs), false)!);
Show("WithCtor False", () => Activator.CreateInstance(typeof(WithCtor), false)!);
Show("Throws True", () => Activator.CreateInstance(typeof(Throws), true)!);

Console.WriteLine("attributes of CoreLib types");
foreach (var type in new[] { typeof(int), typeof(string), typeof(object), typeof(DateTime), typeof(Half) })
{
    Console.WriteLine($"{type.Name} {type.GetCustomAttributes(typeof(MarkerAttribute), false).Length} {type.GetCustomAttributes(typeof(MarkerAttribute), true).Length} {type.IsDefined(typeof(MarkerAttribute), false)} {(type.GetCustomAttribute<MarkerAttribute>() is null)}");
}
Console.WriteLine(typeof(Open).GetCustomAttributes(typeof(MarkerAttribute), false).Length + " " + typeof(Open).IsDefined(typeof(MarkerAttribute), false));

Console.WriteLine("Half over UTF-8");
var ci = CultureInfo.InvariantCulture;
Console.WriteLine(Half.TryParse("1.5"u8, ci, out var h) + " " + h + " " + Half.TryParse("abc"u8, ci, out _) + " " + Half.TryParse(""u8, out _) + " " + Half.Parse("2.25"u8, ci) + " " + Half.Parse("7"u8, NumberStyles.Integer, ci) + " " + Half.TryParse("65520"u8, out var big) + " " + big + " " + Half.TryParse("-0.5"u8, NumberStyles.Float, ci, out var neg) + " " + neg);
try { Half.Parse("x"u8, ci); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }

[AttributeUsage(AttributeTargets.All)]
class MarkerAttribute : Attribute { }

[Marker]
class Open { public override string ToString() => "open"; }

class Hidden
{
    private Hidden() { }
    public override string ToString() => "hidden";
}

struct Point
{
    public int X;
    public override string ToString() => "point " + X;
}

class NeedsArgs
{
    public NeedsArgs(int x) { }
}

class WithCtor
{
    public WithCtor() { }
    public override string ToString() => "withctor";
}

class Throws
{
    private Throws() => throw new InvalidOperationException("ctor failed");
}
