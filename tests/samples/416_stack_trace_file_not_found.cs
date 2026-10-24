// FileNotFoundException from File.OpenText / ReadAllText / ReadAllLines: ToString() has the
// "File name: '...'" line and, on Unix, the library frames .NET 11 lists for a failed open, then the
// program's own frames. Paths are printed relative to the working directory.
using System;
using System.IO;
using System.Text;

class Program
{
    static void Main()
    {
        Show("OpenText", () => File.OpenText("no-such-file.txt"));
        Show("ReadAllText", () => File.ReadAllText("no-such-file.txt"));
        Show("ReadAllLines", () => File.ReadAllLines("no-such-file.txt"));
        Show("FileName", () =>
        {
            try { File.ReadAllText("missing.txt"); }
            catch (FileNotFoundException e) { Console.WriteLine(Path.GetFileName(e.FileName)); throw; }
        });
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

    /// <summary>The working directory as "." and source paths trimmed to the file name, one line each.</summary>
    static string Clean(string text)
    {
        text = text.Replace(Directory.GetCurrentDirectory(), ".");
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
