using System.Text.Json;
using System.Text.Json.Serialization;

Console.WriteLine(JsonSerializer.Serialize(new List<string> { "a", "b" }, Ctx.Default.ListString));
Console.WriteLine(JsonSerializer.Deserialize("[\"x\",\"y\"]", Ctx.Default.ListString)!.Count);

[JsonSerializable(typeof(List<string>))]
partial class Ctx : JsonSerializerContext;
