#:property PublishAot=false
#nullable enable
// Reflection-based System.Text.Json over a class whose members are BCL types: the resolver asks every type of the graph for its
// public constructors (primitives and enums have none) and for its attributes, whichever library implements the serializer.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

enum Color { Red, Green = 5 }

class Inner
{
    public int Id { get; set; }
    public string? Label { get; set; }
}

class Everything
{
    public bool Flag { get; set; }
    public byte B { get; set; }
    public char C { get; set; }
    public int I { get; set; }
    public long L { get; set; }
    public double D { get; set; }
    public decimal M { get; set; }
    public string? S { get; set; }
    public DateTime T { get; set; }
    public Guid G { get; set; }
    public TimeSpan Span { get; set; }
    public Color Shade { get; set; }
    public int? Maybe { get; set; }
    public int[] Numbers { get; set; } = [];
    public List<int> Items { get; set; } = new();
    public Dictionary<string, int> Map { get; set; } = new();
    public Inner? Child { get; set; }
    public List<Inner> Children { get; set; } = new();
}

static class Program
{
    static void Main()
    {
        Console.WriteLine(typeof(int).GetConstructors().Length);
        Console.WriteLine(typeof(Color).GetConstructors().Length);
        Console.WriteLine(typeof(double).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length);

        var value = new Everything
        {
            Flag = true, B = 7, C = 'x', I = 3, L = -4, D = 1.5, M = 2.25m, S = "text",
            T = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), G = Guid.Empty, Span = TimeSpan.FromMinutes(90), Shade = Color.Green,
            Maybe = 9, Numbers = [1, 2, 3], Items = { 4, 5 }, Map = { ["a"] = 1, ["b"] = 2 },
            Child = new Inner { Id = 1, Label = "one" }, Children = { new Inner { Id = 2 }, new Inner { Id = 3, Label = "three" } },
        };
        string json = JsonSerializer.Serialize(value);
        Console.WriteLine(json);
        var back = JsonSerializer.Deserialize<Everything>(json)!;
        Console.WriteLine($"{back.Flag} {back.B} {back.C} {back.I} {back.L} {back.D} {back.M} {back.S} {back.T:o} {back.G} {back.Span} {back.Shade} {back.Maybe}");
        Console.WriteLine($"{string.Join(",", back.Numbers)} {string.Join(",", back.Items)} {back.Map["b"]} {back.Child!.Label} {back.Children.Count} {back.Children[1].Label}");
        Console.WriteLine(JsonSerializer.Serialize(back) == json);
    }
}
