// System.Buffers.ReadOnlySequence<T> over arrays and segment chains, SequencePosition, BuffersExtensions
// and SequenceReader<T>: how System.IO.Pipelines hands Kestrel a request and how its HTTP parser reads it.
using System.Buffers;
using System.Text;

var request = "GET /plaintext HTTP/1.1\r\nHost: localhost\r\nAccept: */*\r\n\r\n"u8.ToArray();
// Split into three segments, the way pipe buffers arrive.
var first = new Segment(request.AsMemory(0, 10));
var last = first.Append(request.AsMemory(10, 20)).Append(request.AsMemory(30));
var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
Console.WriteLine($"{sequence.Length} {sequence.IsSingleSegment} {sequence.IsEmpty} {sequence.First.Length} {sequence.FirstSpan.Length}");
foreach (var memory in sequence) Console.Write(memory.Length + " ");
Console.WriteLine();
Console.WriteLine(Encoding.ASCII.GetString(sequence.Slice(4, 10).ToArray()));
Console.WriteLine(sequence.GetOffset(sequence.GetPosition(25)) + " " + sequence.Slice(sequence.GetPosition(12), 3).Length + " " + sequence.Slice(28).Length);
SequencePosition? crlf = sequence.PositionOf((byte)'\r');
Console.WriteLine(crlf is { } p ? sequence.GetOffset(p) : -1);
Console.WriteLine(Encoding.ASCII.GetString(sequence.Slice(sequence.Start, crlf!.Value).ToArray()));
Console.WriteLine(sequence.Slice(sequence.End).Length + " " + sequence.Start.Equals(sequence.GetPosition(0)) + " " + (sequence.Start.GetObject() == first));
var copy = new byte[sequence.Length];
sequence.CopyTo(copy);
Console.WriteLine(copy.AsSpan().SequenceEqual(request));

// Reading it like Kestrel's HttpParser: request line, then headers up to the empty line.
var reader = new SequenceReader<byte>(sequence);
reader.TryReadTo(out ReadOnlySpan<byte> method, (byte)' ');
reader.TryReadTo(out ReadOnlySequence<byte> target, (byte)' ');
reader.TryReadTo(out ReadOnlySpan<byte> version, "\r\n"u8);
Console.WriteLine($"{Encoding.ASCII.GetString(method)}|{Encoding.ASCII.GetString(target.ToArray())}|{Encoding.ASCII.GetString(version)}|{reader.Consumed}|{reader.Remaining}");
while (!reader.IsNext("\r\n"u8, advancePast: true))
{
    reader.TryReadTo(out ReadOnlySpan<byte> name, (byte)':');
    reader.AdvancePast((byte)' ');
    reader.TryReadTo(out ReadOnlySequence<byte> value, "\r\n"u8);
    Console.WriteLine($"{Encoding.ASCII.GetString(name)}={Encoding.ASCII.GetString(value.ToArray())}");
}
Console.WriteLine($"{reader.End} {reader.Consumed} {reader.Remaining} {reader.TryRead(out _)}");
reader.Rewind(4);
Console.WriteLine($"{reader.TryPeek(out byte peeked)} {peeked} {reader.TryPeek(2, out byte ahead)} {ahead} {reader.UnreadSpan.Length} {reader.CurrentSpanIndex}");
reader.Rewind(reader.Consumed);
Console.WriteLine($"{reader.Consumed} {reader.IsNext((byte)'G')} {reader.TryAdvanceTo((byte)'/')} {reader.Consumed} {reader.TryReadExact(9, out var exact)} {Encoding.ASCII.GetString(exact.ToArray())}");
Console.WriteLine($"{reader.TryReadToAny(out ReadOnlySpan<byte> upTo, "\r\n"u8, advancePastDelimiter: false)} {Encoding.ASCII.GetString(upTo)} {reader.AdvancePastAny("\r\n"u8)}");
reader.AdvanceToEnd();
Console.WriteLine($"{reader.End} {reader.Consumed == sequence.Length}");

var numbers = new SequenceReader<byte>(new ReadOnlySequence<byte>(new byte[] { 1, 0, 0, 0, 0, 2, 0xFF, 0xFE }));
numbers.TryReadLittleEndian(out int little);
numbers.TryReadBigEndian(out short big);
numbers.TryReadLittleEndian(out short negative);
Console.WriteLine($"{little} {big} {negative} {numbers.TryReadLittleEndian(out int none)} {none}");

// Character sequences, arrays and the argument checks.
var chars = new ReadOnlySequence<char>("hello world".ToCharArray(), 6, 5);
Console.WriteLine(chars.ToString() + " " + new ReadOnlySequence<int>(new[] { 1, 2, 3 }).ToString() + " " + ReadOnlySequence<byte>.Empty.Length);
var fromMemory = new ReadOnlySequence<byte>(new byte[] { 9, 8, 7 }.AsMemory(1));
Console.WriteLine(fromMemory.Length + " " + fromMemory.First.Span[0] + " " + default(ReadOnlySequence<byte>).IsEmpty);
try { sequence.Slice(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { sequence.Slice(10, 100); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { sequence.GetPosition(-5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { new ReadOnlySequence<byte>(new byte[2], 1, 5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { new ReadOnlySequence<byte>(last, 0, first, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
var writer = new ArrayBufferWriter<byte>(4);
writer.Write("pipelined"u8);
Console.WriteLine(Encoding.ASCII.GetString(writer.WrittenSpan) + " " + writer.WrittenCount);

sealed class Segment : ReadOnlySequenceSegment<byte>
{
    public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

    public Segment Append(ReadOnlyMemory<byte> memory)
    {
        var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = next;
        return next;
    }
}
