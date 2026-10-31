using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

// ASP.NET Core's HTTP abstractions without a server, compiled from the shared framework's IL: paths
// and query strings (escaping and unescaping), headers, media types and string values.
var path = new PathString("/api").Add("/items");
Console.WriteLine(path);
Console.WriteLine(path.StartsWithSegments("/api", out var rest) + " " + rest);
Console.WriteLine(new PathString("/a b/ä€/%").ToUriComponent());
Console.WriteLine(PathString.FromUriComponent("/x%20y/%C3%A4%2F").Value);
Console.WriteLine(QueryString.Create("q", "a b&c").Add("page", "2") + " " + QueryString.FromUriComponent("?x=1").HasValue);
Console.WriteLine(new PathString("/A/b").Equals(new PathString("/a/B")) + " " + (new PathString("/a") + new PathString("/b")));

var headers = new HeaderDictionary { ["Content-Type"] = "text/plain" };
headers.Append("X-Values", "1");
headers.Append("X-Values", "2");
foreach (var (name, values) in headers.OrderBy(h => h.Key, StringComparer.Ordinal))
    Console.WriteLine($"{name}: {values} ({values.Count})");
Console.WriteLine(headers.ContentLength is null);
headers.ContentLength = 13;
Console.WriteLine(headers["Content-Length"]);

var media = MediaTypeHeaderValue.Parse("text/plain; charset=utf-8");
Console.WriteLine($"{media.MediaType} {media.Charset} {media.Parameters.Count}");
Console.WriteLine(MediaTypeHeaderValue.TryParse("application/json;q=0.5", out var json) + " " + json!.Quality);
Console.WriteLine(StringValues.Concat("a", new StringValues(["b", "c"])));

// Minimal APIs' JSON options read JsonSerializer.IsReflectionEnabledByDefault in their constructor: the
// switch, not a serialization, so it compiles (csharpc once reported it as reflection-based serialization).
var jsonOptions = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
Console.WriteLine($"{jsonOptions.PropertyNamingPolicy == System.Text.Json.JsonNamingPolicy.CamelCase} {jsonOptions.PropertyNameCaseInsensitive} {jsonOptions.TypeInfoResolver is not null}");
Console.WriteLine(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
