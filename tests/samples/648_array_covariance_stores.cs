// Array covariance: a string[] is an object[], so stores through the object[] are checked against the
// array's real element type (stelem.ref: ArrayTypeMismatchException), writable references to its elements
// need the exact element type (ldelema, new Span<T>(T[])), and casts between array types follow the CLR's
// array compatibility rules.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

class Animal { public override string ToString() => GetType().Name; }
class Dog : Animal { }
class Cat : Animal { }
sealed class Leaf { }
interface IShape { }
class Square : IShape { }
class Circle : IShape { }
enum Day { Mon }

static class Program
{
    static void Try(string name, Action action)
    {
        try { action(); Console.WriteLine(name + ": ok"); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().FullName + ": " + e.Message); }
    }

    static void Set<T>(T[] array, T value) => array[0] = value;
    static void Show(in object o) => Console.WriteLine(o ?? "null");

    static void Main()
    {
        object[] strings = new string[1];
        Try("int into string[]", () => strings[0] = 42);
        Try("string into string[]", () => strings[0] = "x");
        Try("null into string[]", () => strings[0] = null);
        Animal[] dogs = new Dog[2];
        Try("cat into Dog[]", () => dogs[0] = new Cat());
        Try("dog into Dog[]", () => dogs[1] = new Dog());
        Animal[] animals = new Animal[1];
        Try("cat into Animal[]", () => animals[0] = new Cat());
        IShape[] squares = new Square[1];
        Try("circle into Square[]", () => squares[0] = new Circle());
        Try("leaf into Leaf[]", () => { Leaf[] leaves = new Leaf[1]; leaves[0] = new Leaf(); });
        Try("generic store", () => Set<object>(strings, 7));
        Try("generic store ok", () => Set<Animal>(dogs, new Dog()));

        // Arrays of arrays and of (variant) interfaces.
        object[] jagged = new int[][] { null };
        Try("string[] into int[][]", () => jagged[0] = new string[0]);
        Try("int[] into int[][]", () => jagged[0] = new int[3]);
        object[] objectArrays = new object[][] { null };
        Try("string[] into object[][]", () => objectArrays[0] = new string[0]);
        Try("int[] into object[][]", () => objectArrays[0] = new int[0]);
        Try("object[] into string[][]", () => { object[][] j = new string[1][]; j[0] = new object[1]; });
        IEnumerable<object>[] enumerables = new IEnumerable<string>[1];
        Try("List<string> into IEnumerable<string>[]", () => enumerables[0] = new List<string>());
        Try("List<object> into IEnumerable<string>[]", () => enumerables[0] = new List<object>());
        object[] variant = new IEnumerable<object>[1];
        Try("string[] into IEnumerable<object>[]", () => variant[0] = new string[1]);
        Try("int[] into IEnumerable<object>[]", () => variant[0] = new int[1]);
        Try("Func<string> into Func<object>[]", () => { object[] f = new Func<object>[1]; f[0] = (Func<string>)(() => "x"); });

        // Stores the CFG splits: conditional values, ??=, deconstruction, read-modify-write.
        bool never = strings.Length > 5;
        Try("conditional bad", () => strings[0] = never ? "q" : (object)7);
        Try("conditional ok", () => strings[0] = !never ? "q" : (object)7);
        Try("coalesce ok", () => { strings[0] = null; strings[0] ??= "z"; });
        Try("coalesce bad", () => { strings[0] = null; strings[0] ??= 5; });
        Try("deconstruction", () => (strings[0], never) = (3, true));
        Try("read-modify-write", () => strings[0] = strings[0] + "y");
        Console.WriteLine(strings[0]);

        // Rectangular arrays.
        object[,] grid = new string[1, 1];
        Try("int into string[,]", () => grid[0, 0] = 1);
        Try("string into string[,]", () => grid[0, 0] = "1");

        // Writable references: ldelema checks the exact element type, readonly references don't.
        Try("ref local", () => { ref object r = ref strings[0]; });
        Try("ref readonly local", () => { ref readonly object r = ref strings[0]; Console.WriteLine(r ?? "null"); });
        Try("in argument", () => Show(in strings[0]));
        Try("ref into object[]", () => { object[] o = new object[1]; ref object r = ref o[0]; r = 3; Console.WriteLine(o[0]); });
        Try("Interlocked.Exchange", () => Interlocked.Exchange(ref strings[0], "w"));
        Try("out argument", () => new Dictionary<int, object>().TryGetValue(1, out strings[0]));

        // Spans and memory over a covariant array.
        Try("Span<object>", () => { var s = new Span<object>(strings); });
        Try("Span<object> window", () => { var s = new Span<object>(strings, 0, 1); });
        Try("Span<object> over object[]", () => { var s = new Span<object>(new object[1]); });
        Try("ReadOnlySpan<object>", () => { var s = new ReadOnlySpan<object>(strings); Console.WriteLine(s.Length); });
        Try("AsSpan", () => { var s = strings.AsSpan(); });
        Try("Memory<object>", () => { var m = new Memory<object>(strings); });
        Try("ReadOnlyMemory<object>", () => { var m = new ReadOnlyMemory<object>(strings); Console.WriteLine(m.Length); });

        // Library stores: IList<T>, IList, Array.SetValue, Array.Fill, Array.Resize.
        IList<object> list = new string[1];
        Try("IList<object> int", () => list[0] = 5);
        Try("IList<object> string", () => list[0] = "s");
        IList untyped = new string[1];
        Try("IList int", () => untyped[0] = 5);
        Try("IList string", () => untyped[0] = "s");
        Try("IList out of range", () => untyped[5] = 1);
        IList ints = new int[1];
        Try("IList string into int[]", () => ints[0] = "x");
        Array array = new string[1];
        Try("SetValue int", () => array.SetValue(5, 0));
        Try("SetValue string", () => array.SetValue("s", 0));
        Try("Fill bad", () => Array.Fill(strings, (object)5));
        Try("Fill ok", () => Array.Fill(strings, (object)"z"));
        Try("Resize", () => { object[] r = new string[] { "a" }; Array.Resize(ref r, 2); r[1] = 3; Console.WriteLine(r.GetType() + " " + r[1]); });
        Try("Sort", () => { object[] s = new string[] { "b", "a" }; Array.Sort(s); Console.WriteLine(string.Join(",", s)); });
        Try("Reverse", () => { object[] s = new string[] { "b", "a" }; Array.Reverse(s); Console.WriteLine(string.Join(",", s)); });

        // Casts between array types.
        object o1 = new string[1], o2 = new Dog[1], o3 = new int[1], o4 = new int[1][];
        Console.WriteLine($"{o1 is object[]} {o1 is IEnumerable<object>} {o2 is Animal[]} {o2 is object[]} {o2 is Cat[]}");
        Console.WriteLine($"{o3 is uint[]} {o3 is Day[]} {o3 is object[]} {o3 is long[]} {o4 is object[]} {o4 is uint[][]}");
        Try("(object[]) string[]", () => Console.WriteLine(((object[])o1).Length));
        Try("(Animal[]) string[]", () => Console.WriteLine(((Animal[])o1).Length));
    }
}
