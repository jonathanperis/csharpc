// Unix-only Uri behaviour around file paths: MakeRelativeUri between file URIs (the path is case sensitive unless
// it is a DOS path), the path members of file and DOS-path URIs (AbsolutePath and PathAndQuery drop the slash
// before a drive letter, GetLeftPart(Authority) of a DOS path is empty), IsBaseOf, "/rooted" paths as relative
// references (UriKind.Relative and RelativeOrAbsolute) and as implicit file paths (UriKind.Absolute), and
// root-relative references against an http base.
using System;

static void Make(string from, string to)
{
    Uri relative = new Uri(from).MakeRelativeUri(new Uri(to));
    Console.WriteLine($"{from} -> {to}: [{relative}] absolute={relative.IsAbsoluteUri}");
}

Make("file:///tmp/a/b.txt", "file:///tmp/a/c.txt");
Make("file:///tmp/a/b.txt", "file:///TMP/a/c.txt");
Make("file:///tmp/a/b.txt", "file:///tmp/x/c.txt");
Make("file:///tmp/a/b.txt", "file:///tmp/a/");
Make("file:///tmp/a/b.txt", "http://h/a/c");
Make("/tmp/a/b", "/tmp/a/c");
Make("/tmp/a/b", "file:///tmp/a/b/c");

foreach (string text in new[] { "file:///tmp/x/y", "/tmp/x", "file:///C:/dir/x.txt", "C:\\dir\\x.txt", "C:/dir/x" })
{
    Uri u = new(text);
    Console.WriteLine($"{text} | {u} | {u.AbsolutePath} | {u.PathAndQuery} | {u.LocalPath} | [{u.GetLeftPart(UriPartial.Scheme)}] [{u.GetLeftPart(UriPartial.Authority)}] [{u.GetLeftPart(UriPartial.Path)}] | {u.IsWellFormedOriginalString()}");
}

Console.WriteLine(new Uri("file:///tmp/a/").IsBaseOf(new Uri("file:///tmp/a/b/c")));
Console.WriteLine(new Uri("file:///tmp/a/").IsBaseOf(new Uri("file:///TMP/a/b")));
Console.WriteLine(new Uri("file:///C:/A/").IsBaseOf(new Uri("file:///c:/a/b")));

foreach (string text in new[] { "/tmp/x", "//h/x", "/dir/x?q=1" })
{
    Console.WriteLine($"{text}: {Uri.TryCreate(text, UriKind.Relative, out Uri? rel)} {rel?.IsAbsoluteUri} {Uri.IsWellFormedUriString(text, UriKind.Relative)} {Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute)}");
}
Console.WriteLine(new Uri("/tmp/x", UriKind.Relative).IsAbsoluteUri);
Console.WriteLine(Uri.IsWellFormedUriString("/tmp/x", UriKind.Absolute));

Uri http = new("http://Host:8080/a/b/c?x=1#frag");
foreach (string rel in new[] { "/g", "//g", "//g/x?y", "//g:99/z", "/./g", "/../g", "/a/b/../../x" })
{
    Uri resolved = new(http, rel);
    Console.WriteLine($"{rel}: {resolved} | {resolved.OriginalString}");
}
