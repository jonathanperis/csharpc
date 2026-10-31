namespace IlDynamicLib;

public static class Late
{
    public static object Twice(object value)
    {
        dynamic d = value;
        return d + d;
    }

    // A dynamic receiver binds against its run-time type as the library sees it: a type the library can't
    // access binds as its nearest accessible base (the binder's "best accessible type").
    public static string Describe(object value)
    {
        dynamic d = value;
        try { return d.Describe(); }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException e) { return "binder: " + e.Message; }
    }

    public static int Sum(object items)
    {
        dynamic list = items;
        var total = 0;
        foreach (var x in list) total += x;
        return total;
    }

    // Simple names with dynamic arguments: internal static overloads of the library.
    public static object Tripled(object value) => Triple((dynamic)value);
    internal static int Triple(int x) => x * 3;
    internal static string Triple(string s) => s + s + s;
}

public sealed class Counter
{
    private int _seed;
    public Counter(int seed) => _seed = seed;
    // Private instance overloads, called by simple name through `this`.
    public object Bump(object by) => Add((dynamic)by);
    private int Add(int by) => _seed += by;
    private string Add(string tag) => tag + ":" + _seed;
    public int Value => _seed;
}

public class Shape
{
    public virtual string Describe() => "a shape";
    internal string Secret() => "library internal";
}

public class Square : Shape
{
    public double Side { get; set; } = 2;
    public override string Describe() => "a square of side " + Side;
}

public static class Shapes
{
    public static object Area(object shape)
    {
        dynamic s = shape;
        s.Side *= 1.5;
        return s.Side * s.Side;
    }

    // Internal to the library: accessible to its own call sites.
    public static string Secret(object shape) => ((dynamic)shape).Secret();
}
