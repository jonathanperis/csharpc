using System.Text.Json;
using System.Text.Json.Serialization;
using Warehouse;

// The library's context was generated on the library's compilation (camelCase from its own options);
// the app's context and the reflection-style call are the app's.
var items = new List<Item> { new("bolt", 40), new("nut", 12) };
var json = JsonSerializer.Serialize(items, InventoryJsonContext.Default.ListItem);
Console.WriteLine(json);
Console.WriteLine(JsonSerializer.Deserialize(json, InventoryJsonContext.Default.ListItem)!.Count);

Console.WriteLine(JsonSerializer.Serialize(new Order(7, "bolt"), OrderJsonContext.Default.Order));
Console.WriteLine(JsonSerializer.Serialize(new Shipment("Lisbon", 3)));
Console.WriteLine(typeof(InventoryJsonContext).Assembly.GetName().Name);
Console.WriteLine(typeof(OrderJsonContext).Assembly.GetName().Name);

public sealed record Order(int Id, string Sku);

public sealed record Shipment(string City, int Boxes);

[JsonSerializable(typeof(Order))]
internal partial class OrderJsonContext : JsonSerializerContext
{
}
