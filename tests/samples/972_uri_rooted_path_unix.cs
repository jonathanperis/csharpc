// A rooted path without a drive ("/rel") is a relative Uri on Unix (an implicit file path, so absolute, on Windows):
// Uri.TryCreate with UriKind.RelativeOrAbsolute, and the same text read by the JSON Uri converter.
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

foreach (string s in new[] { "/rel", "/dir/x?q=1", "rel/x", "https://example.com/x" })
{
    bool ok = Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out Uri? uri);
    Console.WriteLine(s + ": " + ok + " " + uri!.IsAbsoluteUri + " " + uri.OriginalString);
}

Console.WriteLine(JsonSerializer.Deserialize("\"/rel\"", Ctx.Default.Uri)!.IsAbsoluteUri);

[JsonSerializable(typeof(Uri))]
partial class Ctx : JsonSerializerContext;
