// CancellationTokenSource / CancellationToken. Output only depends on ordering .NET guarantees:
// callbacks run synchronously inside Cancel(), most recent first; the delay's continuation may run
// on another thread, so its effect is printed after awaiting it.
var cts = new CancellationTokenSource();
var token = cts.Token;
Console.WriteLine($"{token.IsCancellationRequested} {token.CanBeCanceled} {CancellationToken.None.CanBeCanceled}");
var log = new System.Text.StringBuilder();
token.Register(() => log.Append("callback 1;"));
token.Register(() => log.Append("callback 2;"));

var slow = Task.Delay(10_000, token);
var watcher = Watch(slow);
cts.Cancel();
Console.WriteLine(log);
Console.WriteLine(await watcher);
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

async Task<string> Watch(Task t)
{
    try { await t; return "completed?"; }
    catch (TaskCanceledException) { return "delay canceled"; }
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
