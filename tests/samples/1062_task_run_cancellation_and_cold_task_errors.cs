// Task.Run(Func<Task>) looks for an OperationCanceledException (UnwrapPromise lookForOce): a lambda that throws it
// yields a Canceled task, as an async method would, while Task.Run(Func<T>) and an Action fault. And the errors of
// Start / RunSynchronously on tasks that are not cold: a continuation task, a started task, a completed one.
using System;
using System.Threading;
using System.Threading.Tasks;

var cts = new CancellationTokenSource();
cts.Cancel();

static void Report(string label, Task task)
{
    try { task.Wait(); Console.WriteLine($"{label}: {task.Status}"); }
    catch (AggregateException e) { Console.WriteLine($"{label}: {task.Status} {e.InnerException!.GetType().Name} {task.IsCanceled} {task.IsFaulted}"); }
}

Report("run Func<Task> throwing OCE(token)", Task.Run(() => { throw new OperationCanceledException(cts.Token); }));
Report("run Func<Task> throwing OCE", Task.Run(() => { throw new OperationCanceledException(); }));
Report("run Func<Task> throwing other", Task.Run(() => { throw new InvalidOperationException("x"); }));
Report("run Func<Task<int>> throwing OCE", Task.Run((Func<Task<int>>)(() => throw new OperationCanceledException())));
Report("run Func<int> throwing OCE", Task.Run(() => { if (cts.IsCancellationRequested) throw new OperationCanceledException(cts.Token); return 1; }));
Report("run Action throwing OCE", Task.Run((Action)(() => throw new OperationCanceledException())));
Report("run async throwing OCE", Task.Run(async () => { await Task.Yield(); throw new OperationCanceledException(); }));
Report("run with token throwing OCE(token)", Task.Run(() => { throw new OperationCanceledException(cts.Token); }, cts.Token));
Report("run returning null Task", Task.Run((Func<Task>)(() => null!)));

var awaited = Task.Run(() => { throw new OperationCanceledException(); });
try { await awaited; } catch (OperationCanceledException e) { Console.WriteLine("await: " + e.GetType().Name + " " + awaited.Status); }

Console.WriteLine("-- cold task errors");
static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

var antecedent = new Task(() => { });
var continuation = antecedent.ContinueWith(_ => { });
Try("start continuation", () => continuation.Start());
Try("run continuation synchronously", () => continuation.RunSynchronously());
var continuationOfT = antecedent.ContinueWith(_ => 5);
Try("start continuation<T>", () => continuationOfT.Start());
Try("run continuation<T> synchronously", () => continuationOfT.RunSynchronously());
var withState = antecedent.ContinueWith((_, s) => { }, "state");
Try("start continuation with state", () => withState.Start());
antecedent.Start();
continuation.Wait();
Console.WriteLine(continuation.Status + " " + continuationOfT.Result + " " + withState.AsyncState);
Try("start completed continuation", () => continuation.Start());

var promise = new TaskCompletionSource().Task;
Try("start promise", () => promise.Start());
Try("run promise synchronously", () => promise.RunSynchronously());
var gate = new ManualResetEventSlim();
var started = new Task(() => gate.Wait());
started.Start();
Try("start twice", () => started.Start());
Try("run started synchronously", () => started.RunSynchronously());
gate.Set();
started.Wait();
Try("start completed", () => started.Start());
Try("run completed synchronously", () => started.RunSynchronously());
