// ValueTasks over IValueTaskSource / IValueTaskSource<T> (what System.IO.Pipelines and socket awaitables
// return): sources already completed (successfully, faulted, canceled) and completed later, awaited and
// converted with AsTask, reused across operations.
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

sealed class Source<T> : IValueTaskSource<T>, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<T> _core;
    public short Version => _core.Version;
    public void Reset() => _core.Reset();
    public void Succeed(T value) => _core.SetResult(value);
    public void Fail(Exception e) => _core.SetException(e);
    public T GetResult(short token) => _core.GetResult(token);
    void IValueTaskSource.GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}

static class Program
{
    static async Task Main()
    {
        var source = new Source<int>();
        source.Succeed(41);
        var done = new ValueTask<int>(source, source.Version);
        Console.WriteLine($"completed: {done.IsCompleted} {await done}");

        source.Reset();
        source.Succeed(7);
        var plain = new ValueTask(source, source.Version);
        Console.WriteLine($"non-generic completed: {plain.IsCompleted} {ReferenceEquals(plain.AsTask(), Task.CompletedTask)}");

        for (int round = 0; round < 3; round++)
        {
            source.Reset();
            var pending = new ValueTask<int>(source, source.Version);
            var task = pending.AsTask();
            Console.WriteLine($"round {round}: pending {task.IsCompleted}");
            _ = Task.Run(async () => { await Task.Delay(10); source.Succeed(round * 10); });
            Console.WriteLine($"round {round}: {await task}");
        }

        source.Reset();
        source.Fail(new InvalidOperationException("broken"));
        try { await new ValueTask<int>(source, source.Version); }
        catch (InvalidOperationException e) { Console.WriteLine($"faulted: {e.Message}"); }

        source.Reset();
        source.Fail(new OperationCanceledException());
        var canceled = new ValueTask(source, source.Version).AsTask();
        Console.WriteLine($"canceled: {canceled.IsCanceled} {canceled.IsFaulted}");

        source.Reset();
        var later = new ValueTask(source, source.Version).AsTask();
        _ = Task.Run(async () => { await Task.Delay(10); source.Fail(new FormatException("late")); });
        try { await later; }
        catch (FormatException e) { Console.WriteLine($"late fault: {e.Message} {later.IsFaulted}"); }
    }
}
