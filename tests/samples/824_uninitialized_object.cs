// RuntimeHelpers.GetUninitializedObject: zeroed instances without a constructor run (Nullable<T>
// gives a boxed default T), and .NET's exceptions, in its order, for the types it refuses.
#pragma warning disable CS0649, IL2026, IL2062, IL2111 // fields never assigned; trimming analysis of the odd types
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

struct Point { public int X; public int Y; public override string ToString() => $"({X},{Y})"; }
class Plain { public int Value = 42; public string Name = "x"; public Plain() { Console.WriteLine("ctor ran"); } }
abstract class Shape { }
abstract class Shape<T> { }
interface IThing { }
enum Color { Red, Green }
class Gen<T> { public T? Item; }
static class Holder { }
delegate void D();

class Program
{
    static void Show(string label, Func<object> create)
    {
        try
        {
            var o = create();
            var text = o is char c ? ((int)c).ToString() : o.ToString();
            Console.WriteLine($"{label}: {o.GetType()} = {text}");
        }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().FullName}: {e.Message}"); }
    }

    static void Main()
    {
        Show("int?", () => RuntimeHelpers.GetUninitializedObject(typeof(int?)));
        Show("Point?", () => RuntimeHelpers.GetUninitializedObject(typeof(Point?)));
        Show("Color?", () => RuntimeHelpers.GetUninitializedObject(typeof(Color?)));
        Show("int", () => RuntimeHelpers.GetUninitializedObject(typeof(int)));
        Show("bool", () => RuntimeHelpers.GetUninitializedObject(typeof(bool)));
        Show("double", () => RuntimeHelpers.GetUninitializedObject(typeof(double)));
        Show("char", () => RuntimeHelpers.GetUninitializedObject(typeof(char)));
        Show("long", () => RuntimeHelpers.GetUninitializedObject(typeof(long)));
        Show("Point", () => RuntimeHelpers.GetUninitializedObject(typeof(Point)));
        Show("Color", () => RuntimeHelpers.GetUninitializedObject(typeof(Color)));
        Show("Plain", () => RuntimeHelpers.GetUninitializedObject(typeof(Plain)));
        Show("Gen<int>", () => RuntimeHelpers.GetUninitializedObject(typeof(Gen<int>)));
        Show("object", () => RuntimeHelpers.GetUninitializedObject(typeof(object)));
        Show("Shape", () => RuntimeHelpers.GetUninitializedObject(typeof(Shape)));
        Show("Shape<>", () => RuntimeHelpers.GetUninitializedObject(typeof(Shape<>)));
        Show("Holder", () => RuntimeHelpers.GetUninitializedObject(typeof(Holder)));
        Show("Array", () => RuntimeHelpers.GetUninitializedObject(typeof(Array)));
        Show("Enum", () => RuntimeHelpers.GetUninitializedObject(typeof(Enum)));
        Show("ValueType", () => RuntimeHelpers.GetUninitializedObject(typeof(ValueType)));
        Show("Delegate", () => RuntimeHelpers.GetUninitializedObject(typeof(Delegate)));
        Show("MulticastDelegate", () => RuntimeHelpers.GetUninitializedObject(typeof(MulticastDelegate)));
        Show("IThing", () => RuntimeHelpers.GetUninitializedObject(typeof(IThing)));
        Show("IList<>", () => RuntimeHelpers.GetUninitializedObject(typeof(IList<>)));
        Show("int[]", () => RuntimeHelpers.GetUninitializedObject(typeof(int[])));
        Show("string[]", () => RuntimeHelpers.GetUninitializedObject(typeof(string[])));
        Show("int[,]", () => RuntimeHelpers.GetUninitializedObject(typeof(int[,])));
        Show("string", () => RuntimeHelpers.GetUninitializedObject(typeof(string)));
        Show("Gen<>", () => RuntimeHelpers.GetUninitializedObject(typeof(Gen<>)));
        Show("Nullable<>", () => RuntimeHelpers.GetUninitializedObject(typeof(Nullable<>)));
        Show("List<>", () => RuntimeHelpers.GetUninitializedObject(typeof(List<>)));
        Show("Span<>", () => RuntimeHelpers.GetUninitializedObject(typeof(Span<>)));
        Show("T", () => RuntimeHelpers.GetUninitializedObject(typeof(Gen<>).GetGenericArguments()[0]));
        Show("D", () => RuntimeHelpers.GetUninitializedObject(typeof(D)));
        Show("Action", () => RuntimeHelpers.GetUninitializedObject(typeof(Action)));
        Show("Func<>", () => RuntimeHelpers.GetUninitializedObject(typeof(Func<>)));
        Show("Span<int>", () => RuntimeHelpers.GetUninitializedObject(typeof(Span<int>)));
        Show("ReadOnlySpan<char>", () => RuntimeHelpers.GetUninitializedObject(typeof(ReadOnlySpan<char>)));
        Show("int*", () => RuntimeHelpers.GetUninitializedObject(typeof(int*)));
        Show("void", () => RuntimeHelpers.GetUninitializedObject(typeof(void)));
        Show("null", () => RuntimeHelpers.GetUninitializedObject(null!));

        var plain = (Plain)RuntimeHelpers.GetUninitializedObject(typeof(Plain));
        Console.WriteLine($"fields: {plain.Value} {plain.Name ?? "null"}");
        var boxed = RuntimeHelpers.GetUninitializedObject(typeof(int?));
        Console.WriteLine($"int? is a boxed int: {boxed is int}, {boxed is int i && i == 0}");
        var gen = (Gen<int>)RuntimeHelpers.GetUninitializedObject(typeof(Gen<int>));
        Console.WriteLine($"Gen<int>.Item: {gen.Item}");
    }
}
