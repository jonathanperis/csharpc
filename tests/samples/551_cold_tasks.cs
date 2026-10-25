// new Task(...) / new Task<T>(...): created (cold) tasks, Start / Start(scheduler) / RunSynchronously, a
// token canceled before the start, the start errors, and Task.CurrentId inside and outside tasks.
using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine($"outside a task: CurrentId {(Task.CurrentId is null ? "null" : "set")}");

var cold = new Task(() => Console.WriteLine("cold task ran"));
Console.WriteLine($"created: {cold.Status}");
cold.Start();
cold.Wait();
Console.WriteLine($"after start: {cold.Status}");

var withState = new Task(state => Console.WriteLine($"state: {state}"), "payload");
withState.Start(TaskScheduler.Default);
withState.Wait();

var compute = new Task<int>(() => 6 * 7);
Console.WriteLine($"Task<int> created: {compute.Status}");
compute.Start();
Console.WriteLine($"result {compute.Result}");
var computeState = new Task<string>(s => ((string)s!).ToUpperInvariant(), "shout", TaskCreationOptions.LongRunning);
computeState.RunSynchronously();
Console.WriteLine($"ran synchronously: {computeState.Status} {computeState.Result}");

int ranOn = -1;
var inline = new Task(() => ranOn = Environment.CurrentManagedThreadId);
inline.RunSynchronously();
Console.WriteLine($"RunSynchronously on the calling thread: {ranOn == Environment.CurrentManagedThreadId}");
bool childDone = false;
var syncParent = new Task(() => Task.Factory.StartNew(() => { Thread.Sleep(50); childDone = true; }, TaskCreationOptions.AttachedToParent));
syncParent.RunSynchronously(); // returns once the attached child has completed too
Console.WriteLine($"RunSynchronously waits for attached children: {childDone} {syncParent.Status}");

var failing = new Task(() => throw new InvalidOperationException("boom"));
failing.RunSynchronously();
Console.WriteLine($"failing: {failing.Status} {failing.Exception?.InnerException?.Message}");

using (var cts = new CancellationTokenSource())
{
    var cancelable = new Task(() => Console.WriteLine("never runs"), cts.Token);
    Console.WriteLine($"cancelable: {cancelable.Status}");
    cts.Cancel();
    Console.WriteLine($"after cancel: {cancelable.Status}");
    try { cancelable.Start(); }
    catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
}
var preCanceled = new Task(() => { }, new CancellationToken(true));
Console.WriteLine($"created with a canceled token: {preCanceled.Status}");

try { cold.Start(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
var running = Task.Run(() => Thread.Sleep(50));
try { running.Start(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
running.Wait();
try { new TaskCompletionSource().Task.Start(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { new TaskCompletionSource<int>().Task.RunSynchronously(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { new Task((Action)null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

// CurrentId: set inside a task (the same id as the task's), null in a plain thread-pool work item.
var probe = new Task<int?>(() => Task.CurrentId);
probe.Start();
Console.WriteLine($"CurrentId inside matches Id: {probe.Result == probe.Id}");
var run = Task.Run(() => Task.CurrentId);
Console.WriteLine($"Task.Run CurrentId matches: {run.Result == run.Id}");
using (var done = new ManualResetEventSlim())
{
    int? inPool = 0;
    ThreadPool.QueueUserWorkItem(_ => { inPool = Task.CurrentId; done.Set(); });
    done.Wait();
    Console.WriteLine($"thread-pool item: {(inPool is null ? "null" : "set")}");
}
