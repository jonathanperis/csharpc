// System.Uri for simple absolute URIs, Escape/UnescapeDataString.
using System;

static class UriExtensions
{
    public static void Deconstruct(this Uri uri, out string scheme, out string host, out int port)
    {
        scheme = uri.Scheme;
        host = uri.Host;
        port = uri.Port;
    }
}

class Program
{
    static void Show(string text)
    {
        try
        {
            var u = new Uri(text);
            Console.WriteLine($"{u} | {u.AbsoluteUri} | {u.OriginalString} | {u.Scheme} | {u.Host} | {u.Port} | {u.IsDefaultPort} | {u.Authority}");
            Console.WriteLine($"  path={u.AbsolutePath} query={u.Query} fragment={u.Fragment} pq={u.PathAndQuery} local={u.LocalPath} type={u.HostNameType} abs={u.IsAbsoluteUri} file={u.IsFile}");
            Console.WriteLine($"  segments=[{string.Join(",", u.Segments)}] left={u.GetLeftPart(UriPartial.Authority)} {u.GetLeftPart(UriPartial.Path)}");
        }
        catch (UriFormatException e) { Console.WriteLine($"{text}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var docsSite = new Uri("https://learn.microsoft.com:443/dotnet/csharp/");
        var (scheme, host, port) = docsSite;
        Console.WriteLine($"{scheme}://{host}:{port}");
        Uri url = new("https://learn.microsoft.com/");
        Console.WriteLine(string.Equals(url.Scheme, "https", StringComparison.OrdinalIgnoreCase));

        Show("http://Example.COM");
        Show("HTTP://example.com:8080/a/b.html?x=1&y=two#frag");
        Show("https://192.168.1.10:0443/api/v1/items;matrix=1?q=a:b@c/d?e");
        Show("ftp://files.example.org/pub/");
        Show("wss://chat.example.net/socket");
        Show("http://localhost:65535/");
        Show("");
        Show("not a uri");
        Show("example.com/path");

        var a = new Uri("http://Example.com/A?b#one");
        var b = new Uri("HTTP://example.COM:80/A?b#two");
        Console.WriteLine($"{a == b} {a.Equals(b)} {a.Equals("http://example.com/A?b")} {a != new Uri("http://example.com/a?b")} {a.Equals(null)}");
        Console.WriteLine(Uri.TryCreate("https://ok.example/", UriKind.Absolute, out var ok) + " " + ok);
        Console.WriteLine(Uri.TryCreate("no scheme here", UriKind.Absolute, out var bad) + " " + (bad is null));
        Console.WriteLine($"{Uri.UriSchemeHttps} {Uri.SchemeDelimiter} {Uri.CheckSchemeName("web+app")} {Uri.CheckSchemeName("1abc")} {Uri.IsHexDigit('F')}");

        Console.WriteLine(Uri.EscapeDataString("a b&c=d/é?~_.-!*'()"));
        Console.WriteLine(Uri.EscapeDataString("😀 \ud800x"));
        Console.WriteLine(Uri.UnescapeDataString("a%20b%26c%3Dd%2F%C3%A9%3F%7E%zz%4"));
        Console.WriteLine(Uri.UnescapeDataString(Uri.EscapeDataString("round trip ✓ 😀")));
        try { new Uri((string)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }
}
