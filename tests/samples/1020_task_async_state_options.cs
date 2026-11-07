// Task.AsyncState and Task.CreationOptions for every way a task is created (new Task, Task.Factory.StartNew,
// ContinueWith, TaskCompletionSource, Task.Run and the library's own completed tasks), Task as IAsyncResult
// (AsyncState, AsyncWaitHandle, IsCompleted, CompletedSynchronously), TaskCompletionSource's state and options
// arguments with their checks, and Task.Dispose.
using System;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action action)
{
    try { action(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

var marker = new object();
Console.WriteLine("-- AsyncState");
Console.WriteLine(new Task(() => { }).AsyncState == null);
Console.WriteLine(new Task(s => { }, "cold").AsyncState);
Console.WriteLine(new Task<int>(s => 1, 77).AsyncState);
var started = Task.Factory.StartNew(s => 5, marker);
Console.WriteLine(started.AsyncState == marker);
Console.WriteLine(started.Result);
Console.WriteLine(Task.Factory.StartNew(s => { }, "act", CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).AsyncState);
Console.WriteLine(Task.Factory.StartNew(() => 1).AsyncState == null);
Console.WriteLine(Task.Run(() => 1).AsyncState == null);
Console.WriteLine(Task.CompletedTask.AsyncState == null);
Console.WriteLine(Task.FromResult(2).AsyncState == null);
Console.WriteLine(Task.CompletedTask.ContinueWith((t, s) => { }, "continuation").AsyncState);
Console.WriteLine(Task.FromResult(3).ContinueWith((t, s) => 5, "continuation of T").AsyncState);
Console.WriteLine(Task.FromResult(3).ContinueWith((t, s) => 5, "with options", TaskContinuationOptions.ExecuteSynchronously).AsyncState);
Console.WriteLine(new TaskCompletionSource<int>("tcs").Task.AsyncState);
Console.WriteLine(new TaskCompletionSource(42, TaskCreationOptions.None).Task.AsyncState);
Console.WriteLine(new TaskCompletionSource().Task.AsyncState == null);

Console.WriteLine("-- CreationOptions");
Console.WriteLine(new Task(() => { }).CreationOptions);
Console.WriteLine(new Task(() => { }, TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness).CreationOptions);
Console.WriteLine(new Task(() => { }, TaskCreationOptions.HideScheduler).CreationOptions);
Console.WriteLine(new Task<int>(() => 1, TaskCreationOptions.DenyChildAttach).CreationOptions);
Console.WriteLine(Task.Run(() => 1).CreationOptions);
Console.WriteLine(Task.Run(() => { }).CreationOptions);
Console.WriteLine(Task.Run(() => { }, CancellationToken.None).CreationOptions);
Console.WriteLine(Task.Run(() => Task.CompletedTask).CreationOptions);
Console.WriteLine(Task.CompletedTask.CreationOptions);
Console.WriteLine(Task.FromResult(1).CreationOptions);
Console.WriteLine(Task.FromException(new Exception()).CreationOptions);
Console.WriteLine(Task.Delay(1).CreationOptions);
Console.WriteLine(Task.Factory.StartNew(() => 1, TaskCreationOptions.PreferFairness).CreationOptions);
Console.WriteLine(Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).CreationOptions);
Console.WriteLine(Task.CompletedTask.ContinueWith(_ => { }, TaskContinuationOptions.LongRunning | TaskContinuationOptions.OnlyOnRanToCompletion).CreationOptions);
Console.WriteLine(Task.CompletedTask.ContinueWith(_ => { }).CreationOptions);
Console.WriteLine(Task.CompletedTask.ContinueWith(_ => 1, TaskContinuationOptions.ExecuteSynchronously).CreationOptions);
Console.WriteLine(new TaskCompletionSource().Task.CreationOptions);
Console.WriteLine(new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task.CreationOptions);
Console.WriteLine(new TaskCompletionSource<int>(5, TaskCreationOptions.RunContinuationsAsynchronously).Task.CreationOptions);

Console.WriteLine("-- TaskCompletionSource arguments");
Try("LongRunning", () => new TaskCompletionSource<int>(TaskCreationOptions.LongRunning));
Try("DenyChildAttach", () => new TaskCompletionSource(TaskCreationOptions.DenyChildAttach));
Try("HideScheduler", () => new TaskCompletionSource(TaskCreationOptions.HideScheduler));
Try("PreferFairness", () => new TaskCompletionSource("s", TaskCreationOptions.PreferFairness));
Try("undefined bit", () => new TaskCompletionSource<int>((TaskCreationOptions)0x10000));
Try("AttachedToParent", () => Console.WriteLine(new TaskCompletionSource(TaskCreationOptions.AttachedToParent).Task.CreationOptions));
Try("task options", () => new Task(() => { }, (TaskCreationOptions)0x10000));
Try("null action", () => new Task((Action)null));
Try("TrySetException(null)", () => new TaskCompletionSource().TrySetException((Exception)null));
Try("SetException(null)", () => new TaskCompletionSource<int>().SetException((Exception)null));

Console.WriteLine("-- IAsyncResult");
IAsyncResult result = Task.Factory.StartNew(s => 1, "iar");
Console.WriteLine(result.AsyncState);
Console.WriteLine(result.AsyncWaitHandle.WaitOne(10000));
Console.WriteLine(result.IsCompleted);
Console.WriteLine(result.CompletedSynchronously);
IAsyncResult done = Task.CompletedTask;
Console.WriteLine(done.CompletedSynchronously + " " + done.IsCompleted + " " + done.AsyncWaitHandle.WaitOne(0));
Console.WriteLine(done.AsyncWaitHandle.GetType().Name);
var pending = new TaskCompletionSource<int>();
IAsyncResult waiting = pending.Task;
var handle = waiting.AsyncWaitHandle;
Console.WriteLine(waiting.IsCompleted + " " + handle.WaitOne(0));
pending.SetResult(1);
Console.WriteLine(waiting.IsCompleted + " " + handle.WaitOne(10000));
Console.WriteLine(ReferenceEquals(handle, waiting.AsyncWaitHandle));
Console.WriteLine(waiting is Task && (waiting as Task) == pending.Task);
object boxed = pending.Task;
Console.WriteLine(boxed is IAsyncResult);
Console.WriteLine(boxed is IDisposable);
Console.WriteLine(((object)"text") is IAsyncResult);

Console.WriteLine("-- Dispose");
Try("created", () => new Task(() => { }).Dispose());
Try("completed twice", () => { var t = Task.CompletedTask; t.Dispose(); t.Dispose(); });
Try("pending", () => new TaskCompletionSource().Task.Dispose());
Try("canceled", () => Task.FromCanceled(new CancellationToken(true)).Dispose());
Try("faulted", () => { var t = Task.FromException(new Exception("kept")); t.Dispose(); Console.WriteLine(t.Exception.InnerException.Message); });
Try("used after", () => { var t = Task.FromResult(4); t.Dispose(); Console.WriteLine(t.Result + " " + t.Status + " " + t.ContinueWith(x => x.Result + 1).Result); });
Try("started", () => { var g = new ManualResetEventSlim(); var t = Task.Run(() => g.Wait()); try { t.Dispose(); } finally { g.Set(); t.Wait(); } });
