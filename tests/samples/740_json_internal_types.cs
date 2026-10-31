#:property PublishAot=false
// Reflection-based System.Text.Json over internal types and members (the default accessibility of a
// program's classes), private [JsonInclude] members, derived values passed as their base, a type given
// as typeof / GetType(), and roots inside generic code. Through IL (`csharpc app.dll`) the metadata is
// generated from the program's method bodies; it must match what source gets.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.WriteLine(JsonSerializer.Serialize(new Inner { A = 1, B = "x" }));
var back = JsonSerializer.Deserialize<Inner>("""{"A":2,"B":"y","C":9,"D":8}""");
Console.WriteLine(back.A + " " + back.B + " " + back.C + " " + back.D);
Console.WriteLine(JsonSerializer.Serialize(new Ordered()));
Console.WriteLine(JsonSerializer.Deserialize<Ordered>("""{"secret":7,"Internal":6,"Z":1}""").Describe());
Console.WriteLine(JsonSerializer.Serialize<Animal>(new Dog { Name = "Rex", Good = true }));
Animal animal = new Dog { Name = "Fido" };
Console.WriteLine(JsonSerializer.Serialize(animal, animal.GetType()));
Console.WriteLine(JsonSerializer.Serialize(new Holder(), typeof(Holder)));
Console.WriteLine(Wrap(new Pair<int, string>(1, "one")));
Console.WriteLine(Wrap(new List<Inner> { new() { A = 3 } }));
Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, Inner> { ["k"] = new() { B = "v" } }));
Console.WriteLine(JsonSerializer.Serialize(new Line { From = new(1, 2), To = new(3, 4) }));

static string Wrap<T>(T value) => JsonSerializer.Serialize(new Envelope<T> { Value = value, Kind = typeof(T).Name });

class Inner
{
    public int A { get; set; }
    public string B { get; set; }
    internal int C { get; set; } = 3;
    [JsonInclude] internal int D { get; set; } = 4;
}

internal class Ordered
{
    [JsonInclude, JsonPropertyName("secret")] private int Secret { get; set; } = 42;
    [JsonInclude] internal int Internal = 5;
    public int Z { get; set; } = 26;
    public string Describe() => Secret + " " + Internal + " " + Z;
}

class Animal
{
    public string Name { get; set; }
}

sealed class Dog : Animal
{
    public bool Good { get; set; }
}

class Holder
{
    public int[] Values { get; set; } = [1, 2];
    public Inner Item { get; set; }
}

record Pair<TKey, TValue>(TKey Key, TValue Value);

class Envelope<T>
{
    public T Value { get; set; }
    public string Kind { get; set; }
}

readonly record struct Point(int X, int Y);

class Line
{
    public Point From { get; set; }
    public Point To { get; set; }
}
