// Source-generated JsonSerializerContexts (the trimming/AOT-safe path, used without reflection):
// JsonTypeInfo overloads, generated options, combined resolvers, streams and async APIs, and the
// errors for types a context doesn't know.
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

var person = new Person("Ada", 36) { Emails = new List<string> { "ada@example.com" }, Address = new Address { City = "London", Zip = null } };
Console.WriteLine(JsonSerializer.Serialize(person, AppContext.Default.Person));
Console.WriteLine(JsonSerializer.Serialize(person, typeof(Person), AppContext.Default));
Console.WriteLine(JsonSerializer.Serialize(new[] { 1, 2 }, AppContext.Default.Int32Array));
var parsed = JsonSerializer.Deserialize("""{"name":"Bob","age":5,"emails":[],"address":{"city":"Paris"}}""", AppContext.Default.Person);
Console.WriteLine($"{parsed.Name} {parsed.Age} {parsed.Emails.Count} {parsed.Address.City}");
Console.WriteLine(JsonSerializer.Deserialize("{\"k\":[1.5]}", AppContext.Default.DictionaryStringListDouble)["k"][0]);
Console.WriteLine(((Person)JsonSerializer.Deserialize("{\"name\":\"X\"}", typeof(Person), AppContext.Default)).Name);
Console.WriteLine(AppContext.Default.Options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase);
Console.WriteLine(AppContext.Default.Options.WriteIndented + " " + AppContext.Default.Options.DefaultIgnoreCondition);

// Indented output from a second context with its own generated options.
Console.WriteLine(JsonSerializer.Serialize(new Address { City = "Rome", Zip = "00100" }, PrettyContext.Default.Address));

// Options with a resolver chain; JsonTypeInfo lookups.
var options = new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(PrettyContext.Default, AppContext.Default) };
Console.WriteLine(JsonSerializer.Serialize(person, options));
Console.WriteLine(options.GetTypeInfo(typeof(Address)).Type.Name + " " + options.TryGetTypeInfo(typeof(Guid), out _));
var chained = new JsonSerializerOptions();
chained.TypeInfoResolverChain.Add(AppContext.Default);
Console.WriteLine(JsonSerializer.Serialize(new Address { City = "Oslo" }, chained));
JsonTypeInfo<Person> info = (JsonTypeInfo<Person>)AppContext.Default.GetTypeInfo(typeof(Person));
Console.WriteLine(info.Kind + " " + info.Properties.Count + " " + info.Properties[0].Name + " " + (AppContext.Default.GetTypeInfo(typeof(Guid)) == null));

// Streams and async.
var stream = new MemoryStream();
JsonSerializer.Serialize(stream, person, AppContext.Default.Person);
Console.WriteLine(stream.Length + " " + Encoding.UTF8.GetString(stream.ToArray()).Substring(0, 14));
stream.Position = 0;
Console.WriteLine(JsonSerializer.Deserialize(stream, AppContext.Default.Person).Name);
await RunAsync();

var writerBuffer = new MemoryStream();
using (var writer = new Utf8JsonWriter(writerBuffer))
{
    writer.WriteStartArray();
    JsonSerializer.Serialize(writer, new Address { City = "A" }, AppContext.Default.Address);
    JsonSerializer.Serialize(writer, 5, AppContext.Default.Int32);
    writer.WriteEndArray();
}
Console.WriteLine(Encoding.UTF8.GetString(writerBuffer.ToArray()));
var reader = new Utf8JsonReader("""[{"city":"B"}, 7]"""u8);
reader.Read();
reader.Read();
var address = JsonSerializer.Deserialize(ref reader, AppContext.Default.Address);
reader.Read();
Console.WriteLine(address.City + " " + JsonSerializer.Deserialize(ref reader, AppContext.Default.Int32) + " " + reader.TokenType);

var element = JsonSerializer.SerializeToElement(person, AppContext.Default.Person);
Console.WriteLine(element.GetProperty("address").GetProperty("city").GetString() + " " + JsonSerializer.Deserialize(element, AppContext.Default.Person).Age);
var node = JsonSerializer.SerializeToNode(person.Address, AppContext.Default.Address);
Console.WriteLine(node["city"] + " " + JsonSerializer.SerializeToDocument(5, AppContext.Default.Int32).RootElement.GetInt32());

// Types the context doesn't cover, and reflection being disabled.
Try(() => JsonSerializer.Serialize(new Address(), typeof(Address), PrettyContext.Default) + " (pretty)");
Try(() => JsonSerializer.Serialize(TimeSpan.Zero, typeof(TimeSpan), AppContext.Default));
Try(() => JsonSerializer.Serialize(person, options.GetTypeInfo(typeof(Address))));
Try(() => JsonSerializer.Serialize(person));
Try(() => JsonSerializer.Serialize(new Address(), new JsonSerializerOptions()));
Try(() => JsonSerializer.Deserialize<Person>("{}"));
Try(() => JsonSerializer.Deserialize("{\"age\":\"x\"}", AppContext.Default.Person));
Try(() => { AppContext.Default.Options.WriteIndented = true; return null; });

static async Task RunAsync()
{
    var stream = new MemoryStream();
    await JsonSerializer.SerializeAsync(stream, new List<int> { 3, 4 }, AppContext.Default.ListInt32);
    stream.Position = 0;
    var list = await JsonSerializer.DeserializeAsync(stream, AppContext.Default.ListInt32);
    Console.WriteLine("async " + list.Count + " " + list[1]);
    stream = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]"));
    await foreach (int item in JsonSerializer.DeserializeAsyncEnumerable(stream, AppContext.Default.Int32))
        Console.Write(item + ";");
    Console.WriteLine();
}

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public record Person(string Name, int Age)
{
    public List<string> Emails { get; set; }
    public Address Address { get; set; }
}

public class Address
{
    public string City { get; set; }
    public string Zip { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Person))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(Dictionary<string, List<double>>))]
internal partial class AppContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Address))]
internal partial class PrettyContext : JsonSerializerContext { }
