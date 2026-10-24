// Array.Copy's argument checks: the trace starts with the library frames .NET 11 lists on Unix
// (the throw helper and Array.CopyImpl; on Windows .NET also lists Array.Copy itself).
using System;
using System.Text;

class Program
{
    static void Main()
    {
        Show("negative length", () => Array.Copy(new int[2], new int[2], -1));
        Show("negative index", () => Array.Copy(new int[2], -1, new int[2], 0, 1));
        Show("too long", () => Array.Copy(new int[2], new int[1], 2));
        Show("long length", () => Array.Copy(new string[2], new string[2], -1L));
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
            Console.Write(Clean(e.ToString()));
        }
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
