// Activator.CreateInstance(type, args) with no constructor that binds: .NET's MissingMethodException carries the default
// binder's "Member not found." when some public constructor has the arguments' count, and no inner exception when none has.
using System;
using System.IO;

Try("MemoryStream(Stream)", () => Activator.CreateInstance(typeof(MemoryStream), new object[] { new MemoryStream() }));
Try("OnlyInt(string)", () => Activator.CreateInstance(typeof(OnlyInt), new object[] { "x" }));
Try("OnlyInt(int, int)", () => Activator.CreateInstance(typeof(OnlyInt), new object[] { 1, 2 }));
Try("NoPublic(int)", () => Activator.CreateInstance(typeof(NoPublic), new object[] { 1 }));
Try("OnlyInt(int)", () => Activator.CreateInstance(typeof(OnlyInt), new object[] { 7 }));

static void Try(string label, Func<object?> create)
{
    try { Console.WriteLine($"{label}: {create()}"); }
    catch (Exception e)
    {
        Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}");
        Console.WriteLine($"  inner: {(e.InnerException is { } inner ? inner.GetType().Name + ": " + inner.Message : "none")}");
    }
}

class OnlyInt
{
    private readonly int _value;
    public OnlyInt(int value) => _value = value;
    public override string ToString() => $"OnlyInt {_value}";
}

class NoPublic
{
    private NoPublic(int value) => Console.WriteLine(value);
}
