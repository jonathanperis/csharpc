// ArrayBufferWriter<T> grows with Array.Resize: data written into a returned span but not yet advanced over
// (what Utf8JsonWriter keeps pending while it asks for more room) is copied to the larger buffer.
using System;
using System.Buffers;

var writer = new ArrayBufferWriter<byte>(8);
var first = writer.GetSpan(4);
first[0] = 1; first[1] = 2; first[2] = 3; first[3] = 4;
writer.Advance(4);

var pending = writer.GetSpan(2);
pending[0] = 5; pending[1] = 6;
// no Advance yet: the next request outgrows the buffer
var grown = writer.GetMemory(64);
Console.WriteLine(writer.Capacity >= 4 + 64);
Console.WriteLine(BitConverter.ToString(grown.Span.Slice(0, 3).ToArray()));
writer.Advance(2);
Console.WriteLine(BitConverter.ToString(writer.WrittenSpan.ToArray()));

var chars = new ArrayBufferWriter<char>();
for (int i = 0; i < 40; i++)
{
    var span = chars.GetSpan(3);
    span[0] = (char)('a' + i % 26);
    chars.Advance(1);
}
Console.WriteLine(new string(chars.WrittenSpan));
