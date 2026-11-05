// JsonSchemaExporter: JSON schemas generated from serialization contracts (System.Text.Json.Schema), as
// Microsoft.Extensions.AI uses them to describe function parameters: built-in types, enums, nullability,
// collections, objects with required members and constructor defaults, recursion ($ref), polymorphism,
// naming policies, number handling, custom converters and the TransformSchemaNode callback.
#pragma warning disable CS0618
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

static void Show(string name, JsonTypeInfo info, JsonSchemaExporterOptions? exporter = null)
{
    try { Console.WriteLine(name + ": " + info.GetJsonSchemaAsNode(exporter).ToJsonString()); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}

static void ShowType(string name, JsonSerializerOptions options, Type type, JsonSchemaExporterOptions? exporter = null)
{
    try { Console.WriteLine(name + ": " + JsonSchemaExporter.GetJsonSchemaAsNode(options, type, exporter).ToJsonString()); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}

var o = Ctx.Default.Options;

// The shape Microsoft.Extensions.AI builds: a transform that adds a description per property.
var node = JsonSchemaExporter.GetJsonSchemaAsNode(o, typeof(Point), new JsonSchemaExporterOptions
{
    TransformSchemaNode = (context, schema) =>
    {
        if (context.PropertyInfo is { } property)
            schema["description"] = "the " + property.Name;
        return schema;
    },
});
Console.WriteLine(node.ToJsonString());

ShowType("int", o, typeof(int));
ShowType("bool", o, typeof(bool));
ShowType("string", o, typeof(string));
ShowType("char", o, typeof(char));
ShowType("double", o, typeof(double));
ShowType("decimal", o, typeof(decimal));
ShowType("byte", o, typeof(byte));
ShowType("sbyte", o, typeof(sbyte));
ShowType("short", o, typeof(short));
ShowType("ushort", o, typeof(ushort));
ShowType("uint", o, typeof(uint));
ShowType("long", o, typeof(long));
ShowType("ulong", o, typeof(ulong));
ShowType("float", o, typeof(float));
ShowType("DateTime", o, typeof(DateTime));
ShowType("DateTimeOffset", o, typeof(DateTimeOffset));
ShowType("TimeSpan", o, typeof(TimeSpan));
ShowType("Guid", o, typeof(Guid));
ShowType("byte[]", o, typeof(byte[]));
ShowType("object", o, typeof(object));
ShowType("JsonElement", o, typeof(JsonElement));
ShowType("JsonNode", o, typeof(JsonNode));
ShowType("int?", o, typeof(int?));
ShowType("Color", o, typeof(Color));
ShowType("Color?", o, typeof(Color?));
ShowType("Shade", o, typeof(Shade));
ShowType("Flags", o, typeof(Permissions));
ShowType("int[]", o, typeof(int[]));
ShowType("List<string>", o, typeof(List<string>));
ShowType("Dictionary<string,int>", o, typeof(Dictionary<string, int>));
ShowType("List<Point>", o, typeof(List<Point>));
ShowType("Point", o, typeof(Point));
ShowType("Profile", o, typeof(Profile));
ShowType("Account", o, typeof(Account));
ShowType("Tree", o, typeof(Tree));
ShowType("Shape", o, typeof(Shape));
ShowType("Opaque", o, typeof(Opaque));
ShowType("Strict", o, typeof(Strict));
ShowType("Counts", o, typeof(Counts));
ShowType("Sequence", o, typeof(Sequence));
ShowType("Retired", o, typeof(Retired));

var camel = CamelCtx.Default.Options;
ShowType("camel Profile", camel, typeof(Profile));

var oblivious = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
ShowType("oblivious Profile", o, typeof(Profile), oblivious);
ShowType("oblivious string", o, typeof(string), oblivious);

Show("typeinfo Point", Ctx.Default.Point);
Show("typeinfo int", Ctx.Default.Int32);

// Transform with paths and base types.
var paths = new List<string>();
JsonSchemaExporter.GetJsonSchemaAsNode(o, typeof(Tree), new JsonSchemaExporterOptions
{
    TransformSchemaNode = (context, schema) =>
    {
        paths.Add("#/" + string.Join("/", context.Path.ToArray()) + " " + context.TypeInfo.Type.Name + " " + (context.PropertyInfo?.Name ?? "-") + " " + (context.BaseTypeInfo?.Type.Name ?? "-"));
        return schema;
    },
});
paths.Sort(StringComparer.Ordinal);
foreach (var p in paths) Console.WriteLine(p);

try { JsonSchemaExporter.GetJsonSchemaAsNode(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve, TypeInfoResolver = Ctx.Default }, typeof(Point)); }
catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { JsonSchemaExporter.GetJsonSchemaAsNode((JsonSerializerOptions)null!, typeof(Point)); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { JsonSchemaExporter.GetJsonSchemaAsNode(o, null!); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { JsonSchemaExporter.GetJsonSchemaAsNode((JsonTypeInfo)null!); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
ShowType("unknown type", o, typeof(Uri));

record Point(int X, int Y);

enum Color { Red, Green, Blue }

[JsonConverter(typeof(JsonStringEnumConverter<Shade>))]
enum Shade { Light, Dark }

[Flags] enum Permissions { None = 0, Read = 1, Write = 2 }

class Profile
{
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public int Age { get; set; }
    public int? Score { get; set; }
    public List<string> Tags { get; set; } = new();
    public Color Favorite { get; set; }
    public required string Id { get; init; }
    [JsonIgnore] public string Secret { get; set; } = "";
    [Obsolete("use Name")] public string? Legacy { get; set; }
    [JsonPropertyName("e-mail")] public string? Email { get; set; }
    public DateTime Created { get; set; }
    public Guid Key { get; set; }
}

class Account
{
    public Account(string name, int level = 3, string? note = null) { Name = name; Level = level; Note = note; }
    public string Name { get; }
    public int Level { get; }
    public string? Note { get; }
}

class Tree
{
    public string? Label { get; set; }
    public Tree? Left { get; set; }
    public Tree? Right { get; set; }
    public List<Tree> Children { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
abstract class Shape { public string? Name { get; set; } }
class Circle : Shape { public double Radius { get; set; } }
class Square : Shape { public double Side { get; set; } }

[JsonConverter(typeof(OpaqueConverter))]
class Opaque { public int Value; }

class OpaqueConverter : JsonConverter<Opaque>
{
    public override Opaque? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new Opaque { Value = reader.GetInt32() };
    public override void Write(Utf8JsonWriter writer, Opaque value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
class Strict
{
    public int A { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    public string? B { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
class Counts
{
    public int A { get; set; }
    public double B { get; set; }
    public List<long> C { get; set; } = new();
}

class Sequence : List<Point> { }

[Obsolete("gone")]
class Retired { public int Value { get; set; } }

[JsonSerializable(typeof(Point))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(Color))]
[JsonSerializable(typeof(Color?))]
[JsonSerializable(typeof(Shade))]
[JsonSerializable(typeof(Permissions))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(List<Point>))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(Account))]
[JsonSerializable(typeof(Tree))]
[JsonSerializable(typeof(Shape))]
[JsonSerializable(typeof(Opaque))]
[JsonSerializable(typeof(Strict))]
[JsonSerializable(typeof(Counts))]
[JsonSerializable(typeof(Sequence))]
[JsonSerializable(typeof(Retired))]
partial class Ctx : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Profile))]
partial class CamelCtx : JsonSerializerContext;
