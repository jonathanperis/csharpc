#:property PublishAot=false
// The serializer over a wide range of member types: collection interfaces and concrete
// collections, sorted/linked collections, nested generics, DOM members, key/value
// pairs, nullable structs, init-only and required members, object creation handling, and
// generic types/methods as roots.
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var bag = new Bag
{
    List = new List<int> { 1 },
    IList = new List<int> { 2 },
    ICollection = new List<int> { 3 },
    IEnumerable = new[] { 4 },
    IReadOnlyCollection = new[] { 5 },
    ISet = new HashSet<string> { "s" },
    Sorted = new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 },
    SortedSet = new SortedSet<int> { 3, 1, 2 },
    Linked = new LinkedList<string>(new[] { "x", "y" }),
    IDictionary = new Dictionary<string, List<int>> { ["k"] = new List<int> { 7 } },
    IReadOnlyDictionary = new Dictionary<long, bool> { [-1] = true },
    Nested = new List<Dictionary<string, int[]>> { new() { ["z"] = new[] { 8, 9 } } },
    Pair = new KeyValuePair<string, int>("key", 10),
    MaybePoint = new Point { X = 1 },
    NoPoint = null,
    Element = JsonDocument.Parse("""{"raw":[true]}""").RootElement,
    Node = new JsonObject { ["n"] = 1 },
    Array = new JsonArray(1, "two"),
    Any = new List<object> { 1, "s", null },
};
string json = JsonSerializer.Serialize(bag);
Console.WriteLine(json);
var back = JsonSerializer.Deserialize<Bag>(json);
Console.WriteLine(JsonSerializer.Serialize(back) == json);
Console.WriteLine($"{back.IList.GetType().Name} {back.ICollection.GetType().Name} {back.IEnumerable.GetType().Name} {back.IReadOnlyCollection.GetType().Name} {back.ISet.GetType().Name} {back.IDictionary.GetType().Name} {back.IReadOnlyDictionary.GetType().Name}");
Console.WriteLine($"{back.Sorted.Count} {back.Linked.First.Value} {back.Pair.Key}={back.Pair.Value} {back.MaybePoint.Value.X} {back.NoPoint.HasValue} {back.Element.GetProperty("raw")[0]} {back.Node["n"]} {back.Array[1]} {back.Any[0].GetType().Name}");

// Init-only, required, read-only collections populated in place.
var init = JsonSerializer.Deserialize<InitOnly>("""{"Id":3,"Tags":["a"],"Settings":{"Mode":"m"},"Fixed":[9]}""");
Console.WriteLine($"{init.Id} {string.Join(",", init.Tags)} {init.Settings.Mode} {init.Settings.Level} {string.Join(",", init.Fixed)}");
var populate = JsonSerializer.Deserialize<InitOnly>("""{"Id":1,"Settings":{"Mode":"p"},"Fixed":[2]}""", new JsonSerializerOptions { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate });
Console.WriteLine($"{populate.Settings.Mode} {populate.Settings.Level} {string.Join(",", populate.Fixed)}");

var attributed = JsonSerializer.Deserialize<Attributed>("""{"Items":[2],"Map":{"b":2},"Inner":{"Level":9},"Stack":[5],"Point":{"Y":3}}""");
Console.WriteLine($"{string.Join(",", attributed.Items)} {string.Join(",", attributed.Map.Keys)} {attributed.Inner.Mode} {attributed.Inner.Level} {string.Join(",", attributed.Stack)} {attributed.Point.X},{attributed.Point.Y}");
try { JsonSerializer.Deserialize<BadPopulate>("{}"); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

// Generic types and generic methods as roots.
Console.WriteLine(JsonSerializer.Serialize(new Envelope<Point> { Data = new Point { X = 4, Y = 5 }, Count = 1 }));
Console.WriteLine(Roundtrip(new Envelope<string> { Data = "text", Count = 2 }));
Console.WriteLine(Roundtrip(new List<Envelope<int>> { new() { Data = 7 } }));
Console.WriteLine(Roundtrip(new Hidden { Secret = "nested private type" }));

static string Roundtrip<T>(T value)
{
    string json = JsonSerializer.Serialize(value);
    T back = JsonSerializer.Deserialize<T>(json);
    return json + " " + (JsonSerializer.Serialize(back) == json);
}

public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Bag
{
    public List<int> List { get; set; }
    public IList<int> IList { get; set; }
    public ICollection<int> ICollection { get; set; }
    public IEnumerable<int> IEnumerable { get; set; }
    public IReadOnlyCollection<int> IReadOnlyCollection { get; set; }
    public ISet<string> ISet { get; set; }
    public SortedDictionary<string, int> Sorted { get; set; }
    public SortedSet<int> SortedSet { get; set; }
    public LinkedList<string> Linked { get; set; }
    public IDictionary<string, List<int>> IDictionary { get; set; }
    public IReadOnlyDictionary<long, bool> IReadOnlyDictionary { get; set; }
    public List<Dictionary<string, int[]>> Nested { get; set; }
    public KeyValuePair<string, int> Pair { get; set; }
    public Point? MaybePoint { get; set; }
    public Point? NoPoint { get; set; }
    public JsonElement Element { get; set; }
    public JsonNode Node { get; set; }
    public JsonArray Array { get; set; }
    public List<object> Any { get; set; }
}

public class Settings
{
    public string Mode { get; set; } = "default";
    public int Level { get; set; } = 5;
}

public class InitOnly
{
    public required int Id { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = new List<string>();
    public Settings Settings { get; set; } = new Settings();
    public List<int> Fixed { get; } = new List<int> { 1 };
}

[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public class Attributed
{
    public List<int> Items { get; } = new() { 1 };
    public Dictionary<string, int> Map { get; set; } = new() { ["a"] = 1 };
    public Settings Inner { get; set; } = new Settings { Mode = "kept" };
    public Stack<int> Stack { get; } = new(new[] { 4 });
    public Point Point { get; set; } = new Point { X = 7 };
}

public class BadPopulate
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public int[] Numbers { get; set; }
}

public class Envelope<T>
{
    public T Data { get; set; }
    public int Count { get; set; }
}

partial class Program
{
    private class Hidden
    {
        public string Secret { get; set; }
    }
}
