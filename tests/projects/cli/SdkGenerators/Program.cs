using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

Console.WriteLine(JsonSerializer.Serialize(new Point(1, 2), PointContext.Default.Point));
Console.WriteLine(JsonSerializer.Deserialize("{\"X\":5,\"Y\":-3}", PointContext.Default.Point));
Console.WriteLine(Patterns.Digits().Replace("a1b22c333", "#"));
Console.WriteLine(Patterns.Digits().GetType() == typeof(Regex) || Patterns.Digits().GetType().IsSubclassOf(typeof(Regex)));
// The LibraryImport stub must exist (CS8795 otherwise); it is not called, the library differs per OS.
Console.WriteLine(args.Length > 99 ? Native.Missing("x") : 0);

record Point(int X, int Y);

[JsonSerializable(typeof(Point))]
partial class PointContext : JsonSerializerContext;

static partial class Patterns
{
    [GeneratedRegex(@"\d+")]
    public static partial Regex Digits();
}

static partial class Native
{
    [LibraryImport("csharpc-test-missing-library", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Missing(string s);
}
