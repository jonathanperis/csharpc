// TextWriter/TextReader's span and asynchronous members (a writer that only implements Write(char)),
// TaskToAsyncResult, ValueTask.Preserve, Buffer.BlockCopy/ByteLength and MarshalByRefObject's
// remoting members — library surface ASP.NET Core's WebUtilities and Kestrel's streams build on.
using System.Text;

var writer = new CharWriter();
writer.Write("ab".AsSpan());
writer.WriteLine("cd".AsSpan());
await writer.WriteAsync('e');
await writer.WriteAsync("fg");
await writer.WriteAsync("hi".AsMemory());
await writer.WriteLineAsync("jk");
await writer.WriteLineAsync();
await writer.WriteAsync(new StringBuilder("lm"));
writer.Write(new StringBuilder("no"));
await writer.FlushAsync();
await writer.DisposeAsync();
Console.WriteLine(writer.Text.Replace("\r", "\\r").Replace("\n", "\\n") + " " + writer.Disposed);
writer.NewLine = "|";
writer.WriteLine("x");
Console.WriteLine(writer.Text[^2..] + " " + writer.LineChars);

var reader = new CharReader("line one\nline two\nrest");
var span = new char[4];
Console.WriteLine(reader.Read(span.AsSpan()) + " " + new string(span));
Console.WriteLine(await reader.ReadLineAsync());
var block = new char[6];
Console.WriteLine(await reader.ReadBlockAsync(block, 0, 6) + " " + new string(block));
Console.WriteLine(await reader.ReadAsync(block.AsMemory(0, 2)) + " " + await reader.ReadToEndAsync());
try { await reader.ReadAsync(block, -1, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { await reader.ReadAsync(block, 4, 5); } catch (ArgumentException e) { Console.WriteLine(e.Message); }

// APM over tasks.
var done = new TaskCompletionSource<object?>();
var source = new TaskCompletionSource<int>();
var ar = TaskToAsyncResult.Begin(source.Task, r => done.TrySetResult(r.AsyncState), "state");
Console.WriteLine($"{ar.IsCompleted} {ar.CompletedSynchronously} {ar.AsyncState}");
source.SetResult(42);
Console.WriteLine(await done.Task + " " + TaskToAsyncResult.End<int>(ar) + " " + ar.IsCompleted);
var sync = TaskToAsyncResult.Begin(Task.CompletedTask, null, null);
TaskToAsyncResult.End(sync);
Console.WriteLine(sync.CompletedSynchronously + " " + (TaskToAsyncResult.Unwrap(sync) == Task.CompletedTask));
try { TaskToAsyncResult.End(Task.FromException(new InvalidOperationException("apm failure")).ToApm()); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { TaskToAsyncResult.Unwrap(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

ValueTask<int> valueTask = new ValueTask<int>(Task.FromResult(5)).Preserve();
Console.WriteLine(await valueTask + await valueTask);

var bytes = new byte[8];
Buffer.BlockCopy(new[] { 0x04030201, 0x08070605 }, 0, bytes, 0, 8);
Console.WriteLine(string.Join(",", bytes) + " " + Buffer.ByteLength(new long[3]) + " " + Buffer.ByteLength(new char[5]));
var shorts = new short[] { 1, 2, 3, 4 };
Buffer.BlockCopy(shorts, 0, shorts, 2, 4);
Console.WriteLine(string.Join(",", shorts));
try { Buffer.BlockCopy(new string[1], 0, bytes, 0, 1); } catch (ArgumentException e) { Console.WriteLine(e.ParamName); }
try { Buffer.BlockCopy(bytes, 4, bytes, 0, 5); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
try { Buffer.BlockCopy(bytes, -1, bytes, 0, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

#pragma warning disable SYSLIB0010
try { new MemoryStream().InitializeLifetimeService(); } catch (PlatformNotSupportedException e) { Console.WriteLine(e.Message); }
#pragma warning restore SYSLIB0010
Console.WriteLine(new MemoryStream() is MarshalByRefObject);

sealed class CharWriter : TextWriter
{
    private readonly StringBuilder _text = new();
    public bool Disposed;
    public string Text => _text.ToString();
    public string LineChars => new string(CoreNewLine);
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => _text.Append(value);
    protected override void Dispose(bool disposing) => Disposed = true;
}

sealed class CharReader(string text) : TextReader
{
    private int _position;
    public override int Peek() => _position < text.Length ? text[_position] : -1;
    public override int Read() => _position < text.Length ? text[_position++] : -1;
}

static class Apm
{
    public static IAsyncResult ToApm(this Task task) => TaskToAsyncResult.Begin(task, null, null);
}
