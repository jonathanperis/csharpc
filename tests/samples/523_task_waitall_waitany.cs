// Task.WaitAll and Task.WaitAny: timeouts, tokens, empty arrays, nulls, and the aggregate of faulted and
// canceled tasks.
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Try(string label, Func<object?> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var never = new TaskCompletionSource().Task;
        var done = Task.CompletedTask;
        var slow = Task.Delay(50);
        Try("waitall empty", () => { Task.WaitAll(); return "ok"; });
        Try("waitall timeout", () => Task.WaitAll(new[] { done, never }, 20));
        Try("waitall done", () => Task.WaitAll(new[] { done, slow }, 5000));
        Try("waitany done", () => Task.WaitAny(never, done));
        Try("waitany timeout", () => Task.WaitAny(new[] { never }, 20));
        Try("waitany empty timeout", () => Task.WaitAny(new Task[0], 20));
        Try("waitany slow", () => Task.WaitAny(never, Task.Delay(30)));
        Try("waitall null", () => { Task.WaitAll((Task[])null!); return "ok"; });
        Try("waitall null element", () => { Task.WaitAll(done, null!); return "ok"; });
        Try("waitany null element", () => Task.WaitAny(done, null!));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Try("waitall token", () => { Task.WaitAll(new[] { never }, cts.Token); return "ok"; });
        Try("waitany token", () => Task.WaitAny(new[] { never }, cts.Token));
        var canceled = Task.FromCanceled(cts.Token);
        Try("waitall canceled", () => { Task.WaitAll(done, canceled); return "ok"; });
        var faulted = Task.FromException(new FormatException("bad"));
        try { Task.WaitAll(faulted, canceled, done); }
        catch (AggregateException e) { foreach (var x in e.InnerExceptions) Console.WriteLine($"  {x.GetType().Name}: {x.Message}"); }
        Try("waitall timeout negative", () => Task.WaitAll(new[] { done }, -5));
    }
}
