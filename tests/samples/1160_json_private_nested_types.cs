#:property JsonSerializerIsReflectionEnabledByDefault=true
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// Reflection-based System.Text.Json of private and protected nested types, which the compile-time metadata can only
// reach from inside their declaring class: through the program as IL (tests/run.sh --via-il) the metadata is generated
// in another assembly, so these types were a diagnostic there. The types keep their real accessibility for reflection.
public class Outer
{
    private class Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private record Named(string Name, int Age);

    protected struct Pair
    {
        public string Left { get; set; }
        public double Right { get; set; }
    }

    private class Shape
    {
        [JsonInclude] private int sides = 3;
        public string Label { get; set; } = "";
        public Point? Corner { get; set; }
        public List<Point> Others { get; set; } = [];
        public Dictionary<string, Named> People { get; set; } = [];
        public int Sides => sides;
    }

    internal class Holder
    {
        private class Secret
        {
            public string Code { get; set; } = "";
        }

        public static string Run()
        {
            var secret = JsonSerializer.Deserialize<Secret>("{\"Code\":\"abc\"}")!;
            return secret.Code + " " + JsonSerializer.Serialize(secret);
        }
    }

    public static void Run()
    {
        var point = new Point { X = 3, Y = 4 };
        var json = JsonSerializer.Serialize(point);
        Console.WriteLine(json);
        var back = JsonSerializer.Deserialize<Point>(json)!;
        Console.WriteLine($"{back.X},{back.Y}");

        Console.WriteLine(JsonSerializer.Serialize(new Named("Ada", 36)));
        Console.WriteLine(JsonSerializer.Deserialize<Named>("{\"Name\":\"Bob\",\"Age\":41}"));

        Console.WriteLine(JsonSerializer.Serialize(new Pair { Left = "l", Right = 2.5 }));
        Console.WriteLine(JsonSerializer.Deserialize<Pair>("{\"Left\":\"x\",\"Right\":0.25}").Right);

        var shape = new Shape { Label = "tri", Corner = point, Others = [new Point { X = 1, Y = 2 }], People = { ["a"] = new Named("A", 1) } };
        var shapeJson = JsonSerializer.Serialize(shape, new JsonSerializerOptions { WriteIndented = false });
        Console.WriteLine(shapeJson);
        var shapeBack = JsonSerializer.Deserialize<Shape>(shapeJson)!;
        Console.WriteLine($"{shapeBack.Label} {shapeBack.Sides} {shapeBack.Corner!.X} {shapeBack.Others[0].Y} {shapeBack.People["a"].Name}");

        Console.WriteLine(JsonSerializer.Serialize(new List<Point> { point, new Point { X = 9 } }));
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, Point> { ["p"] = point }));
        Console.WriteLine(JsonSerializer.Deserialize<Point[]>("[{\"X\":1,\"Y\":2},{\"X\":3,\"Y\":4}]")!.Length);
        Console.WriteLine(JsonSerializer.Serialize<object>(point));
        Console.WriteLine(Holder.Run());

        foreach (var type in new[] { typeof(Point), typeof(Named), typeof(Pair), typeof(Shape), typeof(Holder) })
            Console.WriteLine($"{type.Name}: private={type.IsNestedPrivate} family={type.IsNestedFamily} internal={type.IsNestedAssembly} public={type.IsNestedPublic}");
    }
}

public static class Program
{
    public static void Main() => Outer.Run();
}
