// TaskFactory.FromAsync: the IAsyncResult and Begin/End overloads, state, options, schedulers, failures.
#nullable disable
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
int mainThread = Environment.CurrentManagedThreadId;

// --- FromAsync(IAsyncResult, ...) over a completed operation: the end method runs before FromAsync returns ---
var ready = new Op("ready", completedSynchronously: true);
ready.Complete();
Task t1 = f.FromAsync(ready, iar => Console.WriteLine($"end1 {((Op)iar).Name} same thread {Environment.CurrentManagedThreadId == mainThread}"));
Console.WriteLine($"returned: {t1.Status} state {t1.AsyncState}");
Task<int> t2 = f.FromAsync(ready, iar => 42);
Console.WriteLine($"{t2.Result} {t2.Status} {t2.AsyncState}");
f.FromAsync(ready, iar => Console.WriteLine("end3"), TaskCreationOptions.None).Wait();
f.FromAsync(ready, iar => Console.WriteLine("end4"), TaskCreationOptions.None, TaskScheduler.Default).Wait();
Console.WriteLine(f.FromAsync(ready, iar => 5, TaskCreationOptions.None).Result);
Console.WriteLine(f.FromAsync(ready, iar => 6, TaskCreationOptions.None, TaskScheduler.Default).Result);
Console.WriteLine(new TaskFactory<string>().FromAsync(ready, iar => "s1").Result);
Console.WriteLine(new TaskFactory<string>().FromAsync(ready, iar => "s2", TaskCreationOptions.None).Result);
Console.WriteLine(new TaskFactory<string>().FromAsync(ready, iar => "s3", TaskCreationOptions.None, TaskScheduler.Default).Result);
Console.WriteLine(Task<int>.Factory.FromAsync(ready, iar => 7).Result);

// --- a failing end method; a canceling one ---
Console.WriteLine(Outcome(f.FromAsync(ready, iar => throw new InvalidOperationException("end failed"))));
Console.WriteLine(Outcome(f.FromAsync(ready, (IAsyncResult iar) => { throw new OperationCanceledException(); })));
Task<int> tc = f.FromAsync(ready, (IAsyncResult iar) => CancelNow());
Console.WriteLine($"{Outcome(tc)} {tc.Status}");

// --- a custom scheduler: queued or inlined? ---
var sch = new CountingScheduler();
f.FromAsync(ready, iar => Console.WriteLine($"on scheduler {TaskScheduler.Current == sch}"), TaskCreationOptions.None, sch).Wait();
Console.WriteLine($"queued {sch.Queued} inline {sch.Inlined}");
var sch2 = new CountingScheduler { RefuseInline = true };
f.FromAsync(ready, iar => Console.WriteLine($"on scheduler {TaskScheduler.Current == sch2}"), TaskCreationOptions.None, sch2).Wait();
Console.WriteLine($"queued {sch2.Queued} inline {sch2.Inlined}");

// --- Begin/End, no arguments ---
var s = new object();
Task t5 = f.FromAsync((cb, st) => new Op("b0", st, cb, completeNow: true), iar => Console.WriteLine($"end5 {((Op)iar).Name}"), s);
t5.Wait();
Console.WriteLine($"{t5.AsyncState == s} {t5.Status}");
f.FromAsync((cb, st) => new Op("b0o", st, cb, completeNow: true), iar => Console.WriteLine("end5o"), null, TaskCreationOptions.None).Wait();
Console.WriteLine(f.FromAsync((cb, st) => new Op("r0", st, cb, completeNow: true), iar => ((Op)iar).Name + "!", "st").Result);
Console.WriteLine(f.FromAsync((cb, st) => new Op("r0o", st, cb, completeNow: true), iar => ((Op)iar).Name + "!", null, TaskCreationOptions.None).Result);

