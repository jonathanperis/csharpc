using System.Text.Json;
using System.Text.Json.Serialization;

// Reflection-based System.Text.Json without an opt-in: as a PublishAot build (and `dotnet run app.cs`, which sets the PublishAot feature
// switches), System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault is false, and the framework's own code says so. A
// JsonSerializerContext keeps working. `#:property JsonSerializerIsReflectionEnabledByDefault=true` opts in (316 and the other samples).
Console.WriteLine(JsonSerializer.IsReflectionEnabledByDefault);
try { Console.WriteLine(JsonSerializer.Serialize(new Point(1, 2))); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { Console.WriteLine(JsonSerializer.Deserialize<Point>("""{"X":3,"Y":4}""")); }
catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine(JsonSerializer.Serialize(new Point(5, 6), PointContext.Default.Point));
Console.WriteLine(JsonSerializer.Deserialize("""{"X":7,"Y":8}""", PointContext.Default.Point));

record Point(int X, int Y);

[JsonSerializable(typeof(Point))]
partial class PointContext : JsonSerializerContext;
