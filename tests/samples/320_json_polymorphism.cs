#:property PublishAot=false
// Polymorphic serialization: [JsonPolymorphic]/[JsonDerivedType] with string and integer
// discriminators, custom discriminator names, interfaces, unknown derived types, and
// deserializing back to the derived type (with its error messages).
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

var shapes = new List<Shape> { new Circle { Name = "c", Radius = 1.5 }, new Square { Name = "s", Side = 2 }, new Shape { Name = "plain" }, null };
string json = JsonSerializer.Serialize(shapes);
Console.WriteLine(json);
foreach (var shape in JsonSerializer.Deserialize<List<Shape>>(json))
    Console.WriteLine(shape == null ? "null" : $"{shape.GetType().Name} {shape.Name} {shape.Describe()}");
Console.WriteLine(JsonSerializer.Serialize<Shape>(new Circle { Name = "root", Radius = 3 }));
Console.WriteLine(JsonSerializer.Serialize(new Circle { Name = "static type", Radius = 3 }));
Console.WriteLine(JsonSerializer.Serialize(new Drawing { Main = new Square { Side = 9 }, Items = new Shape[] { new Circle() } }));

Console.WriteLine(JsonSerializer.Serialize<IAnimal>(new Dog { Name = "Rex", Barks = true }));
Console.WriteLine(JsonSerializer.Serialize(new IAnimal[] { new Cat { Name = "Tom", Lives = 9 }, new Dog { Name = "Fido" } }));
var animals = JsonSerializer.Deserialize<IAnimal[]>("""[{"kind":2,"Lives":3,"Name":"Kit"},{"kind":1,"Name":"D","Barks":true}]""");
foreach (var a in animals) Console.WriteLine(a.GetType().Name + " " + a.Name);

// Unknown derived types.
Console.WriteLine(JsonSerializer.Serialize<Vehicle>(new SportsCar { Wheels = 4, TopSpeed = 300 }));
Try(() => JsonSerializer.Serialize<Shape>(new Triangle { Name = "t" }));
Try(() => JsonSerializer.Deserialize<Shape>("""{"$type":"hexagon","Name":"h"}"""));
Try(() => JsonSerializer.Deserialize<Shape>("""{"Name":"first","$type":"circle"}"""));
Console.WriteLine(JsonSerializer.Deserialize<Shape>("""{"Name":"no discriminator"}""").GetType().Name);
Try(() => JsonSerializer.Deserialize<IAnimal>("""{"Name":"x"}"""));
Try(() => JsonSerializer.Deserialize<IAnimal>("""{"kind":"1"}"""));
var allowed = JsonSerializer.Deserialize<Shape>("""{"$type":"circle","Radius":2,"Name":"ok"}""", new JsonSerializerOptions { AllowOutOfOrderMetadataProperties = true });
Console.WriteLine(allowed.GetType().Name + " " + allowed.Describe());

// Polymorphism configured through JsonTypeInfo modifiers.
var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
resolver.Modifiers.Add(info =>
{
    if (info.Type == typeof(Vehicle))
    {
        info.PolymorphismOptions = new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions { TypeDiscriminatorPropertyName = "_t" };
        info.PolymorphismOptions.DerivedTypes.Add(new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(Truck), "truck"));
    }
});
var modified = new JsonSerializerOptions { TypeInfoResolver = resolver };
Console.WriteLine(JsonSerializer.Serialize<Vehicle>(new Truck { Wheels = 18, Load = 20.5 }, modified));
Console.WriteLine(JsonSerializer.Deserialize<Vehicle>("""{"_t":"truck","Load":1}""", modified) is Truck t ? "Truck " + t.Load : "?");

static void Try(Func<object> f)
{
    try { Console.WriteLine("ok: " + f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
[JsonDerivedType(typeof(Shape), "base")]
public class Shape
{
    public string Name { get; set; }
    public virtual string Describe() => "shape";
}

public class Circle : Shape
{
    public double Radius { get; set; }
    public override string Describe() => "r=" + Radius;
}

public class Square : Shape
{
    public int Side { get; set; }
    public override string Describe() => "side=" + Side;
}

public class Triangle : Shape { }

public class Drawing
{
    public Shape Main { get; set; }
    public Shape[] Items { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Dog), 1)]
[JsonDerivedType(typeof(Cat), 2)]
public interface IAnimal
{
    string Name { get; set; }
}

public class Dog : IAnimal
{
    public string Name { get; set; }
    public bool Barks { get; set; }
}

public class Cat : IAnimal
{
    public string Name { get; set; }
    public int Lives { get; set; }
}

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(Car))]
public class Vehicle
{
    public int Wheels { get; set; }
}

public class Car : Vehicle
{
    public string Brand { get; set; } = "generic";
}

public class SportsCar : Car
{
    public int TopSpeed { get; set; }
}

public class Truck : Vehicle
{
    public double Load { get; set; }
}
