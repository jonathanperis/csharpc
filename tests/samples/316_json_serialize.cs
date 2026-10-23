#:property PublishAot=false
// JsonSerializer.Serialize for user types without a JsonSerializerContext (.NET's reflection-based
// path; csharpc generates the metadata at compile time): properties in declaration order (derived
// before base), collections, dictionaries, nullables, enums, structs, records, numbers and dates.
using System.Collections.Generic;
using System.Text.Json;

var order = new Order
{
    Id = 42,
    Customer = new Customer { Name = "Ann", Email = null, Tier = Tier.Gold },
    Lines = new List<OrderLine>
    {
        new OrderLine { Sku = "A-1", Quantity = 2, Price = 9.99m },
        new OrderLine { Sku = "B<2>", Quantity = 1, Price = 0.1m + 0.2m },
    },
    Tags = new[] { "rush", "gift" },
    Attributes = new Dictionary<string, string> { ["color"] = "red", ["size"] = "L" },
    Counts = new Dictionary<int, double> { [1] = 0.5, [10] = 1e21 },
    PlacedAt = new DateTime(2024, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
    ShippedAt = null,
    Discount = 0.15,
    Weight = 1.25f,
    Origin = new Point(3, -4),
    Express = true,
    Note = "line1\nline2\t\"quoted\" é",
    Grid = new int[][] { new[] { 1, 2 }, new int[0] },
    Seen = new HashSet<long> { 5, 3 },
    Duration = TimeSpan.FromMinutes(90.5),
    Initial = 'Z',
    Data = new byte[] { 0, 255, 128 },
};

Console.WriteLine(JsonSerializer.Serialize(order));
Console.WriteLine(JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new Derived { BaseValue = 1, DerivedValue = 2, Shadowed = "derived" }));
Console.WriteLine(JsonSerializer.Serialize(new Point(1, 2)));
Console.WriteLine(JsonSerializer.Serialize(new Rec("r", 7)));
Console.WriteLine(JsonSerializer.Serialize(new List<Rec> { new Rec("a", 1), null }));
Console.WriteLine(JsonSerializer.Serialize<object>(new Rec("as object", 2)));
Console.WriteLine(JsonSerializer.Serialize(new object[] { 1, "two", 3.5, true, null, Tier.Silver, new Point(0, 0) }));
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["n"] = 1, ["s"] = "x", ["list"] = new List<object> { 1, "y" }, ["nested"] = new Dictionary<string, int> { ["k"] = 2 } }));
Console.WriteLine(JsonSerializer.Serialize(Tier.Gold) + " " + JsonSerializer.Serialize((Tier)42) + " " + JsonSerializer.Serialize(Flags.A | Flags.C));

// Primitives at the root, special values.
Console.WriteLine(JsonSerializer.Serialize(123) + " " + JsonSerializer.Serialize(-0.0) + " " + JsonSerializer.Serialize(double.MaxValue) + " " + JsonSerializer.Serialize(float.Epsilon));
Console.WriteLine(JsonSerializer.Serialize(decimal.MaxValue) + " " + JsonSerializer.Serialize(1.10m) + " " + JsonSerializer.Serialize(long.MinValue) + " " + JsonSerializer.Serialize(ulong.MaxValue));
Console.WriteLine(JsonSerializer.Serialize((string)null) + " " + JsonSerializer.Serialize((int?)null) + " " + JsonSerializer.Serialize((int?)5) + " " + JsonSerializer.Serialize('x'));
Console.WriteLine(JsonSerializer.Serialize(new DateTime(2000, 1, 1)) + " " + JsonSerializer.Serialize(new DateTime(2000, 1, 1, 0, 0, 0, 1, DateTimeKind.Utc)) + " " + JsonSerializer.Serialize(TimeSpan.Zero));
Console.WriteLine(JsonSerializer.Serialize(new List<int>()) + " " + JsonSerializer.Serialize(new Dictionary<string, int>()) + " " + JsonSerializer.Serialize(new Customer()));
Console.WriteLine(JsonSerializer.Serialize(new Queue<int>(new[] { 1, 2, 3 })) + " " + JsonSerializer.Serialize(new Stack<int>(new[] { 1, 2, 3 })));
Console.WriteLine(JsonSerializer.Serialize<IEnumerable<int>>(new List<int> { 4, 5 }) + " " + JsonSerializer.Serialize<IReadOnlyList<string>>(new[] { "x" }));
var utf8 = JsonSerializer.SerializeToUtf8Bytes(new Point(5, 6));
Console.WriteLine(utf8.Length + " " + System.Text.Encoding.UTF8.GetString(utf8));
Console.WriteLine(JsonSerializer.Serialize(order.Customer, typeof(Customer)));

try { JsonSerializer.Serialize(double.NaN); }
catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { JsonSerializer.Serialize(new Customer(), typeof(Point)); }
catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }

public enum Tier { Bronze, Silver, Gold }

[Flags]
public enum Flags { None = 0, A = 1, B = 2, C = 4 }

public class Customer
{
    public string Name { get; set; }
    public string Email { get; set; }
    public Tier Tier { get; set; }
    public int ReadOnly => 7;
    public static int Static { get; set; } = 3;
    public int Field = 9;
    private int Private { get; set; } = 1;
    internal int Internal { get; set; } = 2;
}

public class OrderLine
{
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public struct Point
{
    public Point(int x, int y) { X = x; Y = y; }
    public int X { get; set; }
    public int Y { get; set; }
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public record Rec(string Name, int Count);

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
    public double Discount { get; set; }
    public float Weight { get; set; }
    public Point Origin { get; set; }
    public bool Express { get; set; }
    public string Note { get; set; }
    public int[][] Grid { get; set; }
    public HashSet<long> Seen { get; set; }
    public TimeSpan Duration { get; set; }
    public char Initial { get; set; }
    public byte[] Data { get; set; }
}

public class Base
{
    public int BaseValue { get; set; }
    public string Shadowed { get; set; } = "base";
}

public class Derived : Base
{
    public int DerivedValue { get; set; }
    public new string Shadowed { get; set; }
}
