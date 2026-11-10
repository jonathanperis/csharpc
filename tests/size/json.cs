#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Text.Json;

// The reflection-based serializer of the framework's System.Text.Json (ADR 0023), opted in (it is off by default, as under NativeAOT):
// the expensive case for size.
var json = JsonSerializer.Serialize(new Person("Ada", 36, ["math", "engines"]));
Console.WriteLine(json);
Console.WriteLine(JsonSerializer.Deserialize<Person>(json)!.Name);

record Person(string Name, int Age, string[] Tags);
