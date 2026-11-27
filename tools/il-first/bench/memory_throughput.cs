// System.Memory throughput, the program behind the NativeAOT column of docs/il-first/System.Memory.md
// (tools/il-first/measure-aot.sh): ArrayBufferWriter growth, multi-segment ReadOnlySequence and SequenceReader, MemoryPool rentals.
// The output is checksums only.
using System;
using System.Buffers;

long check = 0;
for (int round = 0; round < 30; round++)
{
    var writer = new ArrayBufferWriter<byte>();
    for (int i = 0; i < 200_000; i++)
    {
        var span = writer.GetSpan(8);
        BitConverter.TryWriteBytes(span, (long)i * round);
        writer.Advance(8);
    }
    check += writer.WrittenCount;

    var first = new Segment(writer.WrittenMemory.Slice(0, writer.WrittenCount / 2));
    var last = first.Append(writer.WrittenMemory.Slice(writer.WrittenCount / 2));
    var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    var reader = new SequenceReader<byte>(sequence);
    while (reader.TryReadLittleEndian(out long value)) check ^= value;
    check += sequence.Slice(13, 1000).Length;

    for (int i = 0; i < 20_000; i++)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(512);
        owner.Memory.Span[i % 512] = (byte)i;
        check += owner.Memory.Span[i % 512];
    }
}
Console.WriteLine(check);

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
