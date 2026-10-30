// FieldInfo over kept metadata: Type.GetField/GetFields with binding flags, GetValue/SetValue on
// class, struct and static fields, constants, enum members with attributes, and .NET's exceptions.
using System;
using System.ComponentModel;
using System.Reflection;

class Base
{
    public int BaseValue = 1;
    protected string Hidden = "base";
    private double _secret = 2.5;
    public static int BaseCount = 7;
    public double Secret => _secret;
}

class Derived : Base
{
    public const int Answer = 42;
    public const string Greeting = "hi";
    public static readonly string Frozen = "frozen";
    public static long Counter = 3;
    public string Name = "derived";
    public int? Maybe;
    private readonly int _id = 9;
    public int Auto { get; set; } = 5;
    public int Id => _id;
}

struct Point
{
    public int X;
    public int Y;
    public override string ToString() => $"({X}, {Y})";
}

enum Color
{
    [Description("Warm")] Red = 1,
    Green = 2,
    [Description("Cool")] Blue = 4,
}

class Program
{
    static void Main()
    {
        foreach (var f in typeof(Derived).GetFields())
            Console.WriteLine($"public: {f} static={f.IsStatic} literal={f.IsLiteral} initonly={f.IsInitOnly} declared={f.DeclaringType.Name}");
        foreach (var f in typeof(Derived).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            Console.WriteLine($"nonpublic: {f.Name} private={f.IsPrivate} family={f.IsFamily}");
        foreach (var f in typeof(Derived).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            Console.WriteLine($"flatten: {f.Name}");
        foreach (var f in typeof(Derived).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Console.WriteLine($"declared: {f.Name}");

        var d = new Derived();
        var name = typeof(Derived).GetField("Name");
        Console.WriteLine(name.GetValue(d));
        name.SetValue(d, "changed");
        Console.WriteLine(d.Name);
        var id = typeof(Derived).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
        id.SetValue(d, 11);
        Console.WriteLine(d.Id);
        var maybe = typeof(Derived).GetField("Maybe");
        Console.WriteLine(maybe.GetValue(d) == null);
        maybe.SetValue(d, 4);
        Console.WriteLine(d.Maybe);
        var baseValue = typeof(Derived).GetField("BaseValue");
        baseValue.SetValue(d, 100);
        Console.WriteLine(d.BaseValue);
        var secret = typeof(Base).GetField("_secret", BindingFlags.NonPublic | BindingFlags.Instance);
        secret.SetValue(d, 9.75);
        Console.WriteLine(d.Secret);
        Console.WriteLine(typeof(Derived).GetField("_secret", BindingFlags.NonPublic | BindingFlags.Instance) == null);
        Console.WriteLine(typeof(Derived).GetField("name") == null);
        Console.WriteLine(typeof(Derived).GetField("name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).Name);

        Console.WriteLine(typeof(Derived).GetField("Answer").GetValue(null));
        Console.WriteLine(typeof(Derived).GetField("Answer").GetRawConstantValue());
        Console.WriteLine(typeof(Derived).GetField("Greeting").GetValue(null));
        var counter = typeof(Derived).GetField("Counter");
        counter.SetValue(null, 10L);
        Console.WriteLine(Derived.Counter);
        Console.WriteLine(typeof(Derived).GetField("Frozen").GetValue(null));

        var p = new Point { X = 1, Y = 2 };
        object boxed = p;
        typeof(Point).GetField("Y").SetValue(boxed, 20);
        Console.WriteLine(boxed);
        Console.WriteLine(typeof(Point).GetField("X").GetValue(boxed));

        foreach (var f in typeof(Color).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var description = f.GetCustomAttribute<DescriptionAttribute>();
            Console.WriteLine($"{f.Name} = {f.GetValue(null)} ({(int)f.GetRawConstantValue()}) {description?.Description ?? "-"} {f.FieldType.Name}");
        }

        try { name.GetValue(null); } catch (TargetException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { name.GetValue("text"); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { name.SetValue(d, 5); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { typeof(Derived).GetField("Answer").SetValue(null, 1); } catch (FieldAccessException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { typeof(Derived).GetField("Frozen").SetValue(null, "x"); } catch (FieldAccessException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { name.GetRawConstantValue(); } catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        Console.WriteLine(ReferenceEquals(typeof(Derived).GetField("Name"), name));
    }
}
