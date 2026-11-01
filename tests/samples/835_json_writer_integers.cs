// Utf8JsonWriter integers, as properties and values: zero, signs, every length, the type limits,
// minimized and indented, inside arrays and objects.
using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

static class Program
{
    static void Main()
    {
        long[] longs = [0, 1, -1, 9, 10, -10, 99, 100, 12345, -98765, int.MaxValue, int.MinValue, uint.MaxValue, 4294967296, -4294967296, long.MaxValue, long.MinValue];
        foreach (bool indented in new[] { false, true })
        {
            var buffer = new ArrayBufferWriter<byte>(8);
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
            {
                w.WriteStartObject();
                var name = JsonEncodedText.Encode("n");
                foreach (long v in longs)
                {
                    if (v >= int.MinValue && v <= int.MaxValue) { w.WriteNumber(name, (int)v); w.WriteNumber("i", (int)v); }
                    w.WriteNumber("l"u8, v);
                    if (v >= 0) { w.WriteNumber("u", (ulong)v); if (v <= uint.MaxValue) w.WriteNumber(name, (uint)v); }
                }
                w.WriteNumber("max", ulong.MaxValue);
                // Property names of every copy path: under 8 bytes, 8 to 16, longer.
                foreach (var n in new[] { "abcdefg", "abcdefgh", "randomNumber", "sixteen_chars_xx", "seventeen_chars_x", "a_much_longer_property_name" })
                    w.WriteNumber(JsonEncodedText.Encode(n), n.Length);
                w.WriteStartArray("values");
                foreach (long v in longs)
                {
                    w.WriteNumberValue(v);
                    if (v >= int.MinValue && v <= int.MaxValue) w.WriteNumberValue((int)v);
                    if (v >= 0 && v <= uint.MaxValue) w.WriteNumberValue((uint)v);
                }
                w.WriteNumberValue(ulong.MaxValue);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            Console.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }
}
