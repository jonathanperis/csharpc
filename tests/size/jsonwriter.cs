using System.Text.Json;

// Utf8JsonWriter and JsonDocument only: no serializer, so no reflection roots.
using var stream = new MemoryStream();
using (var writer = new Utf8JsonWriter(stream))
{
    writer.WriteStartObject();
    writer.WriteString("name", "Ada");
    writer.WriteNumber("age", 36);
    writer.WriteEndObject();
}
using var document = JsonDocument.Parse(stream.ToArray());
Console.WriteLine(document.RootElement.GetProperty("name").GetString());
