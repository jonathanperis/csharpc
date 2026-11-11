#:property JsonSerializerIsReflectionEnabledByDefault=true
#nullable enable
// Reflection-based System.Text.Json builds its converters with MakeGenericType from the types the call names: types with a parameterized
// constructor (up to four parameters, and more), enums, nullable values, collections of records, and values of any type held as object.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum Level { Low, High }

public record Small(string Name, int Age);

public record Many(int A, string B, double C, bool D, long E, Level F);

public class WithCtor
{
    public WithCtor(string id, Level level, int? count) { Id = id; Level = level; Count = count; }
    public string Id { get; }
    public Level Level { get; }
    public int? Count { get; }
}

public class Holder
{
    public List<Small> Smalls { get; set; } = new();
    public Dictionary<string, List<Level>> ByName { get; set; } = new();
    public Small[] Array { get; set; } = [];
    public object? Any { get; set; }
}

static class Program
{
    static void Main()
    {
        var holder = new Holder
        {
            Smalls = { new Small("a", 1), new Small("b", 2) },
            ByName = { ["x"] = new List<Level> { Level.Low, Level.High } },
            Array = [new Small("c", 3)],
            Any = new List<object> { 1, "two", new Small("d", 4) },
        };
        string json = JsonSerializer.Serialize(holder);
        Console.WriteLine(json);

        Console.WriteLine(JsonSerializer.Serialize(new Many(1, "b", 2.5, true, 5, Level.High)));
        var many = JsonSerializer.Deserialize<Many>("{\"A\":1,\"B\":\"b\",\"C\":2.5,\"D\":true,\"E\":5,\"F\":1}")!;
        Console.WriteLine($"{many.A} {many.B} {many.C} {many.D} {many.E} {many.F}");

        var withCtor = JsonSerializer.Deserialize<WithCtor>("{\"id\":\"w\",\"level\":1,\"count\":7}")!;
        Console.WriteLine($"{withCtor.Id} {withCtor.Level} {withCtor.Count}");
        Console.WriteLine(JsonSerializer.Serialize(withCtor));

        var back = JsonSerializer.Deserialize<Holder>("{\"Smalls\":[{\"Name\":\"z\",\"Age\":9}],\"ByName\":{\"k\":[1,0]},\"Array\":[],\"Any\":null}")!;
        Console.WriteLine($"{back.Smalls[0]} {string.Join(",", back.ByName["k"])} {back.Array.Length} {back.Any is null}");
    }
}
