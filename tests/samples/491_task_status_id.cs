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
    }
}

