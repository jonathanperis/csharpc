// Utf8JsonWriter, Utf8JsonReader and JsonDocument throughput for the IL-first A/B (tools/il-first/ab.sh).
// stdout is a deterministic checksum (compared with real .NET); the elapsed time goes to stderr.
using System;
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;

var clock = Stopwatch.StartNew();
long checksum = 0;
var buffer = new ArrayBufferWriter<byte>();
for (int round = 0; round < 40; round++)
{
    buffer.Clear();
    using (var writer = new Utf8JsonWriter(buffer))
    {
        writer.WriteStartObject();
        writer.WriteStartArray("items");
        for (int i = 0; i < 400; i++)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", i * 31 + round);
            writer.WriteString("name", "item-" + i + " \"quoted\" café <tag>");
            writer.WriteNumber("price", i * 1.25 + 0.5);
            writer.WriteBoolean("ok", (i & 1) == 0);
            writer.WriteNull("none");
            writer.WriteStartArray("tags");
            for (int t = 0; t < 4; t++) writer.WriteStringValue("tag" + (i + t) % 17);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    checksum += buffer.WrittenCount;

    var reader = new Utf8JsonReader(buffer.WrittenSpan);
    while (reader.Read())
    {
        if (reader.TokenType == JsonTokenType.Number) checksum += reader.GetDouble() > 100 ? 2 : 1;
        else if (reader.TokenType == JsonTokenType.String) checksum += reader.GetString()!.Length;
    }

    using var document = JsonDocument.Parse(buffer.WrittenMemory);
    foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        checksum += item.GetProperty("name").GetString()!.Length + item.GetProperty("tags").GetArrayLength() + (item.GetProperty("ok").GetBoolean() ? 1 : 0);
}
Console.WriteLine(checksum);
Console.Error.WriteLine($"elapsed {clock.ElapsedMilliseconds} ms");
