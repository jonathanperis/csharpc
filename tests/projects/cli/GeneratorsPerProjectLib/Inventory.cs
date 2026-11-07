using System.Text.Json.Serialization;

namespace Warehouse;

public sealed record Item(string Name, int Quantity);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Item))]
[JsonSerializable(typeof(List<Item>))]
public partial class InventoryJsonContext : JsonSerializerContext
{
}
