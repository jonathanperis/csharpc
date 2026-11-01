// .NET 11's DirectoryNotFoundException carries the directory (DirectoryPath) and its ToString() adds a
// "Directory path: '...'" line after the message, as FileNotFoundException does with "File name".
using System;
using System.IO;
using System.Linq;

string missing = Path.GetFullPath("no-such-directory-912");
string Clean(string text) => text.Replace(missing, "<missing>");

Show("Directory.GetFiles", () => Directory.GetFiles(missing));
Show("Directory.GetDirectories", () => Directory.GetDirectories(missing, "*"));
Show("Directory.EnumerateFiles", () => Directory.EnumerateFiles(missing).ToList());
Show("DirectoryInfo.GetFiles", () => new DirectoryInfo(missing).GetFiles());
Show("Directory.Delete", () => Directory.Delete(missing));
Show("File.ReadAllText", () => File.ReadAllText(Path.Combine(missing, "a.txt")));
Show("File.WriteAllText", () => File.WriteAllText(Path.Combine(missing, "a.txt"), "x"));
Show("new DirectoryNotFoundException(message)", () => throw new DirectoryNotFoundException("custom"));
Show("new DirectoryNotFoundException(message, path)", () => throw new DirectoryNotFoundException("with path", "/some/dir"));

void Show(string name, Action action)
{
    try { action(); Console.WriteLine(name + ": no exception"); }
    catch (DirectoryNotFoundException e)
    {
        var text = e.ToString();
        var at = text.IndexOf("   at ", StringComparison.Ordinal);
        Console.WriteLine($"== {name}: DirectoryPath {(e.DirectoryPath is null ? "null" : Clean(e.DirectoryPath))}");
        Console.WriteLine(Clean(at < 0 ? text : text.Substring(0, at).TrimEnd()));
    }
}