// --- with 1..3 arguments ---
f.FromAsync<int>((a1, cb, st) => new Op($"a1={a1}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 1, null).Wait();
f.FromAsync<int>((a1, cb, st) => new Op($"a1={a1}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 2, null, TaskCreationOptions.None).Wait();
f.FromAsync<int, string>((a1, a2, cb, st) => new Op($"{a1},{a2}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 1, "two", null).Wait();
f.FromAsync<int, string>((a1, a2, cb, st) => new Op($"{a1},{a2}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 1, "two", null, TaskCreationOptions.None).Wait();
f.FromAsync<int, string, double>((a1, a2, a3, cb, st) => new Op($"{a1},{a2},{a3}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 1, "two", 3.5, null).Wait();
f.FromAsync<int, string, double>((a1, a2, a3, cb, st) => new Op($"{a1},{a2},{a3}", st, cb, completeNow: true), iar => Console.WriteLine($"end {((Op)iar).Name}"), 1, "two", 3.5, null, TaskCreationOptions.None).Wait();
Console.WriteLine(f.FromAsync<int, string>((a1, cb, st) => new Op($"{a1}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, "state").Result);
Console.WriteLine(f.FromAsync<int, string>((a1, cb, st) => new Op($"{a1}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, null, TaskCreationOptions.None).Result);
Console.WriteLine(f.FromAsync<int, string, string>((a1, a2, cb, st) => new Op($"{a1}{a2}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, "x", null).Result);
Console.WriteLine(f.FromAsync<int, string, string>((a1, a2, cb, st) => new Op($"{a1}{a2}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, "x", null, TaskCreationOptions.None).Result);
Console.WriteLine(f.FromAsync<int, string, double, string>((a1, a2, a3, cb, st) => new Op($"{a1}{a2}{a3}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, "x", 1.5, null).Result);
Console.WriteLine(f.FromAsync<int, string, double, string>((a1, a2, a3, cb, st) => new Op($"{a1}{a2}{a3}", st, cb, completeNow: true), iar => ((Op)iar).Name + "?", 9, "x", 1.5, null, TaskCreationOptions.None).Result);

// TaskFactory<TResult> Begin/End
var fs = new TaskFactory<string>();
Console.WriteLine(fs.FromAsync((cb, st) => new Op("g0", st, cb, completeNow: true), iar => ((Op)iar).Name, null).Result);
Console.WriteLine(fs.FromAsync((cb, st) => new Op("g0o", st, cb, completeNow: true), iar => ((Op)iar).Name, null, TaskCreationOptions.None).Result);
Console.WriteLine(fs.FromAsync<int>((a1, cb, st) => new Op($"g{a1}", st, cb, completeNow: true), iar => ((Op)iar).Name, 1, null).Result);
Console.WriteLine(fs.FromAsync<int>((a1, cb, st) => new Op($"g{a1}", st, cb, completeNow: true), iar => ((Op)iar).Name, 2, null, TaskCreationOptions.None).Result);
Console.WriteLine(fs.FromAsync<int, int>((a1, a2, cb, st) => new Op($"g{a1}{a2}", st, cb, completeNow: true), iar => ((Op)iar).Name, 1, 2, null).Result);
Console.WriteLine(fs.FromAsync<int, int>((a1, a2, cb, st) => new Op($"g{a1}{a2}", st, cb, completeNow: true), iar => ((Op)iar).Name, 1, 3, null, TaskCreationOptions.None).Result);
Console.WriteLine(fs.FromAsync<int, int, int>((a1, a2, a3, cb, st) => new Op($"g{a1}{a2}{a3}", st, cb, completeNow: true), iar => ((Op)iar).Name, 1, 2, 3, null).Result);
Console.WriteLine(fs.FromAsync<int, int, int>((a1, a2, a3, cb, st) => new Op($"g{a1}{a2}{a3}", st, cb, completeNow: true), iar => ((Op)iar).Name, 1, 2, 4, null, TaskCreationOptions.None).Result);

// --- Begin/End: the callback fires later on another thread ---
Op deferred = null!;
Task<string> t6 = f.FromAsync((cb, st) => deferred = new Op("deferred", st, cb, completeNow: false), iar => "end:" + ((Op)iar).Name, "state6");
Console.WriteLine($"deferred: {t6.IsCompleted} {t6.AsyncState}");
deferred.CompleteWithCallback();
Console.WriteLine($"{t6.Result} {t6.Status}");

// --- failures ---
Check("begin throws", () => f.FromAsync<int>((cb, st) => throw new InvalidOperationException("begin failed"), iar => 1, null));
Check("begin returns null", () => f.FromAsync((cb, st) => null!, iar => { }, null));
Console.WriteLine(Outcome(f.FromAsync((cb, st) => new Op("e", st, cb, completeNow: true), iar => throw new InvalidOperationException("end failed in begin/end"), null)));
var tcn = f.FromAsync((cb, st) => new Op("e", st, cb, completeNow: true), (IAsyncResult iar) => { throw new OperationCanceledException(); }, null);
Console.WriteLine($"{Outcome(tcn)} {tcn.Status}");

// --- argument checks ---
Check("null asyncResult", () => f.FromAsync((IAsyncResult)null!, iar => { }));
Check("null endMethod", () => f.FromAsync(ready, (Action<IAsyncResult>)null!));
Check("null endFunction", () => f.FromAsync(ready, (Func<IAsyncResult, int>)null!));
Check("null scheduler", () => f.FromAsync(ready, iar => { }, TaskCreationOptions.None, null!));
Check("null begin", () => f.FromAsync((Func<AsyncCallback, object, IAsyncResult>)null!, iar => { }, null));
Check("null end", () => f.FromAsync((cb, st) => new Op("x", st, cb, completeNow: true), (Action<IAsyncResult>)null!, null));
Check("null end func", () => f.FromAsync((cb, st) => new Op("x", st, cb, completeNow: true), (Func<IAsyncResult, int>)null!, null));
Check("null begin1", () => f.FromAsync((Func<int, AsyncCallback, object, IAsyncResult>)null!, iar => { }, 1, null));
Check("null begin2", () => f.FromAsync((Func<int, int, AsyncCallback, object, IAsyncResult>)null!, iar => { }, 1, 2, null));
Check("null begin3", () => f.FromAsync((Func<int, int, int, AsyncCallback, object, IAsyncResult>)null!, iar => { }, 1, 2, 3, null));
Check("LongRunning", () => f.FromAsync(ready, iar => { }, TaskCreationOptions.LongRunning));
Check("PreferFairness", () => f.FromAsync(ready, iar => { }, TaskCreationOptions.PreferFairness));
Check("LongRunning begin/end", () => f.FromAsync((cb, st) => new Op("x", st, cb, completeNow: true), iar => { }, null, TaskCreationOptions.LongRunning));
foreach (TaskCreationOptions o in new[] { TaskCreationOptions.AttachedToParent, TaskCreationOptions.DenyChildAttach, TaskCreationOptions.HideScheduler, TaskCreationOptions.RunContinuationsAsynchronously })
    Check("option " + o, () => f.FromAsync(ready, iar => { }, o).Wait());
foreach (TaskCreationOptions o in new[] { TaskCreationOptions.AttachedToParent, TaskCreationOptions.DenyChildAttach, TaskCreationOptions.HideScheduler, TaskCreationOptions.RunContinuationsAsynchronously, TaskCreationOptions.PreferFairness })
    Check("begin/end option " + o, () => f.FromAsync((cb, st) => new Op("x", st, cb, completeNow: true), iar => { }, null, o).Wait());
Check("bad options", () => f.FromAsync(ready, iar => { }, (TaskCreationOptions)0x10000000));
Check("AttachedToParent ok", () => f.FromAsync(ready, iar => { }, TaskCreationOptions.AttachedToParent).Wait());

static int CancelNow() => throw new OperationCanceledException();

class Op : IAsyncResult
{
    private readonly ManualResetEvent _handle = new ManualResetEvent(false);
    private readonly AsyncCallback _callback;
    private volatile bool _completed;
    public string Name { get; }
    public object AsyncState { get; }
    public bool CompletedSynchronously { get; }
    public WaitHandle AsyncWaitHandle => _handle;
    public bool IsCompleted => _completed;

    public Op(string name, bool completedSynchronously) { Name = name; CompletedSynchronously = completedSynchronously; }
    public Op(string name, object state, AsyncCallback callback, bool completeNow)
    {
        Name = name; AsyncState = state; _callback = callback;
        if (completeNow) { CompletedSynchronously = true; Complete(); callback?.Invoke(this); }
    }
    public void Complete() { _completed = true; _handle.Set(); }
    public void CompleteWithCallback() { Complete(); _callback?.Invoke(this); }
}

class CountingScheduler : TaskScheduler
{
    public int Queued, Inlined;
    public bool RefuseInline;
    protected override void QueueTask(Task task) { Interlocked.Increment(ref Queued); TryExecuteTask(task); }
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        Interlocked.Increment(ref Inlined);
        return !RefuseInline && TryExecuteTask(task);
    }
    protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
}
