// Custom attributes of source members as the JIT reports them (nullable annotations off, so csc adds no
// [Nullable]): the attributes csc synthesizes ([CompilerGenerated] on auto-property and event accessors and on
// backing fields, [DebuggerBrowsable], [ParamArray], [Extension]), the pseudo-custom attributes CoreCLR lists
// last in GetCustomAttributes() and first in GetCustomAttributesData(), and the ones it never lists
// ([MethodImpl], [SpecialName], [StructLayout], [DefaultParameterValue] is decoded, not listed).
#nullable disable
#pragma warning disable CS0649, CS0067, CS0169, CS0414
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
class MyAttribute : Attribute
{
    public int Value;
    public MyAttribute() { }
    public MyAttribute(int value) { Value = value; }
}

class Members
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Impl() { }
    [MethodImpl(MethodImplOptions.AggressiveInlining), My(1)] public void ImplAndMine() { }
    [SpecialName] public void Special() { }
    public int Plain;
    public readonly int ReadOnly = 1;
    public const int Constant = 1;

    [My(4)] public int Auto { [My(5)] get; [My(6)] set; }
    [field: My(7)] [My(8)] public int AutoField { get; set; }
    public int GetOnly { get; } = 3;
    public int Init { get; init; }
    public static int StaticAuto { get; set; }
    [Obsolete] public int Obsolete { get; set; }
    public int Computed => 1;
    public int Manual { get { return 1; } set { } }

    [My(9)] public void Params([My(10)] params int[] values) { }
    public int ParamsOnly(string name, params object[] rest) => 0;
    public void Optional([Optional] int a, [In] int[] b, [Out] int[] c, [My(11)] [Optional] string d, int e = 5, [CallerMemberName] string caller = "") { }
    public void ByRef(ref int a, out int b) { b = a; }
    [return: My(12)] public int Return() => 1;
    [DebuggerHidden] public void Hidden() { }
    [DebuggerNonUserCode] public void NonUserCode() { }
    [Obsolete("x", false)] public void Obsolete2() { }
    public static Members operator +(Members a, Members b) => a;
    public virtual void Virtual() { }
    public int Lambda() { Func<int> f = () => 1; return f(); }
    public int Captured(int x) { Func<int> f = () => x; return f(); }

}

class Events
{
    [method: My(1)] public event Action Plain;
    [field: My(2)] public event Action WithField;
    public event Action Manual { add { } remove { } }
    public void Touch() { Plain?.Invoke(); WithField?.Invoke(); }
}


[StructLayout(LayoutKind.Sequential)] struct Layout { public int A; }

static class Program
{
    static string Names(object[] attributes) => string.Join(" | ", attributes.Select(a => a.GetType().Name));
    static string Data(IEnumerable<CustomAttributeData> data) => string.Join(" | ", data.Select(d => d.AttributeType.Name));

    static void Line(string what, ICustomAttributeProvider provider, IEnumerable<CustomAttributeData> data) =>
        Console.WriteLine($"{what}: [{Names(provider.GetCustomAttributes(false))}] data [{Data(data)}]");

    static void Show([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, BindingFlags flags)
    {
        Console.WriteLine("== " + type.Name);
        flags |= BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var f in type.GetFields(flags).OrderBy(f => f.Name, StringComparer.Ordinal)) Line("field " + f.Name, f, f.CustomAttributes);
        foreach (var p in type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal)) Line("property " + p.Name, p, p.CustomAttributes);
        foreach (var e in type.GetEvents(flags).OrderBy(e => e.Name, StringComparer.Ordinal)) Line("event " + e.Name, e, e.CustomAttributes);
        foreach (var m in type.GetMethods(flags).OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            Line("method " + m.Name, m, m.CustomAttributes);
            foreach (var p in m.GetParameters()) Line("  parameter " + p.Name, p, p.CustomAttributes);
        }
    }

    static void Main()
    {
        Show(typeof(Members), BindingFlags.Public);
        Show(typeof(Events), BindingFlags.Public);

        Console.WriteLine("-- typed queries");
        var members = typeof(Members);
        Console.WriteLine(members.GetMethod("Params")!.GetCustomAttributes(typeof(MyAttribute), false).Length);
        Console.WriteLine(members.GetMethod("Params")!.GetParameters()[0].IsDefined(typeof(ParamArrayAttribute), false));
        Console.WriteLine(members.GetProperty("Auto")!.GetMethod!.IsDefined(typeof(CompilerGeneratedAttribute), false));
        Console.WriteLine(members.GetProperty("Computed")!.GetMethod!.IsDefined(typeof(CompilerGeneratedAttribute), false));
        Console.WriteLine(members.GetProperty("Manual")!.SetMethod!.IsDefined(typeof(CompilerGeneratedAttribute), false));
        Console.WriteLine(members.GetMethod("Impl")!.GetCustomAttributes(typeof(MethodImplAttribute), false).Length);
        Console.WriteLine(members.GetMethod("Lambda")!.GetCustomAttributes(false).Length);
        Console.WriteLine(typeof(Layout).GetCustomAttributes(false).Length);
        Console.WriteLine(typeof(Layout).IsDefined(typeof(MyAttribute), false));
    }
}
