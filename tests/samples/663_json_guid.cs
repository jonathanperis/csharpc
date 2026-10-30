// Guids in System.Text.Json: serialization (values and dictionary keys), Utf8JsonReader.GetGuid/TryGetGuid
// and JsonElement.GetGuid/TryGetGuid — only the 36-character "D" format, escaped or not.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

record Item(Guid Id, string Name);

[JsonSerializable(typeof(Item))]
[JsonSerializable(typeof(Dictionary<Guid, int>))]
partial class Ctx : JsonSerializerContext { }

class Program
{
    static void Main()
    {
        var id = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        string json = JsonSerializer.Serialize(new Item(id, "x"), Ctx.Default.Item);
        Console.WriteLine(json);
        Console.WriteLine(JsonSerializer.Deserialize(json, Ctx.Default.Item)!.Id);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<Guid, int> { [id] = 1 }, Ctx.Default.DictionaryGuidInt32));
        Console.WriteLine(JsonSerializer.Deserialize("{\"6F9619FF-8B86-D011-B42D-00C04FC964FF\":2}", Ctx.Default.DictionaryGuidInt32)![id]);

        foreach (string text in new[] { "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", "\"6F9619FF-8B86-D011-B42D-00C04FC964FF\"",
                                        "\"{6f9619ff-8b86-d011-b42d-00c04fc964ff}\"", "\"6f9619ff8b86d011b42d00c04fc964ff\"",
                                        "\"\\u0036f9619ff-8b86-d011-b42d-00c04fc964ff\"", "\"nope\"" })
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
            reader.Read();
            bool ok = reader.TryGetGuid(out Guid g);
            using var doc = JsonDocument.Parse(text);
            bool ok2 = doc.RootElement.TryGetGuid(out Guid g2);
            Console.WriteLine($"{text} -> {ok} {g} {ok2} {g2 == g}");
        }
        try { var r = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"bad\"")); r.Read(); r.GetGuid(); }
        catch (FormatException e) { Console.WriteLine(e.Message); }
        try { var r = new Utf8JsonReader(Encoding.UTF8.GetBytes("12")); r.Read(); r.GetGuid(); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        try { JsonSerializer.Deserialize("{\"Id\":\"bad\",\"Name\":\"y\"}", Ctx.Default.Item); }
        catch (JsonException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
