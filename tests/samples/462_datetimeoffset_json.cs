// System.Text.Json and DateTimeOffset: the serializer's converter (values, nullable, lists, dictionary
// keys), JsonElement/Utf8JsonReader getters, Utf8JsonWriter values and property values, JsonNode/JsonValue.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var opts = new JsonSerializerOptions { WriteIndented = false };
var ev = new Event { Name = "launch", At = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.FromHours(2)), Maybe = null,
    Stamps = new List<DateTimeOffset> { DateTimeOffset.UnixEpoch, new DateTimeOffset(2001, 2, 3, 4, 5, 6, 789, TimeSpan.FromMinutes(-330)) },
    ByDay = new Dictionary<DateTimeOffset, int> { [new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)] = 1 } };
string json = JsonSerializer.Serialize(ev, Ctx.Default.Event);
Console.WriteLine(json);
var back = JsonSerializer.Deserialize(json, Ctx.Default.Event)!;
Console.WriteLine($"{back.Name} {back.At:o} {back.Maybe.HasValue} {back.Stamps[1]:o} {back.ByDay.Count}");
var back2 = JsonSerializer.Deserialize("{\"Name\":\"x\",\"At\":\"2024-07-01T12:00:00Z\",\"Maybe\":\"2024-07-01T12:00:00.5-03:00\"}", Ctx.Default.Event)!;
Console.WriteLine($"{back2.At:o} {back2.Maybe:o}");
try { JsonSerializer.Deserialize("{\"At\":\"not a date\"}", Ctx.Default.Event); }
catch (JsonException e) { Console.WriteLine(e.Message); }

using (var doc = JsonDocument.Parse("[\"2013-01-07T00:00:00Z\", \"2013-01-08T10:00:00+05:30\", \"bad\", \"2013-01-08T10:00:00.25-11:00\"]"))
{
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        bool ok = el.TryGetDateTimeOffset(out var v);
        Console.WriteLine(ok ? v.ToString("o") + " " + v.DayOfWeek : "invalid");
    }
    try { doc.RootElement[2].GetDateTimeOffset(); } catch (FormatException e) { Console.WriteLine("FormatException " + e.Message); }
}

var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("{\"a\":\"2019-07-26T00:00:00-07:00\",\"b\":\"2019-07-26T00:00:00.1234567Z\"}"));
while (reader.Read())
    if (reader.TokenType == JsonTokenType.String)
        Console.WriteLine(reader.GetDateTimeOffset().ToString("o"));

var buffer = new System.IO.MemoryStream();
using (var w = new Utf8JsonWriter(buffer))
{
    w.WriteStartObject();
    w.WriteString("at", new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-8)));
    w.WriteString("frac", new DateTimeOffset(2024, 1, 2, 3, 4, 5, 120, TimeSpan.Zero));
    w.WritePropertyName("list");
    w.WriteStartArray();
    w.WriteStringValue(DateTimeOffset.MaxValue);
    w.WriteStringValue(DateTimeOffset.MinValue);
    w.WriteEndArray();
    w.WriteEndObject();
}
Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));

JsonNode node = new DateTimeOffset(2022, 2, 2, 2, 2, 2, TimeSpan.FromHours(1));
Console.WriteLine(node.ToJsonString() + " " + ((DateTimeOffset)node).Offset + " " + node.GetValue<DateTimeOffset>().Year);
var parsedNode = JsonNode.Parse("{\"t\":\"2022-02-02T02:02:02+01:00\"}")!;
Console.WriteLine(((DateTimeOffset)parsedNode["t"]!).ToString("o"));
Console.WriteLine(JsonValue.Create(new DateTimeOffset(2022, 2, 2, 2, 2, 2, TimeSpan.Zero)).ToJsonString());

public class Event
{
    public string Name { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public DateTimeOffset? Maybe { get; set; }
    public List<DateTimeOffset> Stamps { get; set; } = new();
    public Dictionary<DateTimeOffset, int> ByDay { get; set; } = new();
}

[JsonSerializable(typeof(Event))]
partial class Ctx : JsonSerializerContext { }
