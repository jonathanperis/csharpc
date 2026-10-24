// Exception.StackTrace / ToString(): the frames of the program's own code, formatted like .NET's
// System.Diagnostics.StackTrace (namespace, nested and generic types, generic methods, .NET parameter
// type names, constructors, accessors, operators, explicit interface implementations). Paths are
// trimmed to the file name: they depend on where the repository is checked out.
using System;
using System.Collections.Generic;
using System.Text;
using Shapes.Geometry;

namespace Shapes.Geometry
{
    public class Container<T>
    {
        public class Slot
        {
            public static void Fill<U>(T item, U extra, int[] grid, ref int count, out string label, int? limit,
                (int, string) pair, List<int> list, Dictionary<string, int> map, decimal price, DateTime when)
            {
                label = "";
                throw new InvalidOperationException("slot is full");
            }
        }

        private int _count;

        public Container(bool fill)
        {
            if (fill) Slot.Fill<long>(default!, 1L, new int[0], ref _count, out _, null, default, new List<int>(), new Dictionary<string, int>(), 1m, default);
        }

        public int Count => _count > 0 ? _count : throw new ArgumentOutOfRangeException(nameof(Count), "empty");
        public T this[int index] => throw new IndexOutOfRangeException("no item " + index);
        public static Container<T> operator +(Container<T> a, Container<T> b) => throw new NotSupportedException("cannot add");
    }

    public struct Point : IDisposable
    {
        public double X;
        public void Move(double dx, float dy, char tag, bool flag, byte b, object o, string s) => throw new ArgumentException("bad move", nameof(dx));
        void IDisposable.Dispose() => throw new ObjectDisposedException("Point");
    }

    public static class PointExtensions
    {
        public static void Scale(this Point p, double factor) => throw new ArgumentException("bad factor");
    }

    public class Program
    {
        public class ShapeException : Exception
        {
            public ShapeException(string message, Exception? inner = null) : base(message, inner) { }
        }
    }
}

public static class Program
{
    static void Main()
    {
        Show("generic nested", () => new Container<int>(true));
        Show("property", () => { var c = new Container<string>(false); Console.WriteLine(c.Count); });
        Show("indexer", () => Console.WriteLine(new Container<int>(false)[3]));
        Show("operator", () => { var a = new Container<int>(false); var b = a + a; });
        Show("struct", () => new Point().Move(1, 2, 'c', true, 1, null!, ""));
        Show("explicit interface", () => { using var p = new Point(); });
        Show("extension", () => new Point().Scale(2));
        Show("static method", Level1);
        ShowText("custom exception", () => throw new Shapes.Geometry.Program.ShapeException("bad shape"));
        ShowText("inner exception", Wrapper);
        ShowText("line directive", Mapped);

        var never = new InvalidOperationException("never thrown");
        Console.WriteLine(never.StackTrace == null);
        Console.WriteLine(never.ToString());
        Console.WriteLine($"interpolated: {Catch(Level1)?.GetType().Name}");
    }

    static void Level1() => Level2(1);
    static void Level2(int depth)
    {
        if (depth > 0)
            Level3("deep", depth);
    }
    static void Level3(string what, int depth) => throw new FormatException($"{what} {depth}");

    static void Wrapper()
    {
        try
        {
            Level1();
        }
        catch (FormatException e)
        {
            throw new Shapes.Geometry.Program.ShapeException("wrapped", e);
        }
    }

    static void Mapped()
    {
#line 500 "mapped.cs"
        Level1();
#line default
    }

    static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception e) { return e; }
    }

    static void Show(string name, Action action)
    {
        Console.WriteLine("== " + name);
        try
        {
            action();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name + ": " + e.Message);
            Console.WriteLine(Clean(e.StackTrace));
        }
    }

    static void ShowText(string name, Action action)
    {
        Console.WriteLine("== " + name);
        try { action(); }
        catch (Exception e) { Console.WriteLine(Clean(e.ToString())); }
    }

    /// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
    static string Clean(string? text)
    {
        if (text == null) return "<null>" + Environment.NewLine;
        var sb = new StringBuilder();
        foreach (var line in text.Split(Environment.NewLine))
        {
            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
            sb.AppendLine(at >= 0 && cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
        }
        return sb.ToString();
    }
}
