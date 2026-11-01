using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IlStackTracesLib;
using IlStackTracesNoPdbLib;

class Program
{
    static async Task Main()
    {
        Show("recursion", () => Throwers.Recurse(2));
        Show("hidden frames", Throwers.ThroughHiddenFrames);
        Show("generic nested types", () => { int x = 3; Throwers.Generic("v", ref x); });
        Show("lambda", () => Throwers.Lambda(4));
        Show("local function", () => Throwers.Local("s"));
        Show("iterator", () => { foreach (var i in Throwers.Iterate(3)) Console.WriteLine(i); });
        Show("callback", () => Throwers.Callback(() => Throwers.Throw("callback")));
        Show("ExceptionDispatchInfo", Throwers.Edi);
        Show("inner exception", Throwers.Wrapped);
        Show("finally", Throwers.Finally);
        Show("constructor", () => new Point(-1));
        Show("indexer", () => Console.WriteLine(new Point(1)[1]));
        Show("operator", () => Console.WriteLine((new Point(1) + new Point(2)).X));
        Show("library without PDB, callback", () => new Driver("db").Convert<int>(s => throw new FormatException(s)));
        await ShowAsync("await chain", Throwers.OuterAsync);
        await ShowAsync("before the first await", () => Throwers.EarlyAsync(1));
        await ShowAsync("await in catch", () => Throwers.AwaitInCatch(true));
        await ShowAsync("TaskCompletionSource", Throwers.AwaitCompletionSource);
        await ShowAsync("library without PDB, async", () => new Driver("db").OpenAsync(true));
        Show("GetAwaiter().GetResult()", () => Throwers.OuterAsync().GetAwaiter().GetResult());
        Show("Wait()", () => Throwers.OuterAsync().Wait());
    }

    static void Show(string name, Action a)
    {
        Console.WriteLine("== " + name);
        try { a(); } catch (Exception e) { Console.Write(Clean(e.ToString())); }
    }

    static async Task ShowAsync(string name, Func<Task> a)
    {
        Console.WriteLine("== " + name);
        try { await a(); } catch (Exception e) { Console.Write(Clean(e.ToString())); }
    }

    /// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
    static string Clean(string text)
    {
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
