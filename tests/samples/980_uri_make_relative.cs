// Uri.MakeRelativeUri and the obsolete Uri.MakeRelative: a relative Uri from one absolute Uri to another (same
// scheme, host and port) with ".." segments, "./", the target's query and fragment, a ':' in the first segment,
// and the case-sensitive path comparison (DOS drive paths compare ignoring case); the target itself when the
// scheme, host or port differ; the exceptions.
using System;

#pragma warning disable CS0618

static void Make(string from, string to)
{
    try
    {
        Uri relative = new Uri(from).MakeRelativeUri(new Uri(to));
        Console.WriteLine($"{from} -> {to}: [{relative}] absolute={relative.IsAbsoluteUri} original=[{relative.OriginalString}]");
    }
    catch (Exception e)
    {
        Console.WriteLine($"{from} -> {to}: {e.GetType().Name}: {e.Message}");
    }
}

string baseUri = "http://h/a/b/c";
foreach (string target in new[]
{
    "http://h/a/b/c", "http://h/a/b/d", "http://h/a/b/", "http://h/a/b", "http://h/a/", "http://h/a", "http://h/", "http://h",
    "http://h/a/b/c/d", "http://h/a/b/c/", "http://h/a/b/c?q=1", "http://h/a/b/c#f", "http://h/a/b/d?q#f", "http://h/A/b/c",
    "http://h/a/b2", "http://h/a/bb/c", "http://h/a/x:y", "http://h/a/b/x:y", "http://h/b", "http://h/a/b/d/e/f/g",
    "https://h/a/b/c", "http://H:81/a", "http://other/a/b/c", "http://h:80/a/b/x", "http://h/a/b/c?", "http://h/a/b/c#",
})
    Make(baseUri, target);

Make("http://h/a", "http://h/a?");
Make("http://h/a?x#y", "http://h/a?x#y");
Make("http://h/a?x", "http://h/a/b?x");
Make("http://h/", "http://h/");
Make("http://h/a/", "http://h/a/");
Make("http://h/a/", "http://h/a/?q");
Make("http://h/a", "http://h/a/");
Make("http://h/a/b/c/d", "http://h/a/x");
Make("http://h/abc/def", "http://h/abd/def");
Make("http://h/abc", "http://h/");
Make("http://h/a;x/b", "http://h/a;x/c;y");
Make("http://h/a/b/x/../y", "http://h/a/./b/z");
Make("http://h/a/b/c/d", "http://h/a/b/../../x");
Make("http://1.2.3.4/a/b", "http://1.2.3.4/a/c");
Make("http://h:8080/a", "http://h:8080/b");
Make("http://h:8080/a", "http://h:8081/b");
Make("ftp://h/a/b", "ftp://h/a/c");
Make("ws://h/a/b", "ws://h/a/c");
Make("http://h/a/b", "ws://h/a/c");
Make("C:\\a\\b.txt", "C:\\a\\c.txt");
Make("C:\\a\\b.txt", "C:\\A\\c.txt");
Make("file:///C:/a/b.txt", "file:///c:/A/x/c.txt");
Make("file:///C:/a/b.txt", "file:///D:/a/c.txt");

Uri from = new("http://h/a/b");
Console.WriteLine(from.MakeRelative(new Uri("http://h/a/c")));
Console.WriteLine(from.MakeRelative(new Uri("http://x/a/c?q#f")));
Console.WriteLine(from.MakeRelative(new Uri("http://h/a/c?q#f")));
Console.WriteLine(from.MakeRelative(new Uri("http://h/a/b")).Length);

foreach (Action action in new Action[]
{
    () => from.MakeRelativeUri(null!),
    () => from.MakeRelative(null!),
    () => from.MakeRelativeUri(new Uri("x/y", UriKind.Relative)),
    () => new Uri("x/y", UriKind.Relative).MakeRelativeUri(from),
    () => new Uri("x/y", UriKind.Relative).MakeRelative(from),
})
{
    try { action(); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

Uri sameDocument = from.MakeRelativeUri(new Uri("http://h/a/b"));
Console.WriteLine($"[{sameDocument}] {sameDocument.IsAbsoluteUri} {sameDocument.OriginalString.Length} {sameDocument == new Uri("", UriKind.Relative)}");
