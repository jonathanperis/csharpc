// SynchronizationContext: awaits that capture a derived context post their continuation to it
// (ConfigureAwait(false) doesn't, Task.Yield does), async void reports OperationStarted/Completed and
// rethrows on the context, CancellationToken.Register(useSynchronizationContext) runs through Send;
// ExecutionContext.Capture/Run and ThreadPool.QueueUserWorkItem
// (docs-standard-asynchronous-programming-patterns-executioncontext-synchronizationcontext).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

var pump = new PumpContext();
SynchronizationContext.SetSynchronizationContext(pump);
Console.WriteLine($"current: {SynchronizationContext.Current?.GetType().Name}");
int main = Environment.CurrentManagedThreadId;
var log = new List<string>();
var work = RunAsync(log, main);
pump.RunUntil(work);
Console.WriteLine(string.Join(Environment.NewLine, log));
Console.WriteLine($"result {work.Result}, posts {pump.Posts}");

// async void: OperationStarted when called, OperationCompleted when done; an exception goes to the context.
VoidAsync(false);
VoidAsync(true);
pump.RunUntil(() => pump.Completed == 2);
Console.WriteLine($"started {pump.Started}, completed {pump.Completed}, failures {pump.Failures}");

// A plain SynchronizationContext is no context to post to.
SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
await Task.Delay(1);
Console.WriteLine($"base context posts nothing: {pump.Posts}");
SynchronizationContext.SetSynchronizationContext(pump);

using (var cts = new CancellationTokenSource())
{
    cts.Token.Register(() => Console.WriteLine($"callback via Send: {pump.Sends}"), useSynchronizationContext: true);
    cts.Token.Register(() => Console.WriteLine("plain callback"));
    cts.Cancel();
    Console.WriteLine($"sends {pump.Sends}");
}
SynchronizationContext.SetSynchronizationContext(null);
Console.WriteLine($"current: {SynchronizationContext.Current?.GetType().Name ?? "null"}");

var captured = ExecutionContext.Capture()!;
ExecutionContext.Run(captured, state => Console.WriteLine($"run with {state}"), "state");
try { ExecutionContext.Run(null!, _ => { }, null); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

using var done = new CountdownEvent(3);
ThreadPool.QueueUserWorkItem(_ => done.Signal());
ThreadPool.QueueUserWorkItem(state => { if ((string)state! == "x") done.Signal(); }, "x");
ThreadPool.QueueUserWorkItem(n => { if (n == 42) done.Signal(); }, 42, preferLocal: false);
Console.WriteLine($"pool items done: {done.Wait(10000)}");
try { ThreadPool.QueueUserWorkItem(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

static async Task<int> RunAsync(List<string> log, int main)
{
    await Task.Delay(5);
    log.Add($"after await: on context thread {Environment.CurrentManagedThreadId == main}, current {SynchronizationContext.Current?.GetType().Name}");
    await Task.Run(() => 1);
    log.Add($"after Task.Run: on context thread {Environment.CurrentManagedThreadId == main}");
    await Task.Yield();
    log.Add($"after Yield: on context thread {Environment.CurrentManagedThreadId == main}");
    await Task.Delay(5).ConfigureAwait(false);
    log.Add($"after ConfigureAwait(false): on context thread {Environment.CurrentManagedThreadId == main}");
    return 42;
}

static async void VoidAsync(bool fail)
{
    await Task.Delay(5);
    if (fail) throw new InvalidOperationException("from async void");
}

sealed class PumpContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback, object?)> _queue = new();
    public int Posts, Sends, Started, Completed, Failures;

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue)
        {
            Posts++;
            _queue.Enqueue((d, state));
            Monitor.PulseAll(_queue);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        Sends++;
        d(state);
    }

    public override void OperationStarted() => Interlocked.Increment(ref Started);
    public override void OperationCompleted()
    {
        lock (_queue)
        {
            Completed++;
            Monitor.PulseAll(_queue);
        }
    }

    public void RunUntil(Task task) => RunUntil(() => task.IsCompleted);

    public void RunUntil(Func<bool> done)
    {
        while (true)
        {
            (SendOrPostCallback, object?) item;
            lock (_queue)
            {
                while (_queue.Count == 0)
                {
                    if (done()) return;
                    Monitor.Wait(_queue, 10);
                }
                item = _queue.Dequeue();
            }
            try { item.Item1(item.Item2); }
            catch (InvalidOperationException e) { Failures++; Console.WriteLine($"on the context: {e.Message}"); }
        }
    }
}
