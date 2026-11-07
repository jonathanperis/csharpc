// Resolving a relative reference against a base Uri: new Uri(Uri, string), new Uri(Uri, Uri), Uri.TryCreate(Uri, string/Uri),
// over http, https, ftp and ws bases (fragment-only, query-only, path, dot-segment and absolute references, the
// resulting AbsoluteUri and OriginalString), the dot segments an absolute Uri drops from its AbsoluteUri, the
// argument checks, Uri.IsBaseOf and the obsolete Uri(string, bool).
using System;

#pragma warning disable CS0618

string[] bases =
{
    "http://Host:8080/a/b/c?x=1#frag", "https://h/", "http://h", "http://h/a/b/", "ftp://h/a/b#f", "ws://h:81/a",
};
string[] relatives =
{
    "", "#g", "?y", "g", "./g", "g/", "../g", "../../g", "../../../g", "../../../../g", "g?y", "g#s", "g?y#s", ";x", "g;x",
    "g;x?y#s", ".", "./", "..", "../", "./../g", "./g/.", "g/./h", "g/../h", "g/..", "g/../", "x/./../y", "g.", ".g", "..g",
    "g..", "...", "a//b", "?", "#", "g?y/./x", "g#s/../x", "http://x/y", "https://x", "http://x/../y", "ftp://h2/p",
};

foreach (string b in bases)
{
    Uri baseUri = new(b);
    Console.WriteLine("== " + b);
    foreach (string r in relatives)
    {
        if (b.StartsWith("ftp:") && r.Contains('?')) continue; // ftp has no query: .NET escapes the '?'
        Uri resolved = new(baseUri, r);
        bool ok = Uri.TryCreate(baseUri, r, out Uri? tried);
        bool okUri = Uri.TryCreate(baseUri, new Uri(r, UriKind.RelativeOrAbsolute), out Uri? triedUri);
        bool viaUri = new Uri(baseUri, new Uri(r, UriKind.RelativeOrAbsolute)) == resolved;
        Console.WriteLine($"[{r}] {resolved.AbsoluteUri} | {resolved.OriginalString} | {ok} {tried == resolved} {okUri} {triedUri == resolved} {viaUri}");
    }
}

foreach (string text in new[]
{
    "http://h/a/../b", "http://h/a/./b", "http://h/a/..", "http://h/..", "http://h/.", "http://h/./a", "http://h/a/b/../../../c",
    "http://h/a//../b", "http://h/a/b//../../c", "http://h/..a/../b", "http://h/a/....", "http://h/a/..../b", "http://h/a./b../..c/.d/e...",
    "ftp://h/a/../b#x?y", "ws://h/a/b/../c", "http://h/a/..;x/b/..", "http://h/a/b/c/../../..", "http://h/../../a",
})
{
    Uri u = new(text);
    Console.WriteLine($"{text} | {u} | {u.AbsolutePath} | {u.PathAndQuery} | [{string.Join(",", u.Segments)}] | {u == new Uri(u.AbsoluteUri)}");
}
Console.WriteLine(new Uri("http://h/a/b/../c") == new Uri("http://h/a/c"));
Console.WriteLine(new Uri("http://h/a/b/../c").GetHashCode() == new Uri("http://h/a/c").GetHashCode());

Uri b0 = new("http://h/a/b/");
Console.WriteLine(new Uri(b0, (string)null!));
foreach (Action action in new Action[]
{
    () => new Uri((Uri)null!, "a"),
    () => new Uri(b0, (Uri)null!),
    () => new Uri(new Uri("a/b", UriKind.Relative), "a"),
    () => new Uri(new Uri("a/b", UriKind.Relative), new Uri("c", UriKind.Relative)),
    () => new Uri(new Uri("a/b", UriKind.Relative), "a", true),
})
{
    try { action(); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}
Console.WriteLine(Uri.TryCreate(b0, (string)null!, out _));
Console.WriteLine(Uri.TryCreate((Uri)null!, "x", out _));
Console.WriteLine(Uri.TryCreate(new Uri("a", UriKind.Relative), "x", out _));
Console.WriteLine(Uri.TryCreate(b0, (Uri)null!, out _));
Console.WriteLine(Uri.TryCreate(b0, "http://other/z", out Uri? other) + " " + other);

string[] linkBases = { "http://h/a/b/c", "http://h/a/b/", "http://h/a/b", "http://h/a/", "http://h/", "http://h", "http://H:80/A/B/", "https://h/a/b/", "http://h/a/b/c?q#f", "http://h:81/a/b/" };
foreach (string x in linkBases)
{
    Console.WriteLine(x + ": " + string.Join(" ", Array.ConvertAll(linkBases, y => new Uri(x).IsBaseOf(new Uri(y)) ? "1" : "0")));
}
Console.WriteLine(new Uri("http://h/a/b/").IsBaseOf(new Uri("c/d", UriKind.Relative)));
Console.WriteLine(new Uri("http://h/a/b/").IsBaseOf(new Uri("../../d", UriKind.Relative)));
Console.WriteLine(new Uri("http://h/a/b/").IsBaseOf(new Uri("../d", UriKind.Relative)));
Console.WriteLine(new Uri("a/b", UriKind.Relative).IsBaseOf(new Uri("http://h/a/b")));
try { new Uri("http://h/a/b/").IsBaseOf(null!); }
catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

Console.WriteLine(new Uri("http://h/a", true).UserEscaped + " " + new Uri("http://h/a", false).UserEscaped + " " + new Uri("http://h/a").UserEscaped);
Console.WriteLine(new Uri(b0, "b", true).UserEscaped + " " + new Uri(b0, "b").UserEscaped);
