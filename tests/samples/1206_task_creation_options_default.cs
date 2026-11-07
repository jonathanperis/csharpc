// Task.CreationOptions and AsyncState of tasks created without options of their own (#118): a completed task, a
// TaskCompletionSource's, Task.Run's (DenyChildAttach) and a delegate task. The default ones read the runtime's flag
// word instead of the per-task table; on WebAssembly that read trapped.
using System;
using System.Threading.Tasks;

var u = Task.FromResult(1);
Console.WriteLine($"{u.AsyncState == null} {u.CreationOptions}");
Console.WriteLine(Task.CompletedTask.CreationOptions);
Console.WriteLine(Task.FromCanceled(new System.Threading.CancellationToken(true)).CreationOptions);
Console.WriteLine(Task.FromException(new InvalidOperationException()).CreationOptions);

var tcs = new TaskCompletionSource<int>();
Console.WriteLine(tcs.Task.CreationOptions);
tcs.SetResult(2);
Console.WriteLine(tcs.Task.CreationOptions);

var run = Task.Run(() => 3);
run.Wait();
Console.WriteLine($"{run.Result} {run.CreationOptions}");

async Task<int> Async() { await Task.Yield(); return 4; }
var a = Async();
a.Wait();
Console.WriteLine($"{a.Result} {a.CreationOptions}");

var delay = Task.Delay(1);
delay.Wait();
Console.WriteLine(delay.CreationOptions);

var started = Task.Factory.StartNew(() => 5);
started.Wait();
Console.WriteLine($"{started.Result} {started.CreationOptions}");

var opts = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
Console.WriteLine(opts.Task.CreationOptions);
