// Utf8JsonWriter string properties: values written as they are and values that need escaping (at
// the start, middle and end, after non-ASCII text), surrogate pairs and lone surrogates, other
// encoders, indentation, and the buffer growing in the middle of a value.
using System;
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

static class Program
{
    static string Write(JsonWriterOptions options, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(16);
        using (var w = new Utf8JsonWriter(buffer, options))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static void Main()
    {
        string[] values =
        [
            "Hello, World!", "", "<tag>", "a<b", "end>", "quote\"inside", "back\\slash", "tab\tnew\nline",
            "café", "日本語", "€uro", "ü<", "emoji 😀 face", "lone \uD800 high", "lone \uDC00 low", "plus+amp&",
            new string('x', 300) + "é" + new string('y', 50) + "<",
        ];
        var name = JsonEncodedText.Encode("message");
        foreach (var options in new[]
        {
            new JsonWriterOptions(),
            new JsonWriterOptions { Indented = true },
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
            new JsonWriterOptions { SkipValidation = true },
        })
        {
            Console.WriteLine(Write(options, w =>
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i % 3 == 0) w.WriteString(name, values[i]);
                    else if (i % 3 == 1) w.WriteString("p" + i, values[i]);
                    else w.WriteString("q"u8, values[i]);
                }
            }));
        }
        try { Write(new JsonWriterOptions(), w => { w.WriteStartArray("a"); w.WriteString(name, "x"); }); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
    }
}
