// Task.Status (TaskStatus of promise-style, delegate, faulted and canceled tasks) and Task.Id
// (assigned lazily, in the order of first use, from one counter).
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Later(Task gate) { await gate; return 42; }

    static async Task Main()
    {
        var tcs = new TaskCompletionSource<int>();
        var gate = new TaskCompletionSource();
        Task<int> pending = Later(gate.Task);
        Console.WriteLine($"{tcs.Task.Status} {pending.Status} {gate.Task.Status}");
        Console.WriteLine($"ids: {tcs.Task.Id} {pending.Id} {tcs.Task.Id} {gate.Task.Id}");
        gate.SetResult();
        Console.WriteLine($"{await pending} {pending.Status}");
        tcs.SetResult(1);
        Console.WriteLine(tcs.Task.Status);

        var run = Task.Run(() => 5);
        run.Wait();
        Console.WriteLine($"{run.Status} {run.Result} id={run.Id}");
        var faulted = Task.Run(() => throw new InvalidOperationException("boom"));
        try { await faulted; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        Console.WriteLine(faulted.Status);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = Task.FromCanceled(cts.Token);
        Console.WriteLine($"{canceled.Status} {Task.CompletedTask.Status} {Task.FromResult(3).Status}");
        Console.WriteLine($"{Task.CompletedTask.Id == Task.CompletedTask.Id} {Task.Delay(1).Status}");
        Task t = Task.Delay(10);
        await t;
        Console.WriteLine($"{t.Status} {t.Id} {(int)TaskStatus.Faulted}");

        // Delegate tasks, observed deterministically: Running while the worker is blocked, WaitingToRun
        // while a scheduler holds the task, and the promise-style tasks around them.
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
