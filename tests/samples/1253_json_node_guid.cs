// Guid in the JSON DOM: the implicit and explicit conversions of JsonNode, JsonValue.Create, GetValue<Guid>
// and TryGetValue<Guid> over a created value and a parsed one, and its value kind.
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

var id = new Guid("01234567-89ab-cdef-0123-456789abcdef");
JsonNode implicitNode = id;
Guid? none = null;
JsonNode? nullNode = none;
Console.WriteLine(implicitNode.ToJsonString() + " " + implicitNode.GetValueKind() + " " + (nullNode is null));
Console.WriteLine(JsonValue.Create(id).ToJsonString() + " " + JsonValue.Create((Guid?)id)!.ToJsonString() + " " + (JsonValue.Create(none) is null));
Console.WriteLine((Guid)implicitNode + " " + ((Guid?)implicitNode) + " " + implicitNode.GetValue<Guid>());
var obj = new JsonObject { ["id"] = id, ["ids"] = new JsonArray(id, Guid.Empty) };
Console.WriteLine(obj.ToJsonString());
var parsed = JsonNode.Parse("""{"id":"fedcba98-7654-3210-fedc-ba9876543210","bad":"nope","n":1}""")!;
Console.WriteLine(parsed["id"]!.GetValue<Guid>() + " " + (Guid)parsed["id"]! + " " + parsed["id"]!.AsValue().TryGetValue(out Guid g) + " " + g);
Console.WriteLine(parsed["bad"]!.AsValue().TryGetValue(out Guid g2) + " " + g2);
Console.WriteLine(parsed["n"]!.AsValue().TryGetValue(out Guid g3) + " " + g3);
try { parsed["bad"]!.GetValue<Guid>(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
Console.WriteLine(JsonNode.DeepEquals(implicitNode, JsonNode.Parse("\"01234567-89ab-cdef-0123-456789abcdef\"")));
