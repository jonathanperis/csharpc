#:property PublishAot=false
// JsonSerializer.Deserialize without a JsonSerializerContext: objects, records and constructors,
// collections, dictionaries, nullables, enums, case-insensitivity, comments, trailing commas,
// unmapped members, required properties, and the exact JsonException messages and paths.
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

string json = """
{
  "Id": 7,
  "Customer": { "Name": "Bo", "Email": "bo@example.com", "Tier": 2, "Unknown": [1, {"a": null}] },
  "Lines": [ { "Sku": "X", "Quantity": 3, "Price": 1.50 }, { "Sku": "Y", "Quantity": 0, "Price": -2e-3 } ],
  "Tags": ["a", "b", "c"],
  "Attributes": { "k1": "v1", "k2": null },
  "Counts": { "1": 0.25, "-3": 1e10 },
  "PlacedAt": "2024-02-29T23:59:59.1234567Z",
  "ShippedAt": null,
  "Origin": { "X": -1, "Y": 2 },
  "Grid": [[1], [], [2, 3]],
  "Seen": [5, 5, 6],
  "Duration": "1.02:03:04.5000000",
  "Initial": "Q",
  "Data": "AP+A"
}
""";

var order = JsonSerializer.Deserialize<Order>(json);
Console.WriteLine($"{order.Id} {order.Customer.Name} {order.Customer.Email} {order.Customer.Tier}");
foreach (var line in order.Lines) Console.WriteLine($"  {line.Sku} x{line.Quantity} @ {line.Price}");
Console.WriteLine(string.Join(",", order.Tags) + " | " + string.Join(",", order.Attributes.Keys) + " | " + (order.Attributes["k2"] == null));
foreach (var kv in order.Counts) Console.WriteLine($"  {kv.Key} => {kv.Value}");
Console.WriteLine(order.PlacedAt.ToString("O") + " " + order.PlacedAt.Kind + " " + (order.ShippedAt == null));
Console.WriteLine($"{order.Origin.X},{order.Origin.Y} grid={order.Grid.Length}:{order.Grid[2][1]} seen={order.Seen.Count} {order.Duration} {order.Initial} {order.Data.Length}:{order.Data[1]}");

