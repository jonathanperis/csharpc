using System;
using System.IO;

string[] samples = { "", "/", "a", "/a", "a/", "/a/b/", "a/b.c", "/a/b.c.d", ".bashrc", "dir.d/file", "a//b", "../x/./y", "/x/../../y", "file.", "/a/b/c.tar.gz" };
foreach (var s in samples)
{
    Console.WriteLine($"'{s}': name='{Path.GetFileName(s)}' stem='{Path.GetFileNameWithoutExtension(s)}' ext='{Path.GetExtension(s)}' has={Path.HasExtension(s)} dir='{Path.GetDirectoryName(s) ?? "<null>"}' rooted={Path.IsPathRooted(s)} root='{Path.GetPathRoot(s)}' ends={Path.EndsInDirectorySeparator(s)}");
}
Console.WriteLine(Path.Combine("a", "b", "c"));
Console.WriteLine(Path.Combine("a/", "b"));
Console.WriteLine(Path.Combine("a", "/b"));
Console.WriteLine(Path.Combine("", "b"));
Console.WriteLine(Path.Combine("a", ""));
Console.WriteLine(Path.Combine(new[] { "x", "y", "/z", "w" }));
Console.WriteLine(Path.Join("a", "/b"));
Console.WriteLine(Path.Join("a/", "/b"));
Console.WriteLine(Path.Join("", "b", null, "c"));
Console.WriteLine(Path.ChangeExtension("a/b.txt", ".md"));
Console.WriteLine(Path.ChangeExtension("a/b.txt", "md"));
Console.WriteLine(Path.ChangeExtension("a/b.txt", null));
Console.WriteLine(Path.ChangeExtension("a/b", ""));
Console.WriteLine(Path.GetFullPath("/a/b/../c/./d/"));
Console.WriteLine(Path.GetFullPath("/a//b///c"));
Console.WriteLine(Path.GetFullPath("/../.."));
Console.WriteLine(Path.GetFullPath("x/../y", "/base/dir"));
Console.WriteLine(Path.GetRelativePath("/a/b/c", "/a/d/e"));
Console.WriteLine(Path.GetRelativePath("/a/b", "/a/b"));
Console.WriteLine(Path.GetRelativePath("/a/b", "/a/b/c/d"));
Console.WriteLine(Path.GetRelativePath("/a/b/c", "/a"));
Console.WriteLine(Path.TrimEndingDirectorySeparator("/a/b/"));
Console.WriteLine(Path.TrimEndingDirectorySeparator("/"));
Console.WriteLine(Path.DirectorySeparatorChar + " " + Path.AltDirectorySeparatorChar + " " + Path.PathSeparator);
Console.WriteLine(Path.GetInvalidFileNameChars().Length + " " + Path.GetInvalidPathChars().Length);
Console.WriteLine(Path.IsPathFullyQualified("/x") + " " + Path.IsPathFullyQualified("x"));
var r = Path.GetRandomFileName();
Console.WriteLine(r.Length + " " + r[8]);
Console.WriteLine(Path.GetTempPath().EndsWith("/"));
try { Path.GetFullPath(""); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
try { Path.Combine("a", null!); } catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name); }
