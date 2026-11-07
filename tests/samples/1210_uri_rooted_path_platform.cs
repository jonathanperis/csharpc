// A rooted path without a drive ("/absolute/path") is read per platform: on Unix it is an implicit file path
// (absolute for UriKind.Absolute, relative for Relative and RelativeOrAbsolute), on Windows only "C:\dir" and UNC
// paths are, so there UriKind.Absolute is a UriFormatException. Both resolve against a base address alike, which is
// what HttpClient.BaseAddress does with a request path. The Windows expectation is recorded live (real .NET there).
using System;

static string Describe(Uri uri)
{
    if (!uri.IsAbsoluteUri) return "relative [" + uri.OriginalString + "]";
    return "absolute " + uri.Scheme + " " + uri.AbsoluteUri + " | " + uri.AbsolutePath + " | " + uri.LocalPath + " | " + uri.IsFile;
}

foreach (string text in new[] { "/absolute/path", "/", "/dir/x?q=1" })
{
    foreach (UriKind kind in new[] { UriKind.Absolute, UriKind.Relative, UriKind.RelativeOrAbsolute })
    {
        if (kind == UriKind.Absolute && text.Contains('?')) continue; // Unix escapes the '?' of a file path: not the point here
        string result;
        try { result = Describe(new Uri(text, kind)); }
        catch (Exception e) { result = e.GetType().Name + ": " + e.Message; }
        bool created = Uri.TryCreate(text, kind, out Uri? parsed);
        Console.WriteLine($"new Uri({text}, {kind}): {result} | TryCreate {created}{(created ? " " + parsed!.IsAbsoluteUri : "")}");
    }
}

var baseUri = new Uri("http://example.com/api/v1/");
foreach (string text in new[] { "/absolute/path", "items/1", "/dir/x?q=1" })
    Console.WriteLine(text + " -> " + new Uri(baseUri, text) + " | " + new Uri(baseUri, new Uri(text, UriKind.RelativeOrAbsolute)));
