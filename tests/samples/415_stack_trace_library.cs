// Exceptions thrown inside .NET's library: the trace starts with the library frames .NET 11 lists
// for these common members (dictionary and list indexers, LINQ First/Last, Stack/Queue), then the
// program's own frames.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class Program
{
    static void Main()
    {
        Show("dictionary", () => { var d = new Dictionary<string, int>(); Console.WriteLine(d["missing"]); });
        Show("sorted dictionary", () => { var d = new SortedDictionary<int, string>(); Console.WriteLine(d[7]); });
        Show("list get", () => { var l = new List<int> { 1 }; Console.WriteLine(l[3]); });
        Show("list set", () => { var l = new List<string>(); l[0] = "x"; });
        Show("list remove", () => new List<int>().RemoveAt(2));
        Show("first", () => Console.WriteLine(new List<int>().First()));
        Show("first predicate", () => Console.WriteLine(new[] { 1, 2 }.First(x => x > 5)));
        Show("last", () => Console.WriteLine(Enumerable.Empty<string>().Last()));
        Show("stack", () => new Stack<int>().Pop());
        Show("queue", () => new Queue<int>().Dequeue());
        Show("through a helper", Helper);
    }

    static void Helper()
    {
        var scores = new Dictionary<string, int> { ["ann"] = 3 };
        Console.WriteLine(Lookup(scores, "bob"));
    }

    static int Lookup(Dictionary<string, int> map, string key) => map[key];

    static void Show(string name, Action action)
    {
        Console.WriteLine("== " + name);
        try
        {
            action();
        }
        catch (Exception e)
        {
            Console.Write(Clean(e.ToString()));
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
