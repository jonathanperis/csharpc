// JsonSourceGenerationMode: serialization-only contexts (the generated fast path, without property
// metadata), metadata-only contexts, and what happens when options or the operation don't fit.
using System.Text.Json;
using System.Text.Json.Serialization;

var forecast = new Forecast { Date = new DateTime(2019, 8, 1), TemperatureCelsius = 25, Summary = "Hot", Tags = new List<string> { "a" }, Location = new Location { City = "Oslo" } };

Console.WriteLine(JsonSerializer.Serialize(forecast, SerializeOnlyContext.Default.Forecast));
Console.WriteLine(JsonSerializer.Serialize(forecast, typeof(Forecast), SerializeOnlyContext.Default));
Console.WriteLine(JsonSerializer.Serialize(forecast, SerializeOnlyIndentedContext.Default.Forecast));
Console.WriteLine(JsonSerializer.Serialize(forecast, PerTypeModeContext.Default.Forecast));
Console.WriteLine(JsonSerializer.Serialize(forecast, MetadataOnlyContext.Default.Forecast));
Console.WriteLine(JsonSerializer.Deserialize("""{"Date":"2020-01-02T00:00:00","TemperatureCelsius":-3,"Summary":"Cold"}""", MetadataOnlyContext.Default.Forecast).Summary);

Try(() => JsonSerializer.Deserialize("{}", SerializeOnlyContext.Default.Forecast));
var incompatible = new JsonSerializerOptions { TypeInfoResolver = SerializeOnlyContext.Default, WriteIndented = true, ReferenceHandler = ReferenceHandler.Preserve };
Try(() => JsonSerializer.Serialize(forecast, incompatible));
var compatible = new JsonSerializerOptions { TypeInfoResolver = SerializeOnlyContext.Default, WriteIndented = true };
Try(() => JsonSerializer.Serialize(forecast, compatible));

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public class Forecast
{
    public DateTime Date { get; set; }
    public int TemperatureCelsius { get; set; }
    public string? Summary { get; set; }
    public List<string>? Tags { get; set; }
    public Location? Location { get; set; }
}

public class Location
{
    public string? City { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(Forecast))]
internal partial class SerializeOnlyContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(Forecast))]
internal partial class SerializeOnlyIndentedContext : JsonSerializerContext { }

[JsonSerializable(typeof(Forecast), GenerationMode = JsonSourceGenerationMode.Serialization)]
internal partial class PerTypeModeContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Forecast))]
internal partial class MetadataOnlyContext : JsonSerializerContext { }
