// JsonSchemaExporter for the converters a source-generated context names beyond the numeric primitives:
// Int128, UInt128, Half, DateOnly, TimeOnly, Uri, Version and (Read)Memory<byte>. Each converter reports its own
// schema (format "date"/"time"/"uri", the version pattern, base64 content encoding, IEEE number patterns).
using System;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

record Rec(Int128 I, UInt128 U, Half H, DateOnly D, TimeOnly T, Uri Uri, Version V, ReadOnlyMemory<byte> Ro, Memory<byte> Rw);

[JsonSerializable(typeof(Rec))]
[JsonSerializable(typeof(Int128))]
[JsonSerializable(typeof(UInt128))]
[JsonSerializable(typeof(Half))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(Uri))]
[JsonSerializable(typeof(Version))]
[JsonSerializable(typeof(ReadOnlyMemory<byte>))]
[JsonSerializable(typeof(Memory<byte>))]
partial class Ctx : JsonSerializerContext;

class Program
{
    static void Show(string name, JsonSerializerOptions options, Type type)
    {
        try { Console.WriteLine(name + ": " + JsonSchemaExporter.GetJsonSchemaAsNode(options, type).ToJsonString()); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        var o = Ctx.Default.Options;
        Show("Int128", o, typeof(Int128));
        Show("UInt128", o, typeof(UInt128));
        Show("Half", o, typeof(Half));
        Show("DateOnly", o, typeof(DateOnly));
        Show("TimeOnly", o, typeof(TimeOnly));
        Show("Uri", o, typeof(Uri));
        Show("Version", o, typeof(Version));
        Show("ReadOnlyMemory<byte>", o, typeof(ReadOnlyMemory<byte>));
        Show("Memory<byte>", o, typeof(Memory<byte>));
        Show("Rec", o, typeof(Rec));

        var strings = new JsonSerializerOptions(o) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        strings.MakeReadOnly();
        Show("Int128 (AllowReadingFromString)", strings, typeof(Int128));
        Show("Half (AllowReadingFromString)", strings, typeof(Half));
    }
}
