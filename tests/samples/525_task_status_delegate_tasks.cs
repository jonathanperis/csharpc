// Task.Status of delegate tasks, observed deterministically: Running while the worker is blocked,
// WaitingToRun while a scheduler holds the task (Task.Run, Task.Run with a token, StartNew), and the
// promise-style tasks around them (continuations, unwrap proxies), plus pre-canceled and faulted ones.
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        foreach (var makeRunning in new Func<Task>[]
        {
            () => Task.Run(() => { started.Set(); release.Wait(); }),
            () => Task.Run(() => { started.Set(); release.Wait(); }, CancellationToken.None),
            () => Task.Factory.StartNew(() => { started.Set(); release.Wait(); }),
        })
        {
            started.Reset(); release.Reset();
            Task running = makeRunning();
            started.Wait();
            Console.WriteLine($"running: {running.Status}");
            release.Set();
            running.Wait();
            Console.WriteLine($"done: {running.Status}");
        }
        var held = new HoldingScheduler();
        Task<TaskStatus>? self = null;
        self = Task.Factory.StartNew(() => self!.Status, CancellationToken.None, TaskCreationOptions.None, held);
        Console.WriteLine($"queued: {self.Status}");
        var continuation = self.ContinueWith(x => x.Result, TaskScheduler.Default);
        Console.WriteLine($"continuation: {continuation.Status}");
        held.RunAll();
        Console.WriteLine($"inside: {self.Result} after: {self.Status} continuation: {await continuation}");
        using var hold = new ManualResetEventSlim();
        var unwrapped = Task.Run(async () => { await Task.Yield(); hold.Wait(); });
        Console.WriteLine($"unwrap pending: {unwrapped.Status}");
        hold.Set();
        await unwrapped;
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var preCanceled = new[] { Task.Run(() => { }, cts.Token), Task.Factory.StartNew(() => { }, cts.Token) };
        try { Task.WaitAll(preCanceled); } catch (AggregateException e) { Console.WriteLine($"{e.InnerExceptions.Count} {preCanceled[0].Status} {preCanceled[1].Status}"); }
        var faultedRun = Task.Factory.StartNew(() => throw new FormatException("f"));
        try { faultedRun.Wait(); } catch (AggregateException) { }
        Console.WriteLine(faultedRun.Status);
    }
}

// Holds tasks until RunAll, so their WaitingToRun status can be observed.
sealed class HoldingScheduler : TaskScheduler
{
    private readonly System.Collections.Generic.List<Task> _tasks = new();
    protected override void QueueTask(Task task) => _tasks.Add(task);
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    protected override System.Collections.Generic.IEnumerable<Task> GetScheduledTasks() => _tasks;
    public void RunAll() { foreach (var t in _tasks.ToArray()) TryExecuteTask(t); _tasks.Clear(); }
}
