// System.Text.Json.Utf8JsonWriter: structure, indentation options, numbers, dates, literals,
// comments, raw values and the validation errors of an invalid sequence of writes.
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

static string Write(JsonWriterOptions options, Action<Utf8JsonWriter> body)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, options))
    {
        body(writer);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}

static void Sample(Utf8JsonWriter w)
{
    w.WriteStartObject();
    w.WriteString("name", "csharpc");
    w.WriteNumber("int", 42);
    w.WriteNumber("long", long.MinValue);
    w.WriteNumber("ulong", ulong.MaxValue);
    w.WriteNumber("double", 0.1 + 0.2);
    w.WriteNumber("small", 1e-7);
    w.WriteNumber("big", 1.5e300);
    w.WriteNumber("float", 3.14159f);
    w.WriteNumber("decimal", 1234.5000m);
    w.WriteBoolean("yes", true);
    w.WriteNull("nothing");
    w.WriteString("date", new DateTime(2019, 7, 26, 10, 0, 0, DateTimeKind.Utc));
    w.WriteString("unspecified", new DateTime(2019, 7, 26, 10, 0, 0, 500));
    w.WriteStartArray("items");
    w.WriteNumberValue(1);
    w.WriteStringValue("two");
    w.WriteStartObject();
    w.WriteEndObject();
    w.WriteStartArray();
    w.WriteEndArray();
    w.WriteStartArray();
    w.WriteNullValue();
    w.WriteBooleanValue(false);
    w.WriteEndArray();
    w.WriteEndArray();
    w.WritePropertyName("nested");
    w.WriteStartObject();
    w.WriteBase64String("bytes", new byte[] { 1, 2, 3, 250, 251, 252 });
    w.WriteEndObject();
    w.WriteEndObject();
}

Console.WriteLine(Write(default, Sample));
Console.WriteLine(Write(new JsonWriterOptions { Indented = true }, Sample));
Console.WriteLine(Write(new JsonWriterOptions { Indented = true, IndentCharacter = '\t', IndentSize = 1 }, Sample));
Console.WriteLine(Write(new JsonWriterOptions { Indented = true, IndentSize = 4, NewLine = "\r\n" }, Sample).Replace("\r", "<CR>"));

// Top-level values, comments and raw JSON.
Console.WriteLine(Write(default, w => w.WriteNumberValue(-0.0)));
Console.WriteLine(Write(default, w => w.WriteStringValue("only a string")));
Console.WriteLine(Write(new JsonWriterOptions { Indented = true }, w =>
{
    w.WriteCommentValue("leading comment");
    w.WriteStartObject();
    w.WriteCommentValue("inside");
    w.WritePropertyName("raw");
    w.WriteRawValue("[1, 2,   3]");
    w.WriteString("after", "x");
    w.WriteEndObject();
}));

// IBufferWriter output, BytesPending/BytesCommitted and Flush.
var buffer = new ArrayBufferWriter<byte>();
using (var w = new Utf8JsonWriter(buffer))
{
    w.WriteStartArray();
    w.WriteNumberValue(1);
    Console.WriteLine($"pending={w.BytesPending} committed={w.BytesCommitted} depth={w.CurrentDepth}");
    w.Flush();
    Console.WriteLine($"pending={w.BytesPending} committed={w.BytesCommitted}");
    w.WriteEndArray();
}
Console.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan) + " " + buffer.WrittenCount);

// Invalid writes throw InvalidOperationException (or ArgumentException) with .NET's messages.
void Try(string label, Action<Utf8JsonWriter> body)
{
    try
    {
        Write(default, body);
        Console.WriteLine(label + ": ok");
    }
    catch (Exception e)
    {
        Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message);
    }
}

Try("property in array", w => { w.WriteStartArray(); w.WriteString("a", "b"); });
Try("property first", w => w.WritePropertyName("a"));
Try("two properties", w => { w.WriteStartObject(); w.WritePropertyName("a"); w.WritePropertyName("b"); });
Try("value in object", w => { w.WriteStartObject(); w.WriteNumberValue(1); });
Try("two values", w => { w.WriteNumberValue(1); w.WriteNumberValue(2); });
Try("end without start", w => w.WriteEndObject());
Try("mismatched end", w => { w.WriteStartObject(); w.WriteEndArray(); });
Try("end after property", w => { w.WriteStartObject(); w.WritePropertyName("a"); w.WriteEndObject(); });
Try("start after value", w => { w.WriteStringValue("x"); w.WriteStartObject(); });
Try("NaN", w => w.WriteNumberValue(double.NaN));
Try("infinity", w => { w.WriteStartObject(); w.WriteNumber("x", float.PositiveInfinity); });
Try("comment delimiter", w => w.WriteCommentValue("a */ b"));
Try("invalid raw", w => w.WriteRawValue("[1,"));
Try("raw two values", w => w.WriteRawValue("1 2"));
Try("depth", w =>
{
    for (int i = 0; i < 1001; i++) w.WriteStartArray();
});
Try("max depth 2", w =>
{
    using var stream = new MemoryStream();
    using var limited = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = 2 });
    limited.WriteStartArray();
    limited.WriteStartArray();
    limited.WriteStartArray();
});
Try("bad indent", w => _ = new JsonWriterOptions { IndentCharacter = 'x' });
Try("bad indent size", w => _ = new JsonWriterOptions { IndentSize = 200 });
Try("bad new line", w => _ = new JsonWriterOptions { NewLine = "\n\n" });
Try("skip validation", w =>
{
    using var stream = new MemoryStream();
    using (var loose = new Utf8JsonWriter(stream, new JsonWriterOptions { SkipValidation = true }))
    {
        loose.WriteNumberValue(1);
        loose.WriteNumberValue(2);
        loose.WriteEndObject();
    }
    Console.WriteLine("  " + Encoding.UTF8.GetString(stream.ToArray()));
});

// Options round trip through the writer.
using (var w = new Utf8JsonWriter(new MemoryStream(), new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
{
    var o = w.Options;
    Console.WriteLine($"{o.Indented} {o.IndentSize} {(int)o.IndentCharacter} {o.MaxDepth} {o.SkipValidation} {o.NewLine == "\n"} {o.Encoder == JavaScriptEncoder.UnsafeRelaxedJsonEscaping}");
}
