// CancellationTokenSource / CancellationToken on the single-threaded scheduler.
var cts = new CancellationTokenSource();
var token = cts.Token;
Console.WriteLine($"{token.IsCancellationRequested} {token.CanBeCanceled} {CancellationToken.None.CanBeCanceled}");
token.Register(() => Console.WriteLine("callback 1"));
token.Register(() => Console.WriteLine("callback 2"));

var slow = Task.Delay(10_000, token);
var watcher = Watch(slow);
cts.Cancel();
await watcher;
Console.WriteLine($"{token.IsCancellationRequested} {cts.IsCancellationRequested} {slow.IsCanceled}");

try { token.ThrowIfCancellationRequested(); }
catch (OperationCanceledException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

try { await Task.Delay(5, token); }
catch (TaskCanceledException e) { Console.WriteLine("already canceled -> " + e.Message); }

var timed = new CancellationTokenSource(20);
try
{
    await Task.Delay(5_000, timed.Token);
    Console.WriteLine("not reached");
}
catch (OperationCanceledException) { Console.WriteLine("timed out"); }

Console.WriteLine(await Work(CancellationToken.None));
var stopper = new CancellationTokenSource();
stopper.Cancel();
try { await Work(stopper.Token); } catch (OperationCanceledException) { Console.WriteLine("work canceled"); }

async Task Watch(Task t)
{
    try { await t; Console.WriteLine("completed?"); }
    catch (TaskCanceledException) { Console.WriteLine("delay canceled"); }
}

async Task<int> Work(CancellationToken ct)
{
    int total = 0;
    for (int i = 0; i < 3; i++)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        total += i;
    }
    return total;
}
