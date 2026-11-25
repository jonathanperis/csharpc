// SemaphoreSlim's asynchronous waiters, as the framework's code queues them: each one is a Task<bool> of SemaphoreSlim's own
// (SemaphoreSlim+TaskNode, created with RunContinuationsAsynchronously) that Release completes in FIFO order; a waiter with a
// timeout or a token is wrapped in another task; an uncontended WaitAsync returns a cached completed task. Release never runs a
// waiter's continuation inline. Everything printed is decided by the semaphore's state, not by thread timing.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static async Task Main()
    {
        var gate = new SemaphoreSlim(1, 3);

        // Uncontended: a completed task, the same cached one each time.
        var fast1 = gate.WaitAsync();
        gate.Release();
        var fast2 = gate.WaitAsync();
        Console.WriteLine($"uncontended: {fast1.Status}, cached {ReferenceEquals(fast1, fast2)}, options {fast1.CreationOptions}, count {gate.CurrentCount}");

        // Queued waiters: the queued task itself, FIFO.
        var order = new List<int>();
        var waiters = new Task[3];
        for (int i = 0; i < 3; i++) waiters[i] = gate.WaitAsync();
        var node = waiters[0];
        Console.WriteLine($"queued: {node.GetType().FullName}, is Task<bool> {node is Task<bool>}, status {node.Status}, options {node.CreationOptions}, state {node.AsyncState ?? "null"}, id>0 {node.Id > 0}");
        var continuations = new Task[3];
        for (int i = 0; i < 3; i++)
        {
            int id = i;
            continuations[i] = waiters[i].ContinueWith(t => { lock (order) order.Add(id); }, TaskContinuationOptions.ExecuteSynchronously);
        }
        gate.Release();
        await continuations[0];
        Console.WriteLine($"after one Release: [{string.Join(",", order)}], completed {waiters[0].IsCompleted} {waiters[1].IsCompleted} {waiters[2].IsCompleted}, count {gate.CurrentCount}");
        gate.Release(2);
        await Task.WhenAll(continuations);
        lock (order) { order.Sort(); Console.WriteLine($"after Release(2): [{string.Join(",", order)}], results {((Task<bool>)waiters[1]).Result} {((Task<bool>)waiters[2]).Result}, count {gate.CurrentCount}"); }

        // Timeouts and tokens: the waiter is wrapped; false on timeout, canceled on cancellation, a pre-canceled token at once.
        Console.WriteLine($"WaitAsync(20) on 0: {await gate.WaitAsync(20)}, count {gate.CurrentCount}");
        using (var cts = new CancellationTokenSource())
        {
            var canceled = gate.WaitAsync(cts.Token);
            Console.WriteLine($"with a token: {(canceled.GetType() == node.GetType() ? "the node" : "a wrapper")}, status {canceled.Status}");
            cts.Cancel();
            try { await canceled; } catch (OperationCanceledException e) { Console.WriteLine($"canceled: {e.GetType().Name}, task {canceled.Status}"); }
        }
        var pre = gate.WaitAsync(new CancellationToken(true));
        Console.WriteLine($"pre-canceled token: {pre.Status}, count {gate.CurrentCount}");
        var timed = gate.WaitAsync(5000);
        gate.Release();
        Console.WriteLine($"timed waiter released: {await timed}, count {gate.CurrentCount}");

        // A synchronous Wait behind a queued asynchronous waiter queues behind it (and times out here: the one Release goes to the
        // asynchronous waiter).
        var first = gate.WaitAsync();
        Console.WriteLine($"sync Wait(30) behind an async waiter: {gate.Wait(30)}, async completed {first.IsCompleted}");
        gate.Release();
        await first;
        Console.WriteLine($"Release: async waiter {first.Status}, count {gate.CurrentCount}");

        // AvailableWaitHandle follows the count; Release past the maximum; Dispose.
        var handle = gate.AvailableWaitHandle;
        Console.WriteLine($"handle with count {gate.CurrentCount}: {handle.WaitOne(0)}");
        gate.Release();
        Console.WriteLine($"handle with count {gate.CurrentCount}: {handle.WaitOne(0)}");
        gate.Wait();
        Console.WriteLine($"handle with count {gate.CurrentCount}: {handle.WaitOne(0)}");
        gate.Release();
        try { gate.Release(3); } catch (SemaphoreFullException e) { Console.WriteLine($"Release(3) at 1 of 3: {e.Message}"); }
        Console.WriteLine($"count {gate.CurrentCount}");
        gate.Dispose();
        try { gate.WaitAsync(); } catch (ObjectDisposedException e) { Console.WriteLine($"after Dispose: {e.GetType().Name}: {e.Message}"); }
        try { new SemaphoreSlim(2, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"ctor: {e.ParamName}: {e.Message}"); }
    }
}
