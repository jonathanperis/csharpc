#:property JsonSerializerIsReflectionEnabledByDefault=true
// Code the program never runs may use what csharpc can't compile yet (here reflection-based JSON of a
// file-local record and of an anonymous type, as in the docs' System.Text.Json samples): like NativeAOT,
// csharpc compiles the program; such a method becomes a stub (warning CSLLVM0007) that would end the
// program with the diagnostic if it were ever called.
using System;
using System.Text.Json;

Console.WriteLine("start");
Console.WriteLine(new Reachable().Describe());
Console.WriteLine(Samples.Twice(21));
Console.WriteLine("end");

public class Reachable
{
    public string Describe() => "reachable: " + JsonSerializer.Serialize(new Point(1, 2));
}

public record Point(int X, int Y);

public static class Samples
{
    public static int Twice(int x) => x * 2;

    // Never called.
    public static void RoundtripFileLocal()
    {
        var original = new SeriesDataPoint(3.791m, new DateOnly(2002, 1, 13));
        string json = JsonSerializer.Serialize(original);
        Console.WriteLine(json);
        Console.WriteLine(JsonSerializer.Deserialize<SeriesDataPoint>(json) == original);
    }

    // Never called either.
    public static void Anonymous() => Console.WriteLine(JsonSerializer.Serialize(new { X = 1, Y = 2 }));

    // Only called by the methods above.
    public static string Helper() => JsonSerializer.Serialize(new SeriesDataPoint(1m, default));
}

sealed file record SeriesDataPoint(decimal Value, DateOnly Date);