// Records and parameterized constructors.
var rec = JsonSerializer.Deserialize<Rec>("""{"Count": 3, "Name": "r", "Extra": true}""");
Console.WriteLine(rec);
var recs = JsonSerializer.Deserialize<List<Rec>>("""[{"Name":"a","Count":1},null,{"Name":"b"}]""");
Console.WriteLine(recs.Count + " " + recs[0] + " " + (recs[1] == null) + " " + recs[2]);
var pt = JsonSerializer.Deserialize<Point>("""{"X":10,"Y":20,"Length":99}""");
Console.WriteLine(pt.X + " " + pt.Y);
var ctor = JsonSerializer.Deserialize<WithCtor>("""{"a":1,"B":"two","c":[1,2]}""", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
Console.WriteLine(ctor.A + " " + ctor.B + " " + ctor.C.Count + " " + ctor.Set);

// Primitives and DOM-ish roots.
Console.WriteLine(JsonSerializer.Deserialize<int>("42") + " " + JsonSerializer.Deserialize<double>("1.5e3") + " " + JsonSerializer.Deserialize<string>("\"s\\u00e9\"") + " " + JsonSerializer.Deserialize<bool>("true"));
Console.WriteLine(JsonSerializer.Deserialize<int?>("null") == null);
Console.WriteLine(JsonSerializer.Deserialize<decimal>("79228162514264337593543950335") + " " + JsonSerializer.Deserialize<long>("-9223372036854775808") + " " + JsonSerializer.Deserialize<ulong>("18446744073709551615"));
Console.WriteLine(JsonSerializer.Deserialize<Tier>("1") + " " + JsonSerializer.Deserialize<Dictionary<string, int>>("{\"a\":1,\"b\":2}").Count);
object any = JsonSerializer.Deserialize<object>("""{"x":[1,"two",null]}""");
Console.WriteLine(any.GetType().Name + " " + any);
var objects = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"n":1.5,"s":"t","b":false}""");
foreach (var kv in objects) Console.WriteLine($"  {kv.Key}: {kv.Value} ({kv.Value.GetType().Name})");
var element = JsonSerializer.Deserialize<JsonElement>("""[1, {"a": 2}]""");
Console.WriteLine(element.ValueKind + " " + element[1].GetProperty("a").GetInt32());
Console.WriteLine(JsonSerializer.Deserialize<int[]>(" [ 1 , 2 ] ").Length + " " + JsonSerializer.Deserialize<IEnumerable<string>>("[\"q\"]").First());
Console.WriteLine(JsonSerializer.Deserialize<Queue<int>>("[1,2,3]").Peek() + " " + JsonSerializer.Deserialize<Stack<int>>("[1,2,3]").Peek());
Console.WriteLine(JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>("{\"k\":\"v\"}")["k"]);

// Reader options.
var lenient = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true };
var c = JsonSerializer.Deserialize<Customer>("/* c */ {\"name\": \"lenient\", // x\n \"TIER\": 1,}", lenient);
Console.WriteLine(c.Name + " " + c.Tier);
var dup = JsonSerializer.Deserialize<Customer>("{\"Name\":\"first\",\"Name\":\"second\"}");
Console.WriteLine(dup.Name);

// Errors.
Try(() => JsonSerializer.Deserialize<Order>("{\"Id\": \"x\"}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Lines\": [{\"Quantity\": 1.5}]}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Customer\": {\"Name\": 5}}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Tags\": {}}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Id\": 1,}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Id\": 1"));
Try(() => JsonSerializer.Deserialize<Order>(""));
Try(() => JsonSerializer.Deserialize<Order>("[]"));
Try(() => JsonSerializer.Deserialize<int>("null"));
Try(() => JsonSerializer.Deserialize<int>("2147483648"));
Try(() => JsonSerializer.Deserialize<byte>("-1"));
Try(() => JsonSerializer.Deserialize<Point>("{\"X\": null}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"PlacedAt\": \"not a date\"}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Initial\": \"ab\"}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Data\": \"%%\"}"));
Try(() => JsonSerializer.Deserialize<Order>("{\"Counts\": {\"x\": 1}}"));
Try(() => JsonSerializer.Deserialize<Rec>("{\"Name\": \"a\"} x"));
Try(() => JsonSerializer.Deserialize<Required>("{\"Optional\": 1}"));
Try(() => JsonSerializer.Deserialize<Required>("{}"));
Console.WriteLine(JsonSerializer.Deserialize<Required>("{\"Name\":\"n\",\"Age\":0}").Name);
Try(() => JsonSerializer.Deserialize<Order>((string)null));
Try(() => JsonSerializer.Deserialize<Tier>("\"Gold\""));

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (JsonException e) { Console.WriteLine($"JsonException: {e.Message} | Path={e.Path} Line={e.LineNumber} Pos={e.BytePositionInLine} Inner={e.InnerException?.GetType().Name}"); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public enum Tier { Bronze, Silver, Gold }

public class Customer
{
    public string Name { get; set; }
    public string Email { get; set; }
    public Tier Tier { get; set; }
}

public class OrderLine
{
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public record Rec(string Name, int Count);

public class WithCtor
{
    public WithCtor(int a, string b) { A = a; B = b; }
    public int A { get; }
    public string B { get; }
    public List<int> C { get; set; }
    public string Set { get; set; } = "default";
}

public class Required
{
    public required string Name { get; set; }
    [JsonRequired] public int Age { get; set; }
    public int Optional { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; }
    public List<OrderLine> Lines { get; set; }
    public string[] Tags { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
    public Dictionary<int, double> Counts { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public Point Origin { get; set; }
    public int[][] Grid { get; set; }
    public HashSet<long> Seen { get; set; }
    public TimeSpan Duration { get; set; }
    public char Initial { get; set; }
    public byte[] Data { get; set; }
}
