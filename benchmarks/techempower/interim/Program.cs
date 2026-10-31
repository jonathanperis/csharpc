using System.Text.Json.Serialization;

// TechEmpower's plaintext and JSON test types (https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview):
// /plaintext answers "Hello, World!" as text/plain, /json serializes {"message":"Hello, World!"} for
// every request; both carry Kestrel's Server and Date headers. Middleware rather than MapGet: minimal
// API endpoints need the request delegate generator, which this harness doesn't want to depend on.
var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
// TechEmpower runs the server on port 8080; ASPNETCORE_URLS / --urls still override it.
if (builder.Configuration["urls"] is null) builder.WebHost.UseUrls("http://0.0.0.0:8080");
var app = builder.Build();

app.Run(context =>
{
    var path = context.Request.Path.Value;
    var response = context.Response;
    if (path == "/plaintext")
    {
        response.StatusCode = 200;
        response.ContentType = "text/plain";
        response.ContentLength = Payloads.Plaintext.Length;
        return response.Body.WriteAsync(Payloads.Plaintext).AsTask();
    }
    if (path == "/json")
    {
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { Message = "Hello, World!" }, AppJsonContext.Default.JsonMessage);
        response.StatusCode = 200;
        response.ContentType = "application/json";
        response.ContentLength = body.Length;
        return response.Body.WriteAsync(body).AsTask();
    }
    response.StatusCode = 404;
    return Task.CompletedTask;
});

app.Run();

static class Payloads
{
    public static readonly byte[] Plaintext = "Hello, World!"u8.ToArray();
}

public struct JsonMessage
{
    [JsonPropertyName("message")]
    public string Message { get; set; }
}

[JsonSerializable(typeof(JsonMessage))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
