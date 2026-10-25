// System.IO.BufferedStream: which calls reach the underlying stream, and when
// (docs-core-compatibility-core-libraries-10-0-bufferedstream-writebyte-flush).
using System;
using System.IO;
using System.Threading.Tasks;

var traced = new TracingStream();
var buffered = new BufferedStream(traced, bufferSize: 4);
Console.WriteLine($"BufferSize={buffered.BufferSize} CanRead={buffered.CanRead} CanWrite={buffered.CanWrite} CanSeek={buffered.CanSeek}");
// Filling the buffer with WriteByte no longer flushes (.NET 10).
for (byte b = 1; b <= 4; b++) buffered.WriteByte(b);
Console.WriteLine("after 4 WriteByte");
buffered.WriteByte(5);
Console.WriteLine("after 5th WriteByte");
buffered.Write(new byte[] { 6, 7 }, 0, 2);
buffered.Write(new byte[10], 0, 10);
Console.WriteLine($"Position={buffered.Position} Length={buffered.Length}");
buffered.Flush();
buffered.Position = 0;
var read = new byte[3];
Console.WriteLine($"Read={buffered.Read(read, 0, 3)} [{string.Join(",", read)}] Position={buffered.Position}");
Console.WriteLine($"ReadByte={buffered.ReadByte()}");
Console.WriteLine($"Seek={buffered.Seek(-2, SeekOrigin.End)}");
var rest = new byte[8];
Console.WriteLine($"Read={buffered.Read(rest, 0, 8)} ReadByte={buffered.ReadByte()}");
buffered.Seek(1, SeekOrigin.Begin);
buffered.WriteByte(99);
buffered.SetLength(5);
Console.WriteLine($"Length={buffered.Length}");
await buffered.WriteAsync(new byte[] { 1, 2, 3 });
await buffered.FlushAsync();
buffered.Position = 0;
var all = new byte[16];
int n = await buffered.ReadAsync(all, 0, 16);
Console.WriteLine($"ReadAsync={n} [{string.Join(",", all.AsSpan(0, n).ToArray())}]");
var copy = new MemoryStream();
buffered.Position = 2;
buffered.CopyTo(copy);
Console.WriteLine($"CopyTo -> [{string.Join(",", copy.ToArray())}]");
buffered.Dispose();
Console.WriteLine($"disposed: CanRead={buffered.CanRead}");
try { buffered.WriteByte(1); }
catch (ObjectDisposedException e) { Console.WriteLine(e.Message); }

try { new BufferedStream(new MemoryStream(), 0); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { new BufferedStream(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { new BufferedStream(new MemoryStream()).Position = -1; }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

// A read buffer over a non-seekable stream can't be dropped for a write.
var oneWay = new BufferedStream(new NonSeekable(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })), 4);
oneWay.ReadByte();
try { oneWay.WriteByte(1); }
catch (NotSupportedException e) { Console.WriteLine(e.Message); }

sealed class TracingStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) { Console.WriteLine($"  Write({count})"); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Console.WriteLine($"  Write(span {buffer.Length})"); base.Write(buffer); }
    public override int Read(byte[] buffer, int offset, int count) { int n = base.Read(buffer, offset, count); Console.WriteLine($"  Read({count}) = {n}"); return n; }
    public override int Read(Span<byte> buffer) { int n = base.Read(buffer); Console.WriteLine($"  Read(span {buffer.Length}) = {n}"); return n; }
    public override long Seek(long offset, SeekOrigin loc) { Console.WriteLine($"  Seek({offset}, {loc})"); return base.Seek(offset, loc); }
    public override void Flush() { Console.WriteLine("  Flush"); base.Flush(); }
    public override void SetLength(long value) { Console.WriteLine($"  SetLength({value})"); base.SetLength(value); }
    protected override void Dispose(bool disposing) { Console.WriteLine($"  Dispose({disposing})"); base.Dispose(disposing); }
}

sealed class NonSeekable(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
}
