// System.CodeDom.Compiler.IndentedTextWriter (System.Private.CoreLib): the type the Regex source generator writes its code
// with, so csharpc needs it to compile the generator itself. Indentation, tabs pending across Write/WriteLine, negative
// indent, a custom tab string, the NewLine forwarded to the inner writer, WriteLineNoTabs and the Write overloads.
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Text;
using System.Threading.Tasks;

class Program
{
    // The writers use "\n" so their text is the same on every OS; the console gets the platform newline, as in every other sample.
    static void Show(string text) => Console.Write(text.Replace("\n", Environment.NewLine));

    static async Task Main()
    {
        var inner = new StringWriter { NewLine = "\n" };
        var w = new IndentedTextWriter(inner);
        Console.WriteLine("default tab string: [" + IndentedTextWriter.DefaultTabString + "]");
        w.WriteLine("namespace N");
        w.WriteLine("{");
        w.Indent++;
        w.WriteLine("class C");
        w.WriteLine("{");
        w.Indent++;
        w.Write("int x");
        w.Write(" = ");
        w.Write(42);
        w.WriteLine(";");
        w.WriteLine();
        w.Write('c');
        w.Write(true);
        w.Write(1.5);
        w.Write(2.5f);
        w.Write(7L);
        w.Write((object)"obj");
        w.Write("{0}-{1}", "a", 2);
        w.Write(new[] { 'x', 'y', 'z' });
        w.Write(new[] { 'a', 'b', 'c', 'd' }, 1, 2);
        w.WriteLine();
        w.WriteLine("{0:X4}", 255);
        w.WriteLine(3);
        w.WriteLine(false);
        w.WriteLine('q');
        w.WriteLine(9.25);
        w.WriteLineNoTabs("no tabs here");
        w.WriteLine("after no-tabs");
        w.Indent -= 2;
        w.WriteLine("}");
        w.Indent = -5;
        Console.WriteLine("indent after negative: " + w.Indent);
        w.WriteLine("}");
        w.Flush();
        Show(inner.ToString().Replace(' ', '.'));

        var custom = new StringWriter { NewLine = "\n" };
        var t = new IndentedTextWriter(custom, "\t") { Indent = 2 };
        t.NewLine = "|";
        t.WriteLine("first");
        t.Write("second");
        t.Write("third");
        t.WriteLine();
        Console.WriteLine(custom.ToString().Replace("\t", "<TAB>"));  // (no "\n" in it: its NewLine is "|")
        Console.WriteLine("newline forwarded: " + (custom.NewLine == "|" ? "yes" : "no"));
        Console.WriteLine("inner is same: " + ReferenceEquals(t.InnerWriter, custom));
        Console.WriteLine("encoding: " + t.Encoding.GetType().Name);
        t.Close();

        var span = new StringWriter { NewLine = "\n" };
        var s = new IndentedTextWriter(span, "  ");
        s.Indent = 1;
        s.Write("span".AsSpan());
        s.WriteLine(" line".AsSpan());
        s.Write(new StringBuilder("sb"));
        s.WriteLine();
        Show(span.ToString().Replace(' ', '.') + "\n");

        var asyncOut = new StringWriter { NewLine = "\n" };
        var a = new IndentedTextWriter(asyncOut, "--") { Indent = 2 };
        await a.WriteAsync("one ");
        await a.WriteAsync('2');
        await a.WriteLineAsync();
        await a.WriteLineAsync("three");
        await a.WriteLineAsync("four".AsMemory());
        await a.WriteLineNoTabsAsync("five");
        await a.WriteAsync(new[] { 'a', 'b', 'c' }, 0, 2);
        await a.WriteLineAsync(new StringBuilder("sb"));
        await a.FlushAsync();
        Show(asyncOut.ToString());
    }
}
