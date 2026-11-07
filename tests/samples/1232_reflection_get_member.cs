// Type.GetMember(name, MemberTypes, BindingFlags) and its relatives, and TypeInfo's GetDeclaredProperty / Declared* lookups.
#nullable disable
using System;
using System.Linq;
using System.Reflection;

class Base
{
    public virtual void Over() { }
    public int BaseField;
    public static int BaseStatic;
    public int BaseProp { get; set; }
}

class Derived : Base
{
    public int X { get; set; }
    private string Hidden2 { get; set; }
    public static int S { get; set; }
    public int this[int i] { get => i; set { } }
    public string Name;
    public event EventHandler Changed;
    public void M(int a) { }
    public void M(string a) { }
    public override void Over() { }
    private int Hidden() => 1;
    public Derived() { }
    public Derived(int x) { }
    public static void Fire(Derived c) { c.Changed?.Invoke(c, EventArgs.Empty); }
}

class Program
{
    static string Show(MemberInfo[] members) =>
        string.Join(", ", members.Select(m => m.MemberType + ":" + m.Name).OrderBy(s => s, StringComparer.Ordinal));

    static string Names(System.Collections.Generic.IEnumerable<MemberInfo> members) =>
        string.Join(",", members.Select(m => m.Name).Where(n => !n.StartsWith("<")).OrderBy(n => n, StringComparer.Ordinal));

    static void Main()
    {
        var t = typeof(Derived);
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        Console.WriteLine(Show(t.GetMember("M", MemberTypes.Method, flags)));
        Console.WriteLine(Show(t.GetMember("M", MemberTypes.Method | MemberTypes.Property, flags)));
        Console.WriteLine(Show(t.GetMember("X", MemberTypes.Property, flags)));
        Console.WriteLine(Show(t.GetMember("X", MemberTypes.Field, flags)));
        Console.WriteLine(Show(t.GetMember("Name", MemberTypes.All, flags)));
        Console.WriteLine(Show(t.GetMember("Changed", MemberTypes.Event, flags)));
        Console.WriteLine(Show(t.GetMember(".ctor", MemberTypes.Constructor, flags)));
        Console.WriteLine(Show(t.GetMember(".ctor", MemberTypes.Constructor | MemberTypes.Method, flags)));
        Console.WriteLine(Show(t.GetMember("Hidden", MemberTypes.Method, flags)));
        Console.WriteLine(Show(t.GetMember("Hidden", MemberTypes.Method, flags | BindingFlags.NonPublic)));
        Console.WriteLine(Show(t.GetMember("BaseField", MemberTypes.Field, flags)));
        Console.WriteLine(Show(t.GetMember("BaseField", MemberTypes.Field, flags | BindingFlags.DeclaredOnly)));
        Console.WriteLine(Show(t.GetMember("BaseStatic", MemberTypes.Field, flags)));
        Console.WriteLine(Show(t.GetMember("BaseStatic", MemberTypes.Field, flags | BindingFlags.FlattenHierarchy)));
        Console.WriteLine(Show(t.GetMember("name", MemberTypes.Field, flags)));
        Console.WriteLine(Show(t.GetMember("name", MemberTypes.Field, flags | BindingFlags.IgnoreCase)));
        Console.WriteLine(Show(t.GetMember("M*", MemberTypes.Method, flags)));
        Console.WriteLine(Show(t.GetMember("Over", MemberTypes.Method, flags)));
        Console.WriteLine(Show(t.GetMember("Nothing", MemberTypes.All, flags)));
        Console.WriteLine(Show(t.GetMember("Item", MemberTypes.Property, flags)));
        Console.WriteLine(Show(t.GetMember("X")));
        Console.WriteLine(Show(t.GetMember("M", flags)));
        Console.WriteLine(Show(t.GetMember("Name", BindingFlags.Public | BindingFlags.Static)));
        Console.WriteLine(Show(t.GetMembers(flags | BindingFlags.DeclaredOnly)));
        Console.WriteLine(Show(t.GetMembers()).Length > 0);
        try { t.GetMember(null, MemberTypes.All, flags); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        // the members come in kind order: methods, constructors, properties, events, fields
        Console.WriteLine(string.Join(" ", t.GetMember("*", MemberTypes.All, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.MemberType.ToString()[0] + m.Name)));

        var info = t.GetTypeInfo();
        Console.WriteLine(info.GetDeclaredProperty("X").Name + " " + info.GetDeclaredProperty("Hidden2").PropertyType.Name + " " + info.GetDeclaredProperty("S").Name);
        Console.WriteLine(info.GetDeclaredProperty("BaseProp") == null);
        Console.WriteLine(info.GetDeclaredProperty("Item").GetIndexParameters().Length);
        Console.WriteLine(info.GetDeclaredProperty("nope") == null);
        Console.WriteLine(Names(info.DeclaredProperties));
        Console.WriteLine(info.GetDeclaredField("Name").FieldType.Name + " " + (info.GetDeclaredField("BaseField") == null));
        Console.WriteLine(Names(info.DeclaredFields));
        Console.WriteLine(info.GetDeclaredEvent("Changed").EventHandlerType.Name + " " + (info.GetDeclaredEvent("nope") == null));
        Console.WriteLine(Names(info.DeclaredEvents));
        Console.WriteLine(Names(info.DeclaredMethods));
        Console.WriteLine(Names(info.DeclaredMembers));
        Console.WriteLine(info.DeclaredConstructors.Count());
        try { info.GetDeclaredProperty(null); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
