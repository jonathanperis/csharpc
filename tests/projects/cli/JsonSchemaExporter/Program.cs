using System.Text.Json.Schema;
using System.Text.Json.Serialization;

var node = JsonSchemaExporter.GetJsonSchemaAsNode(Ctx.Default.Options, typeof(Point), new JsonSchemaExporterOptions
{
	TransformSchemaNode = (context, schema) =>
	{
		if (context.PropertyInfo is { } property)
			schema["description"] = "the " + property.Name;
		return schema;
	},
});
Console.WriteLine(node.ToJsonString());

record Point(int X, int Y);

[JsonSerializable(typeof(Point))]
partial class Ctx : JsonSerializerContext;
