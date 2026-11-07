// TaskFactory.ContinueWhenAny: every overload shape, which task is handed over, argument checks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static string Outcome(Task t)
{
    try { t.Wait(); return "ran"; }
    catch (AggregateException e) { return $"{(t.IsCanceled ? "canceled" : "faulted")}: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"; }
}
static void Check(string what, Action a)
{
    try { a(); Console.WriteLine($"{what}: no exception"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message.Replace("\r", "").Replace("\n", "|")}"); }
}

var f = Task.Factory;
Task[] done = { Task.FromResult(1), Task.FromResult(2) };
Task<int>[] doneInt = { Task.FromResult(10), Task.FromResult(20), Task.FromResult(30) };

// --- all overloads, every task already complete: the first in order is handed over ---
f.ContinueWhenAny(done, t => Console.WriteLine($"any1 {t == done[0]}")).Wait();
f.ContinueWhenAny(done, t => Console.WriteLine("any2 token"), CancellationToken.None).Wait();
f.ContinueWhenAny(done, t => Console.WriteLine("any3 options"), TaskContinuationOptions.ExecuteSynchronously).Wait();
f.ContinueWhenAny(done, t => Console.WriteLine("any4 full"), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Wait();
Console.WriteLine(f.ContinueWhenAny(done, t => "r1 " + (t == done[0])).Result);
Console.WriteLine(f.ContinueWhenAny(done, t => "r2", CancellationToken.None).Result);
Console.WriteLine(f.ContinueWhenAny(done, t => "r3", TaskContinuationOptions.None).Result);
Console.WriteLine(f.ContinueWhenAny(done, t => "r4", CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
f.ContinueWhenAny(doneInt, t => Console.WriteLine($"g1 {t.Result}")).Wait();
f.ContinueWhenAny(doneInt, t => Console.WriteLine($"g2 {t.Result}"), CancellationToken.None).Wait();
f.ContinueWhenAny(doneInt, t => Console.WriteLine($"g3 {t.Result}"), TaskContinuationOptions.None).Wait();
f.ContinueWhenAny(doneInt, t => Console.WriteLine($"g4 {t.Result}"), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Wait();
Console.WriteLine(f.ContinueWhenAny(doneInt, t => t.Result + 1).Result);
Console.WriteLine(f.ContinueWhenAny(doneInt, t => t.Result + 2, CancellationToken.None).Result);
Console.WriteLine(f.ContinueWhenAny(doneInt, t => t.Result + 3, TaskContinuationOptions.None).Result);
Console.WriteLine(f.ContinueWhenAny(doneInt, t => t.Result + 4, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);

// --- the first to complete wins, later completions are ignored ---
var a = new TaskCompletionSource<int>();
var b = new TaskCompletionSource<int>();
var c = new TaskCompletionSource<int>();
var winner = f.ContinueWhenAny(new Task[] { a.Task, b.Task, c.Task }, t => ((Task<int>)t).Result);
Console.WriteLine($"before: {winner.IsCompleted}");
b.SetResult(22);
Console.WriteLine($"{winner.Result}");
a.SetResult(11);
c.SetResult(33);
Console.WriteLine($"{winner.Result} {winner.Status}");

// --- a faulted or canceled winner is still handed over (no NotOn*) ---
var fa = new TaskCompletionSource<int>();
var fb = new TaskCompletionSource<int>();
var fw = f.ContinueWhenAny(new Task[] { fa.Task, fb.Task }, t => t.Status.ToString());
fb.SetException(new InvalidOperationException("boom"));
Console.WriteLine(fw.Result);
var ca = new TaskCompletionSource<int>();
var cw = f.ContinueWhenAny(new[] { ca.Task }, t => t.Status.ToString());
ca.SetCanceled();
Console.WriteLine(cw.Result);

// --- continuation throws, token canceled ---
Console.WriteLine(Outcome(f.ContinueWhenAny(done, t => throw new InvalidOperationException("in continuation"))));
var cts = new CancellationTokenSource();
var gate = new TaskCompletionSource();
var cancelable = f.ContinueWhenAny(new Task[] { gate.Task }, t => Console.WriteLine("never"), cts.Token);
cts.Cancel();
Console.WriteLine($"canceled early: {cancelable.IsCanceled} {cancelable.Status}");
gate.SetResult();
Console.WriteLine(Outcome(cancelable));
var precanceled = new CancellationTokenSource(); precanceled.Cancel();
Console.WriteLine(Outcome(f.ContinueWhenAny(done, t => Console.WriteLine("never"), precanceled.Token)));
Console.WriteLine(Outcome(f.ContinueWhenAny(done, t => 1, precanceled.Token, TaskContinuationOptions.None, TaskScheduler.Default)));

// --- argument checks ---
Check("null tasks", () => f.ContinueWhenAny((Task[])null!, t => { }));
Check("empty tasks", () => f.ContinueWhenAny(new Task[0], t => { }));
Check("null element", () => f.ContinueWhenAny(new Task[] { Task.CompletedTask, null! }, t => { }));
Check("null action", () => f.ContinueWhenAny(done, (Action<Task>)null!));
Check("null func", () => f.ContinueWhenAny(done, (Func<Task, int>)null!));
Check("null scheduler", () => f.ContinueWhenAny(done, t => { }, CancellationToken.None, TaskContinuationOptions.None, null!));
Check("NotOnFaulted", () => f.ContinueWhenAny(done, t => { }, TaskContinuationOptions.NotOnFaulted));
Check("OnlyOnRanToCompletion", () => f.ContinueWhenAny(done, t => { }, TaskContinuationOptions.OnlyOnRanToCompletion));
Check("LongRunning|ExecuteSynchronously", () => f.ContinueWhenAny(done, t => { }, TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously));
Check("bad options", () => f.ContinueWhenAny(done, t => { }, (TaskContinuationOptions)0x10000000));
Check("generic null tasks", () => f.ContinueWhenAny((Task<int>[])null!, t => { }));
Check("generic empty", () => f.ContinueWhenAny(new Task<int>[0], t => 1));
Check("generic null element", () => f.ContinueWhenAny(new Task<int>[] { null! }, t => 1));
Check("generic null func", () => f.ContinueWhenAny(doneInt, (Func<Task<int>, int>)null!));
Check("generic NotOnCanceled", () => f.ContinueWhenAny(doneInt, t => 1, TaskContinuationOptions.NotOnCanceled));

// --- TaskFactory<TResult> ---
var fi = new TaskFactory<int>();
Console.WriteLine(fi.ContinueWhenAny(done, t => 1).Result);
Console.WriteLine(fi.ContinueWhenAny(done, t => 2, CancellationToken.None).Result);
Console.WriteLine(fi.ContinueWhenAny(done, t => 3, TaskContinuationOptions.None).Result);
Console.WriteLine(fi.ContinueWhenAny(done, t => 4, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
Console.WriteLine(fi.ContinueWhenAny(doneInt, t => t.Result).Result);
Console.WriteLine(fi.ContinueWhenAny(doneInt, t => 6, CancellationToken.None).Result);
Console.WriteLine(fi.ContinueWhenAny(doneInt, t => 7, TaskContinuationOptions.None).Result);
Console.WriteLine(fi.ContinueWhenAny(doneInt, t => 8, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
Check("TResult factory null tasks", () => fi.ContinueWhenAny((Task[])null!, t => 1));
Check("TResult factory NotOnCanceled", () => fi.ContinueWhenAny(done, t => 1, TaskContinuationOptions.NotOnCanceled));
Console.WriteLine(Task<int>.Factory.ContinueWhenAny(done, t => 9).Result);

// --- factory defaults ---
var sch = new InlineScheduler();
var fs = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.None, sch);
Console.WriteLine($"{fs.ContinueWhenAny(done, t => TaskScheduler.Current == sch).Result} {sch.Queued}");
Console.WriteLine($"{fs.ContinueWhenAny(done, t => TaskScheduler.Current == sch, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result} {sch.Queued}");
var cancelled = new CancellationTokenSource(); cancelled.Cancel();
Console.WriteLine(Outcome(new TaskFactory(cancelled.Token).ContinueWhenAny(done, t => { })));

class InlineScheduler : TaskScheduler
{
    public int Queued;
    protected override void QueueTask(Task task) { Interlocked.Increment(ref Queued); TryExecuteTask(task); }
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
    protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
}
