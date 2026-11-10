// Paths with spaces as URIs: a relative reference keeps them as they are (OriginalString, ToString); an implicit file
// path escapes them to %20 in AbsoluteUri/AbsolutePath and keeps them in LocalPath and ToString(). XmlReader.Create(path)
// goes through both (XmlUrlResolver: a relative Uri, then Path.GetFullPath).
using System;
using System.IO;
using System.Xml;

static class Program
{
    static void Main()
    {
        var relative = new Uri("dir with space/p.xml", UriKind.RelativeOrAbsolute);
        Console.WriteLine($"{relative.IsAbsoluteUri} [{relative.OriginalString}] [{relative}]");
        Console.WriteLine(Uri.TryCreate("a b/c", UriKind.Relative, out var tried) ? $"relative [{tried}]" : "not relative");

        var root = Path.Combine(Path.GetTempPath(), "csharpc uri sample " + Environment.ProcessId);
        var dir = Path.Combine(root, "dir with space");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "p.xml");
            File.WriteAllText(file, "<root a=\"1\"><x>hi</x></root>");

            var absolute = new Uri(file);
            Console.WriteLine(absolute.IsAbsoluteUri + " " + absolute.IsFile + " " + absolute.Scheme);
            Console.WriteLine(absolute.AbsoluteUri.EndsWith("/dir%20with%20space/p.xml", StringComparison.Ordinal));
            Console.WriteLine(absolute.AbsolutePath.EndsWith("/dir%20with%20space/p.xml", StringComparison.Ordinal));
            Console.WriteLine(absolute.LocalPath == file);
            Console.WriteLine(absolute.ToString().EndsWith("/dir with space/p.xml", StringComparison.Ordinal));
            Console.WriteLine(absolute.Segments[^2] + absolute.Segments[^1]);

            Directory.SetCurrentDirectory(root);
            using var reader = XmlReader.Create(Path.Combine("dir with space", "p.xml"));
            reader.MoveToContent();
            Console.WriteLine($"{reader.Name} {reader.GetAttribute("a")} {reader.ReadInnerXml()}");
            Console.WriteLine(reader.BaseURI.EndsWith("/dir with space/p.xml", StringComparison.Ordinal));
        }
        finally
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Directory.Delete(root, true);
        }
    }
}
