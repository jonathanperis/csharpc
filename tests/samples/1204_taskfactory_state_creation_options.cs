// TaskFactory.ContinueWhenAll / FromAsync: the AsyncState and CreationOptions of the returned tasks.
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

var f = Task.Factory;
Task[] done = { Task.FromResult(1), Task.FromResult(2) };
var ready = new Op(); ready.Complete();
var fd = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, TaskScheduler.Default);
var cd = fd.ContinueWhenAll(done, ts => 12);
Console.WriteLine($"{cd.Result} {cd.CreationOptions}");
var ca = fd.ContinueWhenAny(done, t => 13);
Console.WriteLine($"{ca.Result} {ca.CreationOptions}");
var fl = f.ContinueWhenAll(done, ts => 14, TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.HideScheduler);
Console.WriteLine($"{fl.Result} {fl.CreationOptions}");
var t1 = f.FromAsync(ready, iar => { });
t1.Wait();
Console.WriteLine($"{t1.CreationOptions} {t1.AsyncState == null}");
var t2 = f.FromAsync(ready, iar => { }, TaskCreationOptions.AttachedToParent);
t2.Wait();
Console.WriteLine($"{t2.CreationOptions} {t2.AsyncState == null}");
var state = new object();
var t3 = f.FromAsync((cb, st) => new Op(true), iar => { }, state, TaskCreationOptions.AttachedToParent);
t3.Wait();
Console.WriteLine($"{t3.AsyncState == state} {t3.CreationOptions}");
var t4 = f.FromAsync((cb, st) => new Op(true), iar => 5, state);
Console.WriteLine($"{t4.Result} {t4.AsyncState == state} {t4.CreationOptions}");


class Op : IAsyncResult
{
    private readonly ManualResetEvent _h = new ManualResetEvent(false);
    private volatile bool _done;
    public object AsyncState => null;
    public bool CompletedSynchronously { get; }
    public WaitHandle AsyncWaitHandle => _h;
    public bool IsCompleted => _done;
    public Op(bool sync = false) { CompletedSynchronously = sync; _done = sync; }
    public void Complete() { _done = true; _h.Set(); }
}
