using System.Text.Json;

// A reflection-style JsonSerializer call without the opt-in: reflection-based JSON is off by default, as under NativeAOT (#320), so the
// program throws the framework's InvalidOperationException; what it measures is the framework's System.Text.Json reached by default.
var json = JsonSerializer.Serialize(new Person("Ada", 36, ["math", "engines"]));
Console.WriteLine(json);
Console.WriteLine(JsonSerializer.Deserialize<Person>(json)!.Name);

record Person(string Name, int Age, string[] Tags);
