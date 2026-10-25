// AsyncLocal<T>: values flow into awaits, Task.Run, threads, the thread pool and ExecutionContext.Run,
// changes inside an async method don't flow back to its caller, and they live beside the current cultures.
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

var user = new AsyncLocal<string>();
var depth = new AsyncLocal<int>();
Console.WriteLine($"initial: '{user.Value ?? "null"}' {depth.Value}");

user.Value = "alice";
depth.Value = 1;
await Inner("first");
Console.WriteLine($"after Inner: {user.Value} {depth.Value}");

string? fromRun = await Task.Run(() => user.Value);
Console.WriteLine($"Task.Run sees: {fromRun}");

string? fromThread = null;
var thread = new Thread(() => { fromThread = user.Value; user.Value = "thread-local change"; });
thread.Start();
thread.Join();
Console.WriteLine($"thread saw: {fromThread}, main still: {user.Value}");

using (var done = new ManualResetEventSlim())
{
    string? fromPool = null;
    ThreadPool.QueueUserWorkItem(_ => { fromPool = user.Value; done.Set(); });
    done.Wait();
    Console.WriteLine($"pool saw: {fromPool}");
}

var captured = ExecutionContext.Capture()!;
user.Value = "bob";
ExecutionContext.Run(captured, _ => Console.WriteLine($"in captured context: {user.Value}"), null);
Console.WriteLine($"back: {user.Value}");

// Cultures share the flow: setting one keeps the AsyncLocal values, and the other way round.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
user.Value = "carol";
Console.WriteLine($"culture '{CultureInfo.CurrentCulture.Name}' user {user.Value}");
await Task.Yield();
Console.WriteLine($"after yield: culture '{CultureInfo.CurrentCulture.Name}' user {user.Value} depth {depth.Value}");

user.Value = null;
depth.Value = 0;
Console.WriteLine($"cleared: '{user.Value ?? "null"}' {depth.Value}");


async Task Inner(string label)
{
    Console.WriteLine($"{label} enter: {user.Value} {depth.Value}");
    depth.Value = depth.Value + 1;
    user.Value = "changed in " + label;
    await Task.Delay(1);
    Console.WriteLine($"{label} after await: {user.Value} {depth.Value}");
    if (depth.Value < 3) await Inner("nested");
    Console.WriteLine($"{label} exit: {user.Value} {depth.Value}");
}
