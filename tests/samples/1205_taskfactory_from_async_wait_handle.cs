// TaskFactory.FromAsync over an operation that is not complete yet: the end method runs once its wait handle
// is signaled (an attached task keeps its parent waiting until then).
#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

var f = Task.Factory;
static string Outcome(Task t)
{
    try { t.Wait(); return "ran"; }
    catch (AggregateException e) { return $"{(t.IsCanceled ? "canceled" : "faulted")}: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"; }
}
var later = new Op("later");
Task<string> t3 = f.FromAsync(later, iar => { Console.WriteLine("end later"); return "later done"; });
Console.WriteLine($"before complete: {t3.IsCompleted} {t3.Status}");
later.Signal();
Console.WriteLine($"{t3.Result} {t3.Status}");

var failing = new Op("failing");
var tf = f.FromAsync(failing, iar => throw new InvalidOperationException("end failed later"));
failing.Signal();
Console.WriteLine(Outcome(tf));
var canceling = new Op("canceling");
var tcn = f.FromAsync(canceling, (IAsyncResult iar) => CancelNow());
canceling.Signal();
Console.WriteLine($"{Outcome(tcn)} {tcn.Status}");

// The default factory, a typed factory and a custom scheduler (it is queued: the operation completed on another thread).
var viaFactory = new Op("factory");
var tg = new TaskFactory<int>().FromAsync(viaFactory, iar => 7);
viaFactory.Signal();
Console.WriteLine(tg.Result);
var sched = new CountingScheduler();
var viaSched = new Op("sched");
var ts = f.FromAsync(viaSched, iar => Console.WriteLine($"end on scheduler {TaskScheduler.Current == sched}"), TaskCreationOptions.None, sched);
viaSched.Signal();
ts.Wait();
Console.WriteLine($"inline {sched.Inlined}");

static int CancelNow() => throw new OperationCanceledException();

// An attached FromAsync task keeps its parent from completing.
var attachedOp = new Op("attached");
Task attachedChild = null;
var outer = Task.Factory.StartNew(() =>
{
    attachedChild = f.FromAsync(attachedOp, iar => { }, TaskCreationOptions.AttachedToParent);
});
SpinWait.SpinUntil(() => outer.Status == TaskStatus.WaitingForChildrenToComplete);
Console.WriteLine($"outer: {outer.Status} child: {attachedChild.Status}");
attachedOp.Signal();
outer.Wait();
Console.WriteLine($"outer: {outer.Status} child: {attachedChild.Status}");


class Op : IAsyncResult
{
    public bool Done, Sync;
    private readonly ManualResetEvent _h = new ManualResetEvent(false);
    private readonly AsyncCallback _callback;
    public string Name { get; }
    public object AsyncState { get; }
    public bool CompletedSynchronously => Sync;
    public WaitHandle AsyncWaitHandle => _h;
    public bool IsCompleted => Done;
    public Op(string name) { Name = name; }
    public void Signal() { Done = true; _h.Set(); }
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
