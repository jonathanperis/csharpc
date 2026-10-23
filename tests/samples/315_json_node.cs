#:property PublishAot=false
// System.Text.Json.Nodes: parsing, navigation, mutation, paths, conversions, DeepClone/DeepEquals,
// ToJsonString/ToString and the node API's exceptions.
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

JsonNode forecast = JsonNode.Parse("""
    {
      "Date": "2019-08-01T00:00:00",
      "Temperature": 25,
      "Summary": "Hot",
      "DatesAvailable": ["2019-08-01T00:00:00", "2019-08-02T00:00:00"],
      "TemperatureRanges": {
        "Cold": { "High": 20, "Low": -10 },
        "Hot": { "High": 60, "Low": 20 }
      },
      "Notes": null,
      "Ratio": 0.5,
      "Flag": true
    }
    """)!;

Console.WriteLine(forecast["Temperature"]!.GetValue<int>() + " " + (int)forecast["Temperature"]! + " " + forecast["Temperature"]!.GetValue<double>());
Console.WriteLine(forecast["Summary"]!.GetValue<string>() + " " + (string)forecast["Summary"]! + " " + forecast["Summary"]);
Console.WriteLine(forecast["Date"]!.GetValue<DateTime>().ToString("O"));
Console.WriteLine(forecast["DatesAvailable"]![1]!.GetValue<string>() + " " + forecast["DatesAvailable"]!.AsArray().Count);
Console.WriteLine(forecast["TemperatureRanges"]!["Hot"]!["High"]!.GetPath() + " " + forecast["TemperatureRanges"]!["Hot"]!["High"]);
Console.WriteLine(forecast["DatesAvailable"]![0]!.GetPath() + " " + forecast.GetPath() + " " + forecast["Notes"]);
Console.WriteLine(forecast["Ratio"]!.GetValueKind() + " " + forecast["Flag"]!.GetValueKind() + " " + forecast.GetValueKind() + " " + forecast["DatesAvailable"]!.GetValueKind());
Console.WriteLine(forecast["TemperatureRanges"]!["Cold"]!.Parent!.GetPropertyName() + " " + forecast["DatesAvailable"]![1]!.GetElementIndex());
Console.WriteLine(ReferenceEquals(forecast["TemperatureRanges"]!["Cold"]!.Root, forecast));
Console.WriteLine(forecast.ToJsonString());
Console.WriteLine(forecast["TemperatureRanges"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

// Mutation.
JsonObject obj = forecast.AsObject();
obj["Summary"] = "Cool";
obj["Temperature"] = 12.5;
obj.Add("Added", new JsonArray(1, "two", null, new JsonObject { ["x"] = false }));
obj.Remove("Notes");
obj["DatesAvailable"]!.AsArray().Add("2019-08-03T00:00:00");
obj["DatesAvailable"]!.AsArray().RemoveAt(0);
obj["TemperatureRanges"]!["Hot"]!["Low"] = JsonValue.Create(21L);
Console.WriteLine(obj.ToJsonString());
Console.WriteLine(obj.Count + " " + obj.ContainsKey("Added") + " " + obj.TryGetPropertyValue("Ratio", out JsonNode? ratio) + " " + ratio);
Console.WriteLine(string.Join(",", obj.Select(kv => kv.Key)));
Console.WriteLine(obj.IndexOf("Ratio") + " " + obj.GetAt(1).Key);

// Building from scratch, implicit conversions, ToString (indented) and JsonValue.
var built = new JsonObject
{
    ["int"] = 1,
    ["long"] = 9007199254740993L,
    ["double"] = 0.1 + 0.2,
    ["float"] = 1.1f,
    ["decimal"] = 79228162514264337593543950335m,
    ["bool"] = false,
    ["string"] = "a<b>\"c\"é",
    ["char"] = 'z',
    ["date"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
    ["null"] = null,
    ["array"] = new JsonArray { 1, 2, 3 },
};
Console.WriteLine(built.ToJsonString());
Console.WriteLine(built.ToString());
Console.WriteLine(built["string"]!.ToString() + " " + built["array"]!.ToString().Replace("\n", "\\n"));
Console.WriteLine(JsonValue.Create(5)!.GetValue<int>() + " " + JsonValue.Create(5)!.TryGetValue(out long _) + " " + JsonValue.Create("s")!.GetValueKind());
Console.WriteLine(string.Join(",", new JsonArray(1, 2, 3).GetValues<int>()));

// DeepClone / DeepEquals.
JsonNode clone = built.DeepClone();
Console.WriteLine(JsonNode.DeepEquals(clone, built) + " " + clone.ToJsonString().Length);
clone["int"] = 2;
Console.WriteLine(JsonNode.DeepEquals(clone, built));
Console.WriteLine(JsonNode.DeepEquals(JsonNode.Parse("{\"a\":[1,2.0]}"), JsonNode.Parse("{\"a\":[1.0,2]}")));
Console.WriteLine(JsonNode.DeepEquals(JsonValue.Create(1), JsonNode.Parse("1")));
Console.WriteLine(JsonNode.DeepEquals(null, JsonNode.Parse("null")));

// Case-insensitive objects.
var insensitive = JsonNode.Parse("{\"Name\": \"x\"}", new JsonNodeOptions { PropertyNameCaseInsensitive = true })!;
Console.WriteLine(insensitive["name"] + " " + insensitive["NAME"]);

// Errors.
void Try(string label, Action action)
{
    try { action(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
}
JsonNode doc = JsonNode.Parse("{\"a\": 1, \"s\": \"text\", \"arr\": [1]}")!;
Try("value as object", () => doc["a"]!.AsObject());
Try("object as array", () => doc.AsArray());
Try("object as value", () => doc.AsValue());
Try("wrong value type", () => doc["s"]!.GetValue<int>());
Try("value on object", () => doc.GetValue<int>());
Try("array index on object", () => _ = doc[0]);
Try("property on array", () => _ = doc["arr"]!["x"]);
Try("reparent", () => new JsonArray().Add(doc["a"]));
Try("duplicate add", () => doc.AsObject().Add("a", 2));
Try("cycle", () => { var o = new JsonObject(); var inner = new JsonObject(); o["inner"] = inner; inner["o"] = o; });
Try("element index without parent", () => new JsonArray().GetElementIndex());
Try("property name of array element", () => doc["arr"]![0]!.GetPropertyName());
Try("node as value", () => JsonValue.Create(new JsonObject()));
Try("parse error", () => JsonNode.Parse("{\"a\":}"));
Try("cast int from string", () => _ = (int)doc["s"]!);
Try("created value wrong type", () => JsonValue.Create(5)!.GetValue<string>());
