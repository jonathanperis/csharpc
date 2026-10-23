#:property PublishAot=false
// Custom JsonConverter<T>s (options, [JsonConverter] on types and members, factories), enum
// converters with naming policies and member names, and serialization callbacks.
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

var options = new JsonSerializerOptions();
options.Converters.Add(new UpperStringConverter());
options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

var doc = new Document
{
    Title = "hello",
    Color = Color.DarkRed,
    Flags = Perms.Read | Perms.Write,
    Money = new Money(1234, "EUR"),
    Temperature = new Celsius(21.5),
    Labels = new List<string> { "a", "b" },
    Mode = Mode.Fast,
};

string json = JsonSerializer.Serialize(doc, options);
Console.WriteLine(json);
var back = JsonSerializer.Deserialize<Document>(json, options);
Console.WriteLine($"{back.Title} {back.Color} {back.Flags} {back.Money.Cents} {back.Money.Currency} {back.Temperature.Degrees} {string.Join("|", back.Labels)} {back.Mode}");
Console.WriteLine(JsonSerializer.Serialize(doc));
Console.WriteLine(JsonSerializer.Serialize(new[] { Color.Red, Color.DarkRed, (Color)7 }, options));
Console.WriteLine(JsonSerializer.Serialize(Perms.None, options) + " " + JsonSerializer.Serialize(Perms.All, options) + " " + JsonSerializer.Serialize((Perms)16, options));
Console.WriteLine(JsonSerializer.Deserialize<Perms>("\"read, execute\"", options) + " " + JsonSerializer.Deserialize<Color>("\"dark_red\"", options) + " " + JsonSerializer.Deserialize<Color>("2", options) + " " + JsonSerializer.Deserialize<Mode>("\"turbo-mode\"", options));
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<Color, int> { [Color.Red] = 1, [Color.DarkRed] = 2 }, options));
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<Color, int> { [Color.Red] = 1 }));

var noInts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) } };
Try(() => JsonSerializer.Deserialize<Color>("1", noInts));
Try(() => JsonSerializer.Deserialize<Color>("\"Purple\"", options));
Try(() => JsonSerializer.Deserialize<Money>("{\"cents\":1}"));
Try(() => JsonSerializer.Deserialize<Money>("\"12 USD\""));
Try(() => JsonSerializer.Serialize((Color)7, noInts));
Console.WriteLine(JsonSerializer.Serialize(Color.DarkRed, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<Color>() } }));
Console.WriteLine(JsonSerializer.Serialize(Mode.Fast, new JsonSerializerOptions { Converters = { new JsonNumberEnumConverter<Mode>() } }));

// Callbacks.
var cb = JsonSerializer.Deserialize<WithCallbacks>("{\"Value\":3}");
Console.WriteLine(JsonSerializer.Serialize(cb));
Console.WriteLine(string.Join(",", WithCallbacks.Log));

// A factory converter producing a converter per closed generic type.
var factoryOptions = new JsonSerializerOptions { Converters = { new BoxFactory() } };
Console.WriteLine(JsonSerializer.Serialize(new Holder { I = new Box<int>(5), S = new Box<string>("x") }, factoryOptions));
var holder = JsonSerializer.Deserialize<Holder>("{\"I\":[9],\"S\":[\"y\"]}", factoryOptions);
Console.WriteLine(holder.I.Value + " " + holder.S.Value);

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public enum Color { Red, Green, DarkRed }

[Flags]
public enum Perms { None = 0, Read = 1, Write = 2, Execute = 4, All = 7 }

[JsonConverter(typeof(JsonStringEnumConverter<Mode>))]
public enum Mode
{
    Slow,
    [JsonStringEnumMemberName("turbo-mode")] Fast,
}

public class UpperStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()?.ToLowerInvariant();
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUpperInvariant());
}

[JsonConverter(typeof(MoneyConverter))]
public readonly record struct Money(long Cents, string Currency);

public class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Money must be a string.");
        string[] parts = reader.GetString().Split(' ');
        return new Money(long.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Cents.ToString(CultureInfo.InvariantCulture) + " " + value.Currency);
}

public struct Celsius
{
    public Celsius(double degrees) { Degrees = degrees; }
    public double Degrees { get; }
}

public class CelsiusConverter : JsonConverter<Celsius>
{
    public override Celsius Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read();
        double d = reader.GetDouble();
        reader.Read();
        return new Celsius(d);
    }

    public override void Write(Utf8JsonWriter writer, Celsius value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Degrees);
        writer.WriteEndArray();
    }
}

public class Document
{
    public string Title { get; set; }
    public Color Color { get; set; }
    public Perms Flags { get; set; }
    public Money Money { get; set; }
    [JsonConverter(typeof(CelsiusConverter))] public Celsius Temperature { get; set; }
    public List<string> Labels { get; set; }
    public Mode Mode { get; set; }
}

public class WithCallbacks : IJsonOnDeserializing, IJsonOnDeserialized, IJsonOnSerializing, IJsonOnSerialized
{
    public static List<string> Log = new();
    public int Value { get; set; }
    public void OnDeserializing() => Log.Add("deserializing " + Value);
    public void OnDeserialized() { Log.Add("deserialized " + Value); Value *= 10; }
    public void OnSerializing() => Log.Add("serializing " + Value);
    public void OnSerialized() => Log.Add("serialized");
}

public class Box<T>
{
    public Box(T value) { Value = value; }
    public T Value { get; }
}

public class Holder
{
    public Box<int> I { get; set; }
    public Box<string> S { get; set; }
}

public class BoxFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Box<int>) || typeToConvert == typeof(Box<string>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(Box<int>) ? new BoxConverter<int>() : new BoxConverter<string>();
}

public class BoxConverter<T> : JsonConverter<Box<T>>
{
    public override Box<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read();
        T value = JsonSerializer.Deserialize<T>(ref reader, options);
        reader.Read();
        return new Box<T>(value);
    }

    public override void Write(Utf8JsonWriter writer, Box<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndArray();
    }
}
