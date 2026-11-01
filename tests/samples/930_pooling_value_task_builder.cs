// PoolingAsyncValueTaskMethodBuilder<T>: a method that returns without suspending gives its result inline
// (awaitable any number of times); one that suspends gives a pooled box good for exactly one await, after
// which the box is reset and reused, so a second await (or IsCompleted) of the stale ValueTask throws
// InvalidOperationException. Faults, cancellation, AsTask, AsyncLocal flow and SynchronizationContext
// capture behave as with the non-pooling builder.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

class QueueContext : SynchronizationContext
{
    public readonly Queue<(SendOrPostCallback, object)> Posted = new();
    public override void Post(SendOrPostCallback d, object state) => Posted.Enqueue((d, state));
}

static class Program
{
    static readonly AsyncLocal<string> s_local = new();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<int> SyncValue(int x)
    {
        if (x < 0) throw new ArgumentException("negative");
        await Task.CompletedTask;
        return x * 2;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<int> Pending(Task<int> t, int add)
    {
        int v = await t;
        return v + add;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<int> PendingThrow(Task t)
    {
        await t;
        throw new InvalidOperationException("after await");
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<string> Canceled(Task t, CancellationToken ct)
    {
        await t;
        ct.ThrowIfCancellationRequested();
        return "not canceled";
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<string> Flow(Task t)
    {
        s_local.Value = "inner";
        await t;
        return s_local.Value;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<long> Yielding(int i)
    {
        await Task.Yield();
        return i;
    }

    static async Task Consumer(ValueTask<int> vt, List<string> log)
    {
        log.Add("consumer before");
        int v = await vt;
        log.Add("consumer after " + v + " on context " + (SynchronizationContext.Current is QueueContext));
    }

    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine(label + ": " + f()); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static async Task TryAwait<T>(string label, ValueTask<T> vt)
    {
        try { Console.WriteLine(label + ": " + await vt); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void ContextCapture()
    {
        var ctx = new QueueContext();
        var log = new List<string>();
        var tcs = new TaskCompletionSource<int>();
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task consumer = Consumer(Pending(tcs.Task, 1), log);
        SynchronizationContext.SetSynchronizationContext(null);
        tcs.SetResult(41);
        log.Add("posted " + ctx.Posted.Count);
        int rounds = 0;
        while (ctx.Posted.Count > 0)
        {
            var (d, state) = ctx.Posted.Dequeue();
            SynchronizationContext.SetSynchronizationContext(ctx);
            d(state);
            SynchronizationContext.SetSynchronizationContext(null);
            rounds++;
        }
        log.Add("rounds " + rounds + ", consumer done " + consumer.IsCompletedSuccessfully);
        foreach (var line in log) Console.WriteLine(line);
    }

    static async Task Main()
    {
        ContextCapture();

        var sync = SyncValue(21);
        Console.WriteLine("sync completed: " + sync.IsCompletedSuccessfully);
        await TryAwait("sync first", sync);
        await TryAwait("sync second", sync);

        var syncFault = SyncValue(-1);
        Console.WriteLine("sync fault faulted: " + syncFault.IsFaulted);
        await TryAwait("sync fault first", syncFault);
        await TryAwait("sync fault second", syncFault);

        var tcs = new TaskCompletionSource<int>();
        var pending = Pending(tcs.Task, 5);
        Console.WriteLine("pending completed: " + pending.IsCompleted);
        tcs.SetResult(10);
        Console.WriteLine("pending completed after SetResult: " + pending.IsCompleted);
        await TryAwait("pending first", pending);
        await TryAwait("pending second", pending);
        Try("pending IsCompleted after await", () => pending.IsCompleted);

        // The next suspending call may reuse the box: the stale ValueTask still throws, the new one works.
        var tcs2 = new TaskCompletionSource<int>();
        var reused = Pending(tcs2.Task, 100);
        Try("stale after reuse", () => pending.IsCompleted);
        tcs2.SetResult(1);
        await TryAwait("reused", reused);

        var tcs3 = new TaskCompletionSource<int>();
        Task<int> asTask = Pending(tcs3.Task, 1).AsTask();
        tcs3.SetResult(1);
        Console.WriteLine("AsTask: " + await asTask + " " + await asTask);

        var gate = new TaskCompletionSource();
        var thrown = PendingThrow(gate.Task);
        gate.SetResult();
        Console.WriteLine("thrown faulted: " + thrown.IsFaulted);
        await TryAwait("thrown", thrown);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var gate2 = new TaskCompletionSource();
        var canceled = Canceled(gate2.Task, cts.Token);
        gate2.SetResult();
        Console.WriteLine("canceled: " + canceled.IsCanceled + " faulted: " + canceled.IsFaulted);
        await TryAwait("canceled", canceled);

        s_local.Value = "outer";
        var gate3 = new TaskCompletionSource();
        var flow = Flow(gate3.Task);
        Console.WriteLine("local after start: " + s_local.Value);
        gate3.SetResult();
        await TryAwait("flow", flow);
        Console.WriteLine("local after await: " + s_local.Value);

        var gate4 = new TaskCompletionSource<int>();
        var configured = Pending(gate4.Task, 2);
        gate4.SetResult(40);
        Console.WriteLine("configured: " + await configured.ConfigureAwait(false));

        long sum = 0;
        for (int i = 0; i < 2000; i++) sum += await Yielding(i);
        Console.WriteLine("sequential sum: " + sum);

        var many = new ValueTask<long>[200];
        for (int i = 0; i < many.Length; i++) many[i] = Yielding(i);
        sum = 0;
        foreach (var vt in many) sum += await vt;
        Console.WriteLine("concurrent sum: " + sum);
    }
}
