// A rethrow (`throw;`) from a handler whose method is small enough to be inlined into its callers: .NET lists every caller once,
// above the rethrowing frame. csharpc at -O2 inlines the chain into one function; the search for the rethrown exception must go on
// with the callers' frames past the handler.
using System;
using System.IO;

class Program
{
    static int s_depth;

    static void Thrower(string what) => throw new InvalidOperationException(what);

    static void Inner(string what)
    {
        try { Thrower(what); }
        catch (InvalidOperationException) when (what.Length > 0) { s_depth++; throw; }
    }

    static void Middle(string what) => Inner(what);

    static void Outer(string what) { Middle(what); s_depth--; }

    static void InnerFinally(string what)
    {
        try { Thrower(what); }
        finally { s_depth++; }
    }

    static void OuterFinally(string what) { InnerFinally(what); s_depth--; }

    static void InnerCatchAll(string what)
    {
        try { Thrower(what); }
        catch { s_depth++; throw; }
    }

    static void OuterCatchAll(string what) { InnerCatchAll(what); s_depth--; }

    static void Main()
    {
        Show("filtered rethrow", () => Outer("a"));
        Show("finally", () => OuterFinally("b"));
        Show("catch-all rethrow", () => OuterCatchAll("c"));
        Console.WriteLine(s_depth);
    }

    static void Show(string name, Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            Console.WriteLine("== " + name + ": " + e.Message);
            foreach (var line in e.StackTrace!.Split(Environment.NewLine))
            {
                var at = line.IndexOf(" in ", StringComparison.Ordinal);
                Console.WriteLine(at < 0 ? line : line[..at] + " in " + Path.GetFileName(line[(at + 4)..]));
            }
        }
    }
}
