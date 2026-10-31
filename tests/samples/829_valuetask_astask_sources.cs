// ValueTask.AsTask() on completed value tasks (Task.CompletedTask for the non-generic one, as .NET), and
// ValueTask<T>s over an IValueTaskSource<T> that has already completed, with default and other results.
using System.Threading.Tasks.Sources;

ValueTask done = default;
Console.WriteLine($"default AsTask is CompletedTask: {ReferenceEquals(done.AsTask(), Task.CompletedTask)} {done.AsTask().Status}");
Console.WriteLine($"ValueTask.CompletedTask AsTask: {ReferenceEquals(ValueTask.CompletedTask.AsTask(), Task.CompletedTask)}");
Task wrapped = Task.Delay(1);
Console.WriteLine($"wrapping a task keeps it: {ReferenceEquals(new ValueTask(wrapped).AsTask(), wrapped)}");
await wrapped;

var source = new Source<int>();
for (int round = 0; round < 3; round++)
{
    source.Reset();
    source.Complete(round == 1 ? 0 : 7 * round);
    var vt = new ValueTask<int>(source, source.Version);
    Console.WriteLine($"round {round}: {vt.IsCompleted} {vt.IsCompletedSuccessfully} {await vt}");
}
var strings = new Source<string?>();
strings.Complete(null);
Console.WriteLine($"null result: {await new ValueTask<string?>(strings, strings.Version) ?? "null"}");
strings.Reset();
strings.Complete("text");
Console.WriteLine($"text result: {await new ValueTask<string?>(strings, strings.Version)}");
var failing = new Source<long>();
failing.Fail(new InvalidOperationException("source failed"));
try { await new ValueTask<long>(failing, failing.Version); }
catch (InvalidOperationException e) { Console.WriteLine($"caught: {e.Message}"); }
var pending = new Source<int>();
var later = new ValueTask<int>(pending, pending.Version);
Console.WriteLine($"pending: {later.IsCompleted}");
_ = Task.Run(() => pending.Complete(99));
Console.WriteLine($"completed later: {await later}");

sealed class Source<T> : IValueTaskSource<T>
{
    private ManualResetValueTaskSourceCore<T> _core;
    public short Version => _core.Version;
    public void Reset() => _core.Reset();
    public void Complete(T value) => _core.SetResult(value);
    public void Fail(Exception e) => _core.SetException(e);
    public T GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}
