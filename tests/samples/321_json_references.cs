#:property PublishAot=false
// ReferenceHandler.Preserve / IgnoreCycles, cycle detection and MaxDepth on write and read.
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

var root = new Node { Name = "root" };
var child = new Node { Name = "child", Parent = root };
root.Children.Add(child);
root.Children.Add(child);
root.Self = root;

var preserve = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
string json = JsonSerializer.Serialize(root, preserve);
Console.WriteLine(json);
var back = JsonSerializer.Deserialize<Node>(json, preserve);
Console.WriteLine($"{back.Name} {back.Self == back} {back.Children.Count} {back.Children[0] == back.Children[1]} {back.Children[0].Parent == back}");

Console.WriteLine(JsonSerializer.Serialize(root, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles }));
Console.WriteLine(JsonSerializer.Serialize(new[] { child, child }, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles }));
var shared = new List<int> { 1, 2 };
Console.WriteLine(JsonSerializer.Serialize(new Pair { A = shared, B = shared }, preserve));
var pair = JsonSerializer.Deserialize<Pair>("""{"$id":"1","A":{"$id":"2","$values":[3]},"B":{"$ref":"2"}}""", preserve);
Console.WriteLine(pair.A == pair.B ? "same list " + pair.A[0] : "different lists");
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, Node> { ["a"] = child, ["b"] = child }, preserve));

Try(() => JsonSerializer.Serialize(root));
Try(() => JsonSerializer.Deserialize<Node>("""{"$id":"1","Name":"x","Self":{"$ref":"2"}}""", preserve));
Try(() => JsonSerializer.Deserialize<Node>("""{"$id":"1","$id":"2"}""", preserve));
Try(() => JsonSerializer.Deserialize<Node>("""{"Self":{"$ref":"1","Name":"x"}}""", preserve));
Try(() => JsonSerializer.Deserialize<Node>("""{"$id":1}""", preserve));
Try(() => JsonSerializer.Deserialize<Node>("""{"$unknown":1}""", preserve));
Try(() => JsonSerializer.Deserialize<Node>("""{"$id":"1"}""").Name);

// Depth limits.
var deep = new Node { Name = "0" };
var cur = deep;
for (int i = 1; i < 5; i++) { cur.Self = new Node { Name = i.ToString() }; cur = cur.Self; }
Try(() => JsonSerializer.Serialize(deep, new JsonSerializerOptions { MaxDepth = 3 }));
Try(() => JsonSerializer.Serialize(deep, new JsonSerializerOptions { MaxDepth = 6 }).Length);
Try(() => JsonSerializer.Deserialize<Node>("""{"Self":{"Self":{"Self":{}}}}""", new JsonSerializerOptions { MaxDepth = 2 }));
Try(() => JsonSerializer.Deserialize<List<List<List<int>>>>("[[[1]]]", new JsonSerializerOptions { MaxDepth = 2 }));
Try(() => JsonSerializer.Deserialize<List<List<List<int>>>>("[[[1]]]", new JsonSerializerOptions { MaxDepth = 3 }).Count);

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

public class Node
{
    public string Name { get; set; }
    public Node Parent { get; set; }
    public Node Self { get; set; }
    public List<Node> Children { get; set; } = new();
}

public class Pair
{
    public List<int> A { get; set; }
    public List<int> B { get; set; }
}
