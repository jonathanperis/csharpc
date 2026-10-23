// System.Text.Json.JsonDocument / JsonElement / JsonProperty: navigation, typed getters, raw text,
// WriteTo (re-escaped through the writer), Clone, DeepEquals, duplicate properties and errors.
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

string json = """
    {
      "name": "Widget",
      "id": 12345678901,
      "price": 19.990,
      "tags": ["a", "b\u00e9", "c\"q"],
      "dims": { "w": 1.5, "h": -2e3, "unit": null },
      "active": true,
      "when": "2024-02-29T12:34:56.789Z",
      "dup": 1,
      "dup": 2,
      "esc\u0061ped": "x\ny",
      "empty": {},
      "nested": [[1, [2, [3]]], {"k": [true, false]}]
    }
    """;

using (JsonDocument doc = JsonDocument.Parse(json))
{
    JsonElement root = doc.RootElement;
    Console.WriteLine(root.ValueKind + " " + root.GetPropertyCount());
    foreach (JsonProperty p in root.EnumerateObject())
        Console.WriteLine($"{p.Name} {p.Value.ValueKind} [{p.Value.GetRawText()}] <{p.Value}> {p.NameEquals("dup")}");
    Console.WriteLine(root.GetProperty("id").GetInt64() + " " + root.GetProperty("id").TryGetInt32(out int small) + " " + small);
    Console.WriteLine(root.GetProperty("price").GetDecimal() + " " + root.GetProperty("price").GetDouble());
    Console.WriteLine(root.GetProperty("dup").GetInt32());
    Console.WriteLine(root.GetProperty("escaped").GetString().Replace("\n", "\\n") + " " + root.TryGetProperty("escaped"u8, out _));
    Console.WriteLine(root.GetProperty("when").GetDateTime().ToString("O"));
    Console.WriteLine(root.GetProperty("tags").GetArrayLength() + " " + root.GetProperty("tags")[1].GetString() + " " + root.GetProperty("tags")[2]);
    Console.WriteLine(string.Join("|", root.GetProperty("tags").EnumerateArray().Select(e => e.GetString())));
    Console.WriteLine(root.GetProperty("dims").GetProperty("h").GetDouble() + " " + root.GetProperty("dims").GetProperty("unit").ValueKind);
    Console.WriteLine(root.GetProperty("nested")[0][1][1][0].GetInt32() + " " + root.GetProperty("nested")[1].GetProperty("k")[1].GetBoolean());
    Console.WriteLine(root.GetProperty("name").ValueEquals("Widget") + " " + root.GetProperty("name").ValueEquals("widget"));

    foreach (bool indented in new[] { false, true })
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented }))
            root.WriteTo(writer);
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    JsonElement clone = root.GetProperty("dims").Clone();
    doc.Dispose();
    Console.WriteLine(clone.GetRawText() + " " + clone.GetProperty("w"));
    try { root.GetProperty("name"); } catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

Console.WriteLine(JsonElement.DeepEquals(JsonDocument.Parse("{\"a\":[1,2.0,{\"b\":null}],\"c\":\"x\"}").RootElement,
    JsonDocument.Parse("{\"c\":\"\\u0078\",\"a\":[1.0,2,{\"b\":null}]}").RootElement));
Console.WriteLine(JsonElement.DeepEquals(JsonDocument.Parse("[1,2]").RootElement, JsonDocument.Parse("[2,1]").RootElement));
Console.WriteLine(JsonElement.DeepEquals(JsonDocument.Parse("1e2").RootElement, JsonDocument.Parse("100").RootElement));

// Errors from the element API.
using (JsonDocument doc = JsonDocument.Parse("{\"n\": 300, \"s\": \"text\", \"a\": [1]}"))
{
    JsonElement root = doc.RootElement;
    void Try(string label, Action action)
    {
        try { action(); Console.WriteLine(label + ": ok"); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
    }
    Try("missing", () => root.GetProperty("missing"));
    Try("byte", () => root.GetProperty("n").GetByte());
    Try("string as int", () => root.GetProperty("s").GetInt32());
    Try("int as string", () => root.GetProperty("n").GetString());
    Try("bool", () => root.GetProperty("n").GetBoolean());
    Try("array on object", () => root.EnumerateArray());
    Try("object on array", () => root.GetProperty("a").EnumerateObject());
    Try("index", () => _ = root.GetProperty("a")[5]);
    Try("length of object", () => root.GetArrayLength());
    Try("default element", () => default(JsonElement).GetRawText());
    Try("date", () => root.GetProperty("s").GetDateTime());
    Try("base64", () => root.GetProperty("s").GetBytesFromBase64());
}

// Parse overloads and options.
Console.WriteLine(JsonDocument.Parse(new MemoryStream(Encoding.UTF8.GetBytes("\uFEFF[1, 2]"))).RootElement.GetArrayLength());
Console.WriteLine(JsonDocument.Parse("[1, /* c */ 2,]", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }).RootElement.GetRawText());
Console.WriteLine(JsonElement.Parse("{\"x\": 1}").GetProperty("x"));
var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("[{\"a\": 1}, 2]"));
reader.Read();
reader.Read();
using (var partial = JsonDocument.ParseValue(ref reader))
    Console.WriteLine(partial.RootElement.GetRawText() + " " + reader.TokenType);
try
{
    JsonDocument.Parse("{\"a\":1,\"a\":2}", new JsonDocumentOptions { AllowDuplicateProperties = false });
}
catch (JsonException e)
{
    Console.WriteLine(e.GetType().Name + ": " + e.Message);
}
