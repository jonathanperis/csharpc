// Task.Status over a task's whole life (created, queued, WaitingForChildrenToComplete, the three final states),
// TaskCanceledException.Task and its token, a new AggregateException for every read of Task.Exception and for
// every Wait, AggregateException.InnerExceptions as one read-only collection, and the argument checks of
// Task.WhenAll / WhenAny / WaitAll / FromCanceled / TaskCompletionSource.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action action)
{
    try { action(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("-- status of a task with an attached child");
var childDone = new TaskCompletionSource();
var bodyDone = new ManualResetEventSlim();
var parent = Task.Factory.StartNew(() =>
{
    Task.Factory.StartNew(() => childDone.Task.Wait(), TaskCreationOptions.AttachedToParent);
    bodyDone.Set();
});
bodyDone.Wait();
var spin = new SpinWait();
while (parent.Status == TaskStatus.Running) spin.SpinOnce();
Console.WriteLine(parent.Status);
Console.WriteLine(parent.IsCompleted);
childDone.SetResult();
parent.Wait();
Console.WriteLine(parent.Status);

var promiseChild = new TaskCompletionSource();
var promiseDone = new ManualResetEventSlim();
var promiseParent = Task.Factory.StartNew(() =>
{
    var p = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
    promiseChild = p;
    promiseDone.Set();
});
promiseDone.Wait();
while (promiseParent.Status == TaskStatus.Running) spin.SpinOnce();
Console.WriteLine(promiseParent.Status);
promiseChild.SetResult();
promiseParent.Wait();
Console.WriteLine(promiseParent.Status);

Console.WriteLine("-- status of cold and started tasks");
var cold = new Task(() => { });
Console.WriteLine(cold.Status);
cold.RunSynchronously();
Console.WriteLine(cold.Status);
var canceled = new Task(() => { }, new CancellationToken(true));
Console.WriteLine(canceled.Status);
var faulty = new Task(() => throw new InvalidOperationException("boom"));
faulty.RunSynchronously();
Console.WriteLine(faulty.Status);
Console.WriteLine(Task.FromCanceled(new CancellationToken(true)).Status);
Console.WriteLine(new TaskCompletionSource().Task.Status);

Console.WriteLine("-- TaskCanceledException.Task and token");
var source = new CancellationTokenSource();
source.Cancel();
var viaToken = Task.FromCanceled(source.Token);
try { viaToken.GetAwaiter().GetResult(); }
catch (TaskCanceledException e)
{
    Console.WriteLine(e.Task == viaToken);
    Console.WriteLine(e.CancellationToken == source.Token);
}
try { viaToken.Wait(); }
catch (AggregateException e)
{
    var inner = (TaskCanceledException)e.InnerException;
    Console.WriteLine(inner.Task == viaToken);
    Console.WriteLine(inner.CancellationToken == source.Token);
    Console.WriteLine(e.InnerExceptions.Count);
}
var viaSource = new TaskCompletionSource<int>();
viaSource.SetCanceled();
try { _ = viaSource.Task.Result; }
catch (AggregateException e)
{
    var inner = (TaskCanceledException)e.InnerException;
    Console.WriteLine(inner.Task == viaSource.Task);
    Console.WriteLine(inner.CancellationToken.IsCancellationRequested);
}
var made = new TaskCanceledException(viaToken);
Console.WriteLine(made.Task == viaToken);
Console.WriteLine(made.CancellationToken == source.Token);
Console.WriteLine(made.Message);
Console.WriteLine(new TaskCanceledException().Task == null);
Console.WriteLine(new TaskCanceledException("text").Message);
Console.WriteLine(new TaskCanceledException("text", new Exception("inner")).InnerException.Message);
Console.WriteLine(new TaskCanceledException("text", null, source.Token).CancellationToken == source.Token);
Console.WriteLine(new TaskCanceledException((Task)null).Task == null);

Console.WriteLine("-- exception identity");
var faulted = Task.FromException(new Exception("kept"));
Console.WriteLine(ReferenceEquals(faulted.Exception, faulted.Exception));
Console.WriteLine(ReferenceEquals(faulted.Exception.InnerException, faulted.Exception.InnerException));
AggregateException first = null, second = null;
try { faulted.Wait(); } catch (AggregateException e) { first = e; }
try { faulted.Wait(); } catch (AggregateException e) { second = e; }
Console.WriteLine(ReferenceEquals(first, second));
Console.WriteLine(ReferenceEquals(first.InnerException, second.InnerException));
Console.WriteLine(first.Message == second.Message);
var aggregate = new AggregateException("m", new Exception("a"), new Exception("b"));
Console.WriteLine(ReferenceEquals(aggregate.InnerExceptions, aggregate.InnerExceptions));
Console.WriteLine(((ICollection<Exception>)aggregate.InnerExceptions).IsReadOnly);
Try("add", () => ((IList<Exception>)aggregate.InnerExceptions).Add(new Exception()));
var many = new TaskCompletionSource<int>();
many.SetException(new[] { new Exception("p"), new Exception("q") });
Console.WriteLine(many.Task.Exception.InnerExceptions.Count + " " + ReferenceEquals(many.Task.Exception, many.Task.Exception));
Console.WriteLine(string.Join(",", many.Task.Exception.InnerExceptions.Select(e => e.Message)));

Console.WriteLine("-- argument checks");
Try("WhenAll(null array)", () => Task.WhenAll((Task[])null));
Try("WhenAll(null enumerable)", () => Task.WhenAll((IEnumerable<Task>)null));
Try("WhenAll<T>(null enumerable)", () => Task.WhenAll((IEnumerable<Task<int>>)null));
Try("WhenAll(null element)", () => Task.WhenAll(new Task[] { Task.CompletedTask, null }));
Try("WhenAll<T>(null element)", () => Task.WhenAll(new Task<int>[] { null }));
Try("WhenAll(empty)", () => Console.WriteLine(Task.WhenAll(new Task[0]).Status));
Try("WhenAny(empty)", () => Task.WhenAny(new Task[0]));
Try("WhenAny(empty enumerable)", () => Task.WhenAny(new List<Task>()));
Try("WhenAny(null)", () => Task.WhenAny((Task[])null));
Try("WhenAny(null enumerable)", () => Task.WhenAny((IEnumerable<Task<int>>)null));
Try("WhenAny(null element)", () => Task.WhenAny(new Task[] { null }));
Try("WaitAll(null)", () => Task.WaitAll((Task[])null));
Try("WaitAll(null element)", () => Task.WaitAll(new Task[] { null }));
Try("WaitAny(null)", () => Task.WaitAny((Task[])null));
Try("FromCanceled(not canceled)", () => Task.FromCanceled(default));
Try("FromCanceled<T>(not canceled)", () => Task.FromCanceled<int>(new CancellationTokenSource().Token));
Try("FromException(null)", () => Task.FromException(null));
Try("SetException(empty)", () => new TaskCompletionSource().SetException(new Exception[0]));
Try("SetException(null element)", () => new TaskCompletionSource().SetException(new Exception[] { null }));
Try("TrySetResult twice", () => { var s = new TaskCompletionSource<int>(); Console.WriteLine(s.TrySetResult(1) + " " + s.TrySetResult(2)); s.SetResult(3); });
