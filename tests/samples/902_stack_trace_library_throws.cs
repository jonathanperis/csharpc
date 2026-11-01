// Exceptions thrown inside .NET's library list the library frames .NET 11 shows for them before the
// program's own: string.Substring, the ArgumentException throw helpers, Dictionary.Add, List<T>'s
// enumerator and Insert, Enumerable.Single ([StackTraceHidden] helpers such as System.ThrowHelper's
// are not listed).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class Program
{
    static void Main()
    {
        Show("Substring(start)", () => Console.WriteLine("abc".Substring(5)));
        Show("Substring(start, length)", () => Console.WriteLine("abc".Substring(1, 5)));
        Show("ThrowIfNull", () => ArgumentNullException.ThrowIfNull((object?)null, "value"));
        Show("ThrowIfNullOrEmpty", () => ArgumentException.ThrowIfNullOrEmpty("", "name"));
        Show("ThrowIfNegative", () => ArgumentOutOfRangeException.ThrowIfNegative(-2, "count"));
        Show("Dictionary.Add", () => new Dictionary<string, int> { ["a"] = 1 }.Add("a", 2));
        Show("collection initializer", () => Console.WriteLine(new Dictionary<int, string> { { 1, "x" }, { 1, "y" } }.Count));
        Show("List<T> foreach", () => { var list = new List<int> { 1, 2 }; foreach (var x in list) list.Add(x); });
        Show("List<T> as IEnumerable<T>", () => { var list = new List<int> { 1, 2 }; foreach (var x in (IEnumerable<int>)list) list.Remove(x); });
        Show("List<T>.Insert", () => new List<int>().Insert(3, 1));
        Show("Single, two elements", () => Console.WriteLine(new[] { 1, 2 }.Single()));
        Show("Single, empty", () => Console.WriteLine(new List<int>().Single()));
        Show("through a helper", () => Console.WriteLine(Middle("hello", 9)));
    }

    static string Middle(string s, int start) => s.Substring(start, 1);

    static void Show(string name, Action action)
    {
        Console.WriteLine("== " + name);
        try { action(); }
        catch (Exception e) { Console.Write(Clean(e.ToString())); }
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
