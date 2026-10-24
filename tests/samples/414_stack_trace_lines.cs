// Stack-trace lines: exceptions the runtime raises (null dereference, array bounds, division by
// zero, invalid cast, checked overflow) belong to the frame that executed the operation; a frame's
// line is the start of its statement (sequence point), also for statements spanning lines, loop
// headers, switch expression arms and conditional operators; small methods the optimizer inlines
// still get their own frames.
using System;
using System.Text;

class Program
{
    static int[]? _nothing = null;
    static object _boxed = "text";
    static int _zero = 0;

    static void Main()
    {
        Show("null", () => Console.WriteLine(_nothing![0]));
        Show("bounds", () => Index(new int[2], 5));
        Show("divide", () => Console.WriteLine(Divide(10)));
        Show("cast", () => Console.WriteLine((int)_boxed));
        Show("overflow", () => Console.WriteLine(Add(int.MaxValue)));
        Show("multi-line statement", MultiLine);
        Show("for header", ForHeader);
        Show("switch arm", () => Console.WriteLine(Pick(2)));
        Show("conditional", () => Console.WriteLine(Choose(true)));
        Show("inlined helpers", () => Console.WriteLine(Outer(3)));
        Show("property chain", () => Console.WriteLine(new Holder().Value.Length));
    }

    static int Index(int[] values, int i) => values[i];
    static int Divide(int x) => x / _zero;
    static int Add(int x) => checked(x + 1);
    static int Check(int x) => x > 1 ? throw new ArgumentException("too big: " + x) : x;

    static void MultiLine()
    {
        var total = Math.Max(1,
            Check(2)) +
            1;
        Console.WriteLine(total);
    }

    static void ForHeader()
    {
        for (int i = 0;
             i < Check(i + 2);
             i++)
        {
            Console.WriteLine(i);
        }
    }

    static int Pick(int v)
    {
        return v switch
        {
            1 => 10,
            2 => Check(v),
            _ => 0,
        };
    }

    static int Choose(bool which)
    {
        var r = which
            ? Check(5)
            : 0;
        return r;
    }

    static int Outer(int x) => Middle(x) + 1;
    static int Middle(int x) => Leaf(x) * 2;
    static int Leaf(int x) => Check(x);

    sealed class Holder
    {
        public string Value => Inner.Text;
        Holder2 Inner => null!;
    }

    sealed class Holder2
    {
        public string Text = "";
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
            Console.Write(Clean(e.StackTrace));
        }
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
