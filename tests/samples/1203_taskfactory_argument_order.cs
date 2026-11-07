#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

static void Check(string what, Action a)
{
    try { a(); Console.WriteLine($"{what}: no exception"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message.Replace("\r", "").Replace("\n", "|")}"); }
}
var f = Task.Factory;
Task[] done = { Task.CompletedTask };
Task[] none = new Task[0];
// ContinueWhenAll order
Check("all: null tasks, null action, bad opts", () => f.ContinueWhenAll(null, (Action<Task[]>)null, TaskContinuationOptions.NotOnFaulted));
Check("all: null tasks, null action", () => f.ContinueWhenAll(null, (Action<Task[]>)null));
Check("all: empty, null action", () => f.ContinueWhenAll(none, (Action<Task[]>)null));
Check("all: done, null action, null sched", () => f.ContinueWhenAll(done, (Action<Task[]>)null, CancellationToken.None, TaskContinuationOptions.None, null));
Check("all: done, action, null sched, bad opts", () => f.ContinueWhenAll(done, t => { }, CancellationToken.None, TaskContinuationOptions.NotOnFaulted, null));
Check("all: null tasks, null sched", () => f.ContinueWhenAll(null, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("all: empty, null sched", () => f.ContinueWhenAll(none, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("all: bad opts + null element", () => f.ContinueWhenAll(new Task[] { null }, t => { }, TaskContinuationOptions.NotOnFaulted));
Check("all: canceled token + null action", () => { var c = new CancellationTokenSource(); c.Cancel(); f.ContinueWhenAll(done, (Action<Task[]>)null, c.Token); });
Check("any: null tasks, null action, bad opts", () => f.ContinueWhenAny(null, (Action<Task>)null, TaskContinuationOptions.NotOnFaulted));
Check("any: empty, null action", () => f.ContinueWhenAny(none, (Action<Task>)null));
Check("any: done, action, null sched, bad opts", () => f.ContinueWhenAny(done, t => { }, CancellationToken.None, TaskContinuationOptions.NotOnFaulted, null));
Check("any: null tasks, null sched", () => f.ContinueWhenAny(null, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
// default token on a TaskFactory<TResult>.ContinueWhenAll(null func)
Check("tf: null func + empty", () => new TaskFactory<int>().ContinueWhenAll(none, (Func<Task[], int>)null));
// more ContinueWhenAll / ContinueWhenAny orders
Check("all null tasks bad opts", () => f.ContinueWhenAll(null, t => { }, TaskContinuationOptions.NotOnFaulted));
Check("all null tasks bad opts func", () => f.ContinueWhenAll(null, t => 1, TaskContinuationOptions.NotOnFaulted));
Check("any null tasks bad opts", () => f.ContinueWhenAny(null, t => { }, TaskContinuationOptions.NotOnFaulted));
Check("any empty bad opts", () => f.ContinueWhenAny(none, t => { }, TaskContinuationOptions.NotOnFaulted));
Check("any empty null sched", () => f.ContinueWhenAny(none, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("any null elem null sched", () => f.ContinueWhenAny(new Task[] { null }, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("all null elem null sched", () => f.ContinueWhenAll(new Task[] { null }, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("all generic empty null sched", () => f.ContinueWhenAll(new Task<int>[0], t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("any generic null tasks null sched", () => f.ContinueWhenAny((Task<int>[])null, t => { }, CancellationToken.None, TaskContinuationOptions.None, null));
Check("tf all empty null sched", () => new TaskFactory<int>().ContinueWhenAll(none, t => 1, CancellationToken.None, TaskContinuationOptions.None, null));
// FromAsync order
var op = new Op();
Check("fa: null ar, null end", () => f.FromAsync((IAsyncResult)null, (Action<IAsyncResult>)null));
Check("fa: null ar, bad opts", () => f.FromAsync((IAsyncResult)null, iar => { }, TaskCreationOptions.LongRunning));
Check("fa: null end, null sched", () => f.FromAsync(op, (Action<IAsyncResult>)null, TaskCreationOptions.None, null));
Check("fa: null sched, bad opts", () => f.FromAsync(op, iar => { }, TaskCreationOptions.LongRunning, null));
Check("fa: null end, bad opts", () => f.FromAsync(op, (Action<IAsyncResult>)null, TaskCreationOptions.LongRunning));
Check("fa: null begin, null end", () => f.FromAsync((Func<AsyncCallback, object, IAsyncResult>)null, (Action<IAsyncResult>)null, null));
Check("fa: null begin, bad opts", () => f.FromAsync((Func<AsyncCallback, object, IAsyncResult>)null, iar => { }, null, TaskCreationOptions.LongRunning));
Check("fa: null end, bad opts (begin/end)", () => f.FromAsync((cb, st) => op, (Action<IAsyncResult>)null, null, TaskCreationOptions.LongRunning));
Check("fa: ar+AttachedToParent|LongRunning", () => f.FromAsync(op, iar => { }, TaskCreationOptions.AttachedToParent | TaskCreationOptions.LongRunning));
Check("fa: be+AttachedToParent|LongRunning", () => f.FromAsync((cb, st) => op, iar => { }, null, TaskCreationOptions.AttachedToParent | TaskCreationOptions.LongRunning));
Check("fa: be+LongRunning|PreferFairness", () => f.FromAsync((cb, st) => op, iar => { }, null, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning));
Check("fa: begin returns incomplete op, never completes (just call)", () => { var t = f.FromAsync((cb, st) => new Op(), iar => { }, null); Console.WriteLine(t.IsCompleted); });
// begin whose callback fires before it returns but reports CompletedSynchronously false
Check("fa: callback inside begin, CompletedSynchronously=false", () =>
{
    var t = f.FromAsync((cb, st) => { var o = new Op { Done = true }; cb(o); return o; }, iar => Console.WriteLine("end ran"), null);
    t.Wait();
    Console.WriteLine(t.Status);
});
Check("fa: callback inside begin AND CompletedSynchronously=true", () =>
{
    int ends = 0;
    var t = f.FromAsync((cb, st) => { var o = new Op { Done = true, Sync = true }; cb(o); return o; }, iar => Interlocked.Increment(ref ends), null);
    t.Wait();
    Console.WriteLine($"{t.Status} {ends}");
});
Check("fa: begin returns completed-sync op without calling back", () =>
{
    int ends = 0;
    var t = f.FromAsync((cb, st) => new Op { Done = true, Sync = true }, iar => Interlocked.Increment(ref ends), null);
    t.Wait();
    Console.WriteLine($"{t.Status} {ends}");
});
Check("fa: begin returns completed op (not sync) without calling back", () =>
{
    var t = f.FromAsync((cb, st) => new Op { Done = true }, iar => { }, null);
    Console.WriteLine($"{t.IsCompleted}");
});
class Op : IAsyncResult
{
    public bool Done, Sync;
    private readonly ManualResetEvent _h = new ManualResetEvent(false);
    public object AsyncState => null;
    public bool CompletedSynchronously => Sync;
    public WaitHandle AsyncWaitHandle => _h;
    public bool IsCompleted => Done;
    public void Signal() => _h.Set();
}
