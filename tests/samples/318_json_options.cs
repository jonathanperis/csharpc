#:property PublishAot=false
// JsonSerializerOptions and attributes: naming policies, ignore conditions, fields, ordering,
// renamed/included members, number handling, extension data, indentation and encoders.
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

var sample = new Sample
{
    FirstName = "Ann",
    lastName = "Lee",
    HTTPStatusCode = 200,
    IOStream = null,
    Amount = 12.5m,
    Count = 0,
    Hidden = "never",
    Renamed = "r",
    Nested = new Dictionary<string, int> { ["KeyOne"] = 1, ["keyTwo"] = 2 },
    Values = new List<double> { 1, double.NaN, double.PositiveInfinity },
};

foreach (var policy in new[] { null, JsonNamingPolicy.CamelCase, JsonNamingPolicy.SnakeCaseLower, JsonNamingPolicy.SnakeCaseUpper, JsonNamingPolicy.KebabCaseLower, JsonNamingPolicy.KebabCaseUpper })
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = policy,
        DictionaryKeyPolicy = policy,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    Console.WriteLine(JsonSerializer.Serialize(sample, options));
}

var all = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    IncludeFields = true,
    IgnoreReadOnlyProperties = true,
    NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
    WriteIndented = true,
    IndentCharacter = '\t',
    IndentSize = 1,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
sample.Values = new List<double> { 0.5 };
sample.Note = "<b>&'é'</b>";
Console.WriteLine(JsonSerializer.Serialize(sample, all));
Console.WriteLine(JsonSerializer.Serialize(new Sample(), new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
Console.WriteLine(JsonSerializer.Serialize(new Sample(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

// Reading with the same options.
var back = JsonSerializer.Deserialize<Sample>("""{"first_name":"Z","amount":"3.25","count":"7","renamed_by_attr":"x","values":["NaN","-Infinity",1],"Extra1":true,"Extra2":{"a":[1]}}""",
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals });
Console.WriteLine($"{back.FirstName} {back.Amount} {back.Count} {back.Renamed} {string.Join(",", back.Values)} {back.Overflow.Count} {back.Overflow["Extra2"]}");
Console.WriteLine(JsonSerializer.Serialize(back, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));

var web = JsonSerializer.Deserialize<Sample>("""{"firstname":"case","count":"5"}""", JsonSerializerOptions.Web);
Console.WriteLine(web.FirstName + " " + web.Count);

// Order, include, constructor attributes and field handling.
Console.WriteLine(JsonSerializer.Serialize(new Ordered()));
var ordered = JsonSerializer.Deserialize<Ordered>("""{"Z":5,"secret":9,"Field":3}""");
Console.WriteLine(ordered.Z + " " + ordered.GetSecret() + " " + ordered.Field);
var immutable = JsonSerializer.Deserialize<Immutable>("""{"Id":4,"Label":"L"}""");
Console.WriteLine(immutable.Id + " " + immutable.Label + " " + JsonSerializer.Serialize(immutable));
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["a b"] = "<>" }, new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase }));

// Number handling and strict options.
Console.WriteLine(JsonSerializer.Serialize(new Numbers(), new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString }));
var strict = JsonSerializerOptions.Strict;
Try(() => JsonSerializer.Deserialize<Numbers>("""{"A":1,"Unknown":2}""", strict));
Try(() => JsonSerializer.Deserialize<Numbers>("""{"A":1,"A":2}""", strict));
Try(() => JsonSerializer.Deserialize<Numbers>("""{"A":"1"}"""));
Try(() => JsonSerializer.Deserialize<Numbers>("""{"D":"NaN"}"""));
Try(() => JsonSerializer.Deserialize<Numbers>("""{"D":"NaN"}""", new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
Try(() => JsonSerializer.Serialize(new Numbers { D = double.NaN }));
Try(() => JsonSerializer.Deserialize<Numbers>("""{"A":" 1"}""", new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString }));
Try(() => JsonSerializer.Deserialize<Numbers>("{\"A\": 1}", new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }).A);
Try(() => JsonSerializer.Deserialize<Numbers>("{\"B\": 1}", new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }));

// Options are frozen once used.
var frozen = new JsonSerializerOptions();
JsonSerializer.Serialize(1, frozen);
Try(() => { frozen.WriteIndented = true; return null; });
Console.WriteLine(frozen.IsReadOnly + " " + new JsonSerializerOptions(frozen).IsReadOnly);
Try(() => { new JsonSerializerOptions { MaxDepth = -1 }; return null; });
Try(() => { new JsonSerializerOptions { IndentSize = 200 }; return null; });
Try(() => { new JsonSerializerOptions { IndentCharacter = 'x' }; return null; });
Try(() => { new JsonSerializerOptions { NewLine = "\t" }; return null; });

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public class Sample
{
    public string FirstName { get; set; }
    [JsonInclude] public string lastName;
    public int HTTPStatusCode { get; set; }
    public string IOStream { get; set; }
    public decimal Amount { get; set; }
    public int Count { get; set; }
    [JsonIgnore] public string Hidden { get; set; }
    [JsonPropertyName("renamed_by_attr")] public string Renamed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Note { get; set; }
    public Dictionary<string, int> Nested { get; set; }
    public List<double> Values { get; set; }
    public int Computed => Count + 1;
    public int PublicField = 4;
    [JsonExtensionData] public Dictionary<string, object> Overflow { get; set; }
}

public class Ordered
{
    [JsonPropertyOrder(2)] public int A { get; set; } = 1;
    [JsonPropertyOrder(-1)] public int Z { get; set; } = 26;
    public int M { get; set; } = 13;
    [JsonInclude, JsonPropertyName("secret")] private int Secret { get; set; } = 42;
    [JsonInclude] public int Field = 6;
    public int GetSecret() => Secret;
}

public class Immutable
{
    [JsonConstructor]
    public Immutable(int id, string label) { Id = id; Label = label + "!"; }
    public Immutable() { }
    public int Id { get; }
    public string Label { get; }
}

public class Numbers
{
    public int A { get; set; } = 1;
    public double D { get; set; } = 2.5;
    [JsonNumberHandling(JsonNumberHandling.Strict)] public long L { get; set; } = 3;
    public float? F { get; set; } = 1.5f;
    public byte[] Bytes { get; set; }
}
