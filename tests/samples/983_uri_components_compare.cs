// Uri.GetComponents over every combination of the component flags (and KeepDelimiter, StrongPort, NormalizedHost,
// SerializationInfoString) in each UriFormat, Uri.GetLeftPart, Uri.Compare, Uri.IsWellFormedOriginalString /
// IsWellFormedUriString, Uri.TryFormat, Uri.CheckHostName and the argument checks of GetComponents.
using System;

static void Try(string label, Func<object> f)
{
    try { Console.WriteLine(label + " => " + f()); }
    catch (Exception e) { Console.WriteLine(label + " !! " + e.GetType().Name + ": " + e.Message); }
}

string[] texts =
{
    "http://Example.COM:8080/a/b/../c/./d.html?x=1&y=2#frag", "https://h/", "http://1.2.3.4:80/p?", "ws://h/a#", "http://h",
    "wss://h:444/a//b/", "ftp://h:21/x;y#z",
};
int[] allBits = { 0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x40000000 };
int[] someBits = { 0x1, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80 };
foreach (string text in texts)
{
    int[] bits = text.StartsWith("http://Example") ? allBits : someBits;
    Uri u = new(text);
    Console.WriteLine($"{text} | {u} | {u.AbsoluteUri} | {u.AbsolutePath} | {u.PathAndQuery} | {u.Authority} | [{string.Join(",", u.Segments)}]");
    foreach (UriPartial part in Enum.GetValues<UriPartial>())
        Try(text + " left " + part, () => u.GetLeftPart(part));
    Try(text + " left 9", () => u.GetLeftPart((UriPartial)9));
    for (int mask = 0; mask < 1 << bits.Length; mask++)
    {
        int components = 0;
        for (int i = 0; i < bits.Length; i++)
            if ((mask & (1 << i)) != 0) components |= bits[i];
        string escaped = u.GetComponents((UriComponents)components, UriFormat.UriEscaped);
        bool same = escaped == u.GetComponents((UriComponents)components, UriFormat.Unescaped)
            && escaped == u.GetComponents((UriComponents)components, UriFormat.SafeUnescaped);
        Console.WriteLine($"{components:X} [{escaped}]{(same ? "" : " formats differ")}");
    }
    Try(text + " ser", () => u.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped));
    Try(text + " ser+", () => u.GetComponents(UriComponents.SerializationInfoString | UriComponents.Scheme, UriFormat.UriEscaped));
    Try(text + " format 0", () => u.GetComponents(UriComponents.Path, (UriFormat)0));
    Try(text + " format 0 scheme", () => u.GetComponents(UriComponents.Scheme, (UriFormat)0));
    Try(text + " format 4", () => u.GetComponents(UriComponents.Path, (UriFormat)4));
    Try(text + " format 0 port", () => u.GetComponents(UriComponents.Port, (UriFormat)0));
    Try(text + " wf", () => u.IsWellFormedOriginalString());
    Try(text + " format", () => { char[] buffer = new char[200]; bool ok = u.TryFormat(buffer, out int n); return ok + " " + new string(buffer, 0, n); });
    Try(text + " format small", () => u.TryFormat(new char[5], out int n) + " " + n);
}

Uri relative = new("a/b?c", UriKind.Relative);
Try("relative ser", () => relative.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped));
Try("relative path", () => relative.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
Try("relative left", () => relative.GetLeftPart(UriPartial.Path));
Try("relative format", () => relative.TryFormat(new char[20], out int n) + " " + n);

string[] compared = { "http://h/a", "http://H/b", "http://h/a?q", "http://h/a#f", "https://h/a", "ftp://h/a" };
UriComponents[] parts = { UriComponents.HttpRequestUrl, UriComponents.AbsoluteUri, UriComponents.Scheme, UriComponents.Path, UriComponents.Fragment, UriComponents.SchemeAndServer };
foreach (string x in compared)
    foreach (string y in compared)
    {
        string row = "";
        foreach (UriComponents p in parts)
            row += " " + Math.Sign(Uri.Compare(new Uri(x), new Uri(y), p, UriFormat.UriEscaped, StringComparison.Ordinal));
        Console.WriteLine($"compare {x} {y}:{row}");
    }
Console.WriteLine(Uri.Compare(null, null, UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal));
Console.WriteLine(Uri.Compare(null, new Uri("http://h/"), UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal));
Console.WriteLine(Uri.Compare(new Uri("http://h/"), null, UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal));
Console.WriteLine(Uri.Compare(new Uri("a", UriKind.Relative), new Uri("http://h/"), UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal));
Console.WriteLine(Uri.Compare(new Uri("http://h/"), new Uri("a", UriKind.Relative), UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal));
Console.WriteLine(Math.Sign(Uri.Compare(new Uri("a", UriKind.Relative), new Uri("b", UriKind.Relative), UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal)));
Console.WriteLine(Uri.Compare(new Uri("http://h/A"), new Uri("http://h/a"), UriComponents.Path, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase));
Console.WriteLine(Math.Sign(Uri.Compare(new Uri("http://h/A"), new Uri("http://h/a"), UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal)));

foreach (string text in new[] { "", "a", "a/b", "?q", "#f", "../x", "./y", "x?y", "a/b?c#d", "http://h/a", "HTTP://H:80/A?b#c" })
    Console.WriteLine($"wf [{text}] {Uri.IsWellFormedUriString(text, UriKind.Relative)} {Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute)} {Uri.IsWellFormedUriString(text, UriKind.Absolute)}");
Console.WriteLine(Uri.IsWellFormedUriString(null, UriKind.Absolute));
Try("kind", () => Uri.IsWellFormedUriString("http://h/a", (UriKind)7));
Try("try kind", () => Uri.TryCreate("http://h/a", (UriKind)7, out _));
Try("ctor kind", () => new Uri("http://h/a", (UriKind)7));

foreach (string? host in new[] { "example.com", "EXAMPLE.com", "a-b.c", "1.2.3.4", "localhost", "", null })
    Try("host [" + host + "]", () => Uri.CheckHostName(host));

Console.WriteLine(new Uri("", UriKind.Relative) + "|" + new Uri("", UriKind.RelativeOrAbsolute).IsAbsoluteUri);
Console.WriteLine(Uri.TryCreate("", UriKind.Absolute, out _));
Console.WriteLine(Uri.TryCreate("", UriKind.Relative, out Uri? empty) + " " + empty!.OriginalString.Length);
Console.WriteLine(Uri.TryCreate("a#b", UriKind.Relative, out Uri? withFragment) + " " + withFragment);
