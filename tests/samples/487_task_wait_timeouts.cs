// Task.Wait with timeouts and tokens, Task/Task<T>.WaitAsync (timeout, cancellation, completion,
// faults) and Task.Run with a CancellationToken, with .NET's exceptions. Outcomes don't depend on
// timing: pending tasks are TaskCompletionSource tasks that nothing completes during the waits.
using System;
using System.Threading;
using System.Threading.Tasks;

static void Try(string name, Action action)
{
    try { action(); Console.WriteLine(name + ": ok"); }
    catch (AggregateException e) { Console.WriteLine(name + ": AggregateException -> " + e.InnerException!.GetType().Name + ": " + e.InnerException.Message); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}

static async Task TryAsync(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine(name + ": ok"); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}

var pending = new TaskCompletionSource<int>();
Console.WriteLine(pending.Task.Wait(0) + " " + pending.Task.Wait(20) + " " + pending.Task.Wait(TimeSpan.FromMilliseconds(5)));
Console.WriteLine(Task.FromResult(1).Wait(0) + " " + Task.CompletedTask.Wait(TimeSpan.Zero));
var faulted = Task.FromException(new InvalidOperationException("boom"));
Try("faulted.Wait(100)", () => faulted.Wait(100));
var canceledSource = new CancellationTokenSource();
canceledSource.Cancel();
Try("Wait(-2)", () => pending.Task.Wait(-2));
Try("Wait(token)", () => pending.Task.Wait(canceledSource.Token));
// Not Wait(10, canceledToken): .NET spins before it looks at the token, and how long depends on the
// processor count, so real .NET throws on one machine and returns false on another.

await TryAsync("WaitAsync timeout", () => pending.Task.WaitAsync(TimeSpan.FromMilliseconds(10)));
await TryAsync("WaitAsync zero", () => pending.Task.WaitAsync(TimeSpan.Zero));
await TryAsync("WaitAsync canceled", () => pending.Task.WaitAsync(canceledSource.Token));
await TryAsync("WaitAsync bad timeout", () => ((Task)pending.Task).WaitAsync(TimeSpan.FromMilliseconds(-2)));
await TryAsync("WaitAsync faulted", () => faulted.WaitAsync(TimeSpan.FromSeconds(30)));
var later = new TaskCompletionSource<string>();
var waiting = later.Task.WaitAsync(TimeSpan.FromSeconds(30), new CancellationTokenSource().Token);
later.SetResult("done");
Console.WriteLine(await waiting);
var laterFault = new TaskCompletionSource();
var waitingFault = laterFault.Task.WaitAsync(TimeSpan.FromSeconds(30));
laterFault.SetException(new FormatException("late"));
await TryAsync("WaitAsync late fault", () => waitingFault);
var cts = new CancellationTokenSource();
var waitingCancel = Task.Delay(Timeout.Infinite).WaitAsync(cts.Token);
cts.Cancel();
await TryAsync("WaitAsync late cancel", () => waitingCancel);
Console.WriteLine(Task.CompletedTask.WaitAsync(TimeSpan.FromSeconds(1)) == Task.CompletedTask);

bool ran = false;
var notRun = Task.Run(() => { ran = true; }, canceledSource.Token);
Try("Run(canceled token)", () => notRun.Wait());
Console.WriteLine(ran + " " + notRun.IsCanceled);
Console.WriteLine(await Task.Run(() => 6 * 7, new CancellationTokenSource().Token));
await Task.Run(() => Console.WriteLine("ran with token"), CancellationToken.None);
