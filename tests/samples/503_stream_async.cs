// System.IO.Stream's virtual surface: the default asynchronous operations of a stream that only
// implements the synchronous ones, the APM pair, timeouts, argument checks, MemoryStream's synchronous
// async overrides and Stream.Null — what ASP.NET Core's and System.IO.Pipelines' streams build on.
var source = new CountingStream(10);
var buffer = new byte[4];
Console.WriteLine(await source.ReadAsync(buffer, 0, 4) + " " + string.Join(",", buffer));
Console.WriteLine(await source.ReadAsync(buffer.AsMemory(1, 2)) + " " + string.Join(",", buffer));
await source.ReadExactlyAsync(buffer, 0, 3);
Console.WriteLine(string.Join(",", buffer) + " " + await source.ReadAtLeastAsync(new byte[8], 1, throwOnEndOfStream: false));
try { await source.ReadExactlyAsync(new byte[2]); } catch (EndOfStreamException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"{source.CanTimeout} {source.Reads} reads");
try { _ = source.ReadTimeout; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { source.WriteTimeout = 5; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

var sink = new CollectingStream();
await sink.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
await sink.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 4, 5 }));
await sink.FlushAsync();
Console.WriteLine(string.Join(",", sink.Data) + " flushed " + sink.Flushes);
await new CountingStream(5).CopyToAsync(sink);
Console.WriteLine(string.Join(",", sink.Data));

// The APM pair: the callback runs once the read is done, End* returns its result once.
var done = new TaskCompletionSource<object?>();
var apm = new CountingStream(3);
IAsyncResult result = apm.BeginRead(buffer, 0, 4, ar => done.SetResult(ar.AsyncState), "state");
Console.WriteLine(await done.Task + " " + apm.EndRead(result) + " " + result.IsCompleted);
try { apm.EndRead(result); } catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name); }
try { apm.EndWrite(result); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
IAsyncResult write = sink.BeginWrite(new byte[] { 9 }, 0, 1, null, null);
sink.EndWrite(write);
Console.WriteLine(sink.Data.Count);

// Argument checks.
try { await source.ReadAsync(null!, 0, 1); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
try { await source.ReadAsync(buffer, -1, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { await source.ReadAsync(buffer, 2, 3); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { source.CopyTo(sink, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { await source.CopyToAsync(new CountingStream(1)); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { await sink.WriteAsync(new byte[1], 0, 1, new CancellationToken(true)); } catch (OperationCanceledException e) { Console.WriteLine(e.GetType().Name); }
try { await sink.ReadAsync(new byte[1], 0, 1); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }

// MemoryStream completes them synchronously.
var memory = new MemoryStream();
Task pending = memory.WriteAsync(new byte[] { 7, 8, 9 }, 0, 3);
Console.WriteLine(pending.IsCompleted + " " + memory.Length);
memory.Position = 0;
ValueTask<int> read = memory.ReadAsync(new byte[2].AsMemory());
Console.WriteLine(read.IsCompleted + " " + read.Result);
var copy = new CollectingStream();
await memory.CopyToAsync(copy);
Console.WriteLine(string.Join(",", copy.Data) + " " + memory.Position);
Console.WriteLine(memory.FlushAsync().IsCompleted);

// Stream.Null and DisposeAsync.
Console.WriteLine(await Stream.Null.ReadAsync(new byte[4], 0, 4) + " " + Stream.Null.ReadByte() + " " + Stream.Null.Length + " " + Stream.Null.CanSeek);
await Stream.Null.WriteAsync(new byte[3]);
await sink.DisposeAsync();
Console.WriteLine(sink.Disposed);

sealed class CountingStream(int length) : Stream
{
    private int _position;
    public int Reads;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        Reads++;
        int n = Math.Min(count, Math.Min(2, length - _position));
        for (int i = 0; i < n; i++) buffer[offset + i] = (byte)(++_position);
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class CollectingStream : Stream
{
    public readonly List<byte> Data = new();
    public int Flushes;
    public bool Disposed;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Data.Count;
    public override long Position { get => Data.Count; set => throw new NotSupportedException(); }
    public override void Flush() => Flushes++;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Data.AddRange(buffer.AsSpan(offset, count).ToArray());
    protected override void Dispose(bool disposing) => Disposed = true;
}
