// Members from #104, as NativeAOT runs them: Type.IsSerializable, Type.InvokeMember, Type.GetType with resolvers, Exception.Source
// (null for a thrown exception: NativeAOT has no MethodBase for the frame), Enum.TryParse(Type, ...), Array.CreateInstanceFromArrayType,
// ModuleHandle (PlatformNotSupportedException), GetType() on a nullable, ResourceManager's constants and protected fields,
// CollectionExtensions.AsReadOnly and TupleExtensions.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Resources;

[Serializable] class Marked { }
class Unmarked
{
    public string Greet(string who) => "hello " + who;
    public static int Twice(int x) => x * 2;
    public int Count { get; set; } = 3;
    public string Field = "field";
}
enum Color { Red, Green, Blue }
delegate void Handler();

class Resources : ResourceManager
{
    public Resources() : base("Some.Resources", typeof(Resources).Assembly) { }
    public string Main => MainAssembly!.GetName().Name!;
}

static class Program
{
    static void Try(string name, Func<object?> f)
    {
        string result;
        try { result = f()?.ToString() ?? "null"; }
        catch (Exception e) { result = e.GetType().Name + ": " + e.Message; }
        Console.WriteLine($"{name}: {result}");
    }

    static void Main()
    {
#pragma warning disable SYSLIB0050
        Try("IsSerializable", () => $"{typeof(Marked).IsSerializable} {typeof(Unmarked).IsSerializable} {typeof(Color).IsSerializable} {typeof(Handler).IsSerializable} {typeof(int[]).IsSerializable} {typeof(string).IsSerializable}");
#pragma warning restore SYSLIB0050
        var target = new Unmarked();
        Try("InvokeMember method", () => typeof(Unmarked).InvokeMember("Greet", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, target, ["world"]));
        Try("InvokeMember static", () => typeof(Unmarked).InvokeMember("Twice", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static, null, null, [21]));
        Try("InvokeMember property", () => { typeof(Unmarked).InvokeMember("Count", BindingFlags.SetProperty, null, target, [7]); return typeof(Unmarked).InvokeMember("Count", BindingFlags.GetProperty, null, target, null); });
        Try("InvokeMember field", () => typeof(Unmarked).InvokeMember("Field", BindingFlags.GetField | BindingFlags.Public | BindingFlags.Instance, null, target, null));
        Try("InvokeMember field or property", () => typeof(Unmarked).InvokeMember("Count", BindingFlags.GetField | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, target, null));
        Try("InvokeMember missing field", () => typeof(Unmarked).InvokeMember("Nope", BindingFlags.GetField | BindingFlags.Public | BindingFlags.Instance, null, target, null));
        Try("InvokeMember missing", () => typeof(Unmarked).InvokeMember("Nope", BindingFlags.InvokeMethod, null, target, null));
        Try("InvokeMember no action", () => typeof(Unmarked).InvokeMember("Greet", BindingFlags.Public, null, target, null));
        Try("GetType resolver", () => Type.GetType("Unmarked", null, (assembly, name, ignoreCase) => assembly == null ? typeof(Unmarked) : null)?.Name);
        Try("GetType resolver null", () => Type.GetType("Nothing", null, (assembly, name, ignoreCase) => null, throwOnError: false)?.Name);
        Try("GetType resolver throws", () => Type.GetType("Nothing", null, (assembly, name, ignoreCase) => null, throwOnError: true));
        Try("Source thrown", () => { try { throw new InvalidOperationException(); } catch (Exception e) { return e.Source ?? "<null>"; } });
        Try("Source set", () => { try { throw new InvalidOperationException() { Source = "mine" }; } catch (Exception e) { return e.Source; } });
        Try("Enum.TryParse(Type)", () => { bool ok = Enum.TryParse(typeof(Color), "Blue", out object? v); bool ci = Enum.TryParse(typeof(Color), "green", true, out object? w); bool bad = Enum.TryParse(typeof(Color), "Purple", out object? x); return $"{ok} {v} {ci} {w} {bad} {x ?? "null"}"; });
        Try("Enum.TryParse(not enum)", () => Enum.TryParse(typeof(int), "1", out _));
        Try("CreateInstanceFromArrayType", () => Array.CreateInstanceFromArrayType(typeof(int[]), [3]).Length);
        Try("CreateInstanceFromArrayType rank", () => Array.CreateInstanceFromArrayType(typeof(int[]), [3, 4]).Length);
        Try("CreateInstanceFromArrayType negative", () => Array.CreateInstanceFromArrayType(typeof(int[]), [-1]).Length);
        Try("CreateInstanceFromArrayType not array", () => Array.CreateInstanceFromArrayType(typeof(int), [1]).Length);
        Try("ModuleHandle", () => typeof(Unmarked).Module.ModuleHandle.MDStreamVersion);
        int? some = 5, none = null;
        Try("nullable GetType", () => some.GetType().Name);
#pragma warning disable CS8629
        Try("null nullable GetType", () => none.GetType().Name);
#pragma warning restore CS8629
        var resources = new Resources();
        Try("ResourceManager", () => $"{ResourceManager.MagicNumber:X} {ResourceManager.HeaderVersionNumber} {resources.Main} {resources.BaseName}");
        Try("AsReadOnly", () => { IList<int> list = new List<int> { 1, 2 }; IDictionary<string, string> map = new Dictionary<string, string> { ["a"] = "b" }; return $"{list.AsReadOnly().Count} {map.AsReadOnly()["a"]}"; });
        Try("AsReadOnly null", () => ((IList<int>)null!).AsReadOnly());
        Try("Deconstruct", () => { var (flag, text) = Tuple.Create(true, "tuple"); return $"{flag} {text}"; });
        Try("Deconstruct pattern", () => Tuple.Create(1, 2, 3) is var (a, b, c) ? a + b + c : -1);
        Try("ToValueTuple/ToTuple", () => $"{Tuple.Create(1, "x").ToValueTuple()} {(2, "y").ToTuple()}");
    }
}
