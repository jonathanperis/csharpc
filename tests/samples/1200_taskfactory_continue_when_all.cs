// TaskFactory.ContinueWhenAll: every overload shape, argument checks, outcomes.
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

// --- ContinueWhenAll, non-generic factory ---
Task a1 = f.ContinueWhenAll(done, ts => Console.WriteLine($"all1 {ts.Length} {ts[0] == done[0]} {ReferenceEquals(ts, done)}"));
a1.Wait();
Console.WriteLine($"{a1.Status} {a1.IsCompletedSuccessfully}");
f.ContinueWhenAll(done, ts => Console.WriteLine("all2 token"), CancellationToken.None).Wait();
f.ContinueWhenAll(done, ts => Console.WriteLine("all3 options"), TaskContinuationOptions.ExecuteSynchronously).Wait();
f.ContinueWhenAll(done, ts => Console.WriteLine("all4 full"), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Wait();
Task<string> r1 = f.ContinueWhenAll(done, ts => "r" + ts.Length);
Console.WriteLine($"{r1.Result}");
Console.WriteLine(f.ContinueWhenAll(done, ts => "r2", CancellationToken.None).Result);
Console.WriteLine(f.ContinueWhenAll(done, ts => "r3", TaskContinuationOptions.None).Result);
Console.WriteLine(f.ContinueWhenAll(done, ts => "r4", CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
f.ContinueWhenAll(doneInt, ts => Console.WriteLine($"g1 {ts.Sum(t => t.Result)} {ts.GetType().Name}")).Wait();
f.ContinueWhenAll(doneInt, ts => Console.WriteLine("g2"), CancellationToken.None).Wait();
f.ContinueWhenAll(doneInt, ts => Console.WriteLine("g3"), TaskContinuationOptions.None).Wait();
f.ContinueWhenAll(doneInt, ts => Console.WriteLine("g4"), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Wait();
Console.WriteLine(f.ContinueWhenAll(doneInt, ts => ts.Sum(t => t.Result)).Result);
Console.WriteLine(f.ContinueWhenAll(doneInt, ts => ts.Length, CancellationToken.None).Result);
Console.WriteLine(f.ContinueWhenAll(doneInt, ts => ts.Length + 1, TaskContinuationOptions.None).Result);
Console.WriteLine(f.ContinueWhenAll(doneInt, ts => ts.Length + 2, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);

// --- ContinueWhenAll with tasks completing later, and mixed outcomes ---
var t1 = new TaskCompletionSource<int>();
var t2 = new TaskCompletionSource<int>();
var t3 = new TaskCompletionSource<int>();
var pending = f.ContinueWhenAll(new Task[] { t1.Task, t2.Task, t3.Task }, ts => string.Join(",", ts.Select(t => t.Status)));
t2.SetResult(2);
Console.WriteLine($"after one: {pending.IsCompleted}");
t1.SetException(new InvalidOperationException("boom"));
Console.WriteLine($"after two: {pending.IsCompleted}");
t3.SetCanceled();
Console.WriteLine($"{pending.Result} {pending.Status}");

// --- the continuation throws / the token is canceled ---
Console.WriteLine(Outcome(f.ContinueWhenAll(done, ts => throw new InvalidOperationException("in continuation"))));
var cts = new CancellationTokenSource();
var gate = new TaskCompletionSource();
var cancelable = f.ContinueWhenAll(new Task[] { gate.Task }, ts => Console.WriteLine("never"), cts.Token);
cts.Cancel();
Console.WriteLine($"canceled early: {cancelable.IsCanceled} {cancelable.Status}");
gate.SetResult();
Console.WriteLine(Outcome(cancelable));
var precanceled = new CancellationTokenSource(); precanceled.Cancel();
var pc = f.ContinueWhenAll(done, ts => Console.WriteLine("never"), precanceled.Token);
Console.WriteLine($"pre-canceled: {Outcome(pc)} {pc.Status}");
var pc2 = f.ContinueWhenAll(done, ts => "never", precanceled.Token, TaskContinuationOptions.None, TaskScheduler.Default);
Console.WriteLine($"pre-canceled result: {Outcome(pc2)} {pc2.Status}");

// --- argument checks ---
Check("null tasks", () => f.ContinueWhenAll((Task[])null!, ts => { }));
Check("empty tasks", () => f.ContinueWhenAll(new Task[0], ts => { }));
Check("null element", () => f.ContinueWhenAll(new Task[] { Task.CompletedTask, null! }, ts => { }));
Check("null action", () => f.ContinueWhenAll(done, (Action<Task[]>)null!));
Check("null func", () => f.ContinueWhenAll(done, (Func<Task[], int>)null!));
Check("null scheduler", () => f.ContinueWhenAll(done, ts => { }, CancellationToken.None, TaskContinuationOptions.None, null!));
Check("NotOnFaulted", () => f.ContinueWhenAll(done, ts => { }, TaskContinuationOptions.NotOnFaulted));
Check("NotOnRanToCompletion", () => f.ContinueWhenAll(done, ts => { }, TaskContinuationOptions.NotOnRanToCompletion));
Check("OnlyOnCanceled", () => f.ContinueWhenAll(done, ts => { }, TaskContinuationOptions.OnlyOnCanceled));
Check("LongRunning|ExecuteSynchronously", () => f.ContinueWhenAll(done, ts => { }, TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously));
Check("bad options", () => f.ContinueWhenAll(done, ts => { }, (TaskContinuationOptions)0x10000000));
Check("generic null tasks", () => f.ContinueWhenAll((Task<int>[])null!, ts => { }));
Check("generic empty", () => f.ContinueWhenAll(new Task<int>[0], ts => 1));
Check("generic null element", () => f.ContinueWhenAll(new Task<int>[] { null! }, ts => 1));
Check("generic null func", () => f.ContinueWhenAll(doneInt, (Func<Task<int>[], int>)null!));
Check("generic NotOnFaulted", () => f.ContinueWhenAll(doneInt, ts => 1, TaskContinuationOptions.NotOnFaulted));
Check("default token", () => f.ContinueWhenAll(done, ts => { }, default(CancellationToken)).Wait());

// --- the array is a copy ---
var gate2 = new TaskCompletionSource();
var src = new Task[] { gate2.Task, Task.CompletedTask };
Task<bool> copy = f.ContinueWhenAll(src, ts => ReferenceEquals(ts, src));
gate2.SetResult();
Console.WriteLine(copy.Result);

// --- TaskFactory<TResult> ---
var fi = new TaskFactory<int>();
Console.WriteLine(fi.ContinueWhenAll(done, ts => ts.Length).Result);
Console.WriteLine(fi.ContinueWhenAll(done, ts => 2, CancellationToken.None).Result);
Console.WriteLine(fi.ContinueWhenAll(done, ts => 3, TaskContinuationOptions.None).Result);
Console.WriteLine(fi.ContinueWhenAll(done, ts => 4, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
Console.WriteLine(fi.ContinueWhenAll(doneInt, ts => ts.Length * 10).Result);
Console.WriteLine(fi.ContinueWhenAll(doneInt, ts => 6, CancellationToken.None).Result);
Console.WriteLine(fi.ContinueWhenAll(doneInt, ts => 7, TaskContinuationOptions.None).Result);
Console.WriteLine(fi.ContinueWhenAll(doneInt, ts => 8, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
Check("TResult factory null tasks", () => fi.ContinueWhenAll((Task[])null!, ts => 1));
Check("TResult factory NotOnCanceled", () => fi.ContinueWhenAll(done, ts => 1, TaskContinuationOptions.NotOnCanceled));
Console.WriteLine(Task<int>.Factory.ContinueWhenAll(done, ts => 9).Result);

// --- factory defaults apply: scheduler, token, continuation options ---
var sch = new InlineScheduler();
var fs = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.None, sch);
Console.WriteLine(fs.ContinueWhenAll(done, ts => TaskScheduler.Current == sch).Result);
Console.WriteLine(sch.Queued);
Console.WriteLine(fs.ContinueWhenAll(done, ts => TaskScheduler.Current == sch, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Result);
Console.WriteLine(sch.Queued);
var fc = new TaskFactory(new CancellationTokenSource().Token);
Console.WriteLine(fc.ContinueWhenAll(done, ts => 5).Result);
var cancelled = new CancellationTokenSource(); cancelled.Cancel();
var fcc = new TaskFactory(cancelled.Token);
Console.WriteLine(Outcome(fcc.ContinueWhenAll(done, ts => { })));
var fe = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
Console.WriteLine(fe.ContinueWhenAll(done, ts => 11).Result);
class InlineScheduler : TaskScheduler
{
    public int Queued;
    protected override void QueueTask(Task task) { Interlocked.Increment(ref Queued); TryExecuteTask(task); }
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
    protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
}
