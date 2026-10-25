// Task.Status of completed tasks (and of a pending TaskCompletionSource / async method) and
// Task.ConfigureAwait(ConfigureAwaitOptions), as Microsoft.Extensions.Primitives and hosting use them.
var pending = new TaskCompletionSource();
Console.WriteLine($"{Task.CompletedTask.Status} {Task.FromResult(1).Status} {pending.Task.Status}");
Console.WriteLine($"{Task.FromException(new InvalidOperationException()).Status} {Task.FromCanceled(new CancellationToken(true)).Status}");
var slow = Slow(pending.Task);
Console.WriteLine(slow.Status);
pending.SetResult();
await slow;
Console.WriteLine($"{pending.Task.Status} {slow.Status}");

var failing = Fail();
await failing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
Console.WriteLine($"suppressed: {failing.Status} {failing.Exception!.InnerException!.Message}");
await Task.FromCanceled(new CancellationToken(true)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
Console.WriteLine("canceled suppressed");
try { await Fail().ConfigureAwait(ConfigureAwaitOptions.None); }
catch (InvalidOperationException e) { Console.WriteLine("thrown: " + e.Message); }
try { await Fail().ConfigureAwait(ConfigureAwaitOptions.ForceYielding); }
catch (InvalidOperationException e) { Console.WriteLine("yielded then thrown: " + e.Message); }
await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);
Console.WriteLine("yielded");
try { Task.CompletedTask.ConfigureAwait((ConfigureAwaitOptions)64); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { _ = ((Task)null!).Status; }
catch (NullReferenceException) { Console.WriteLine("null task"); }

static async Task Slow(Task gate) => await gate;

static async Task Fail()
{
    await Task.Yield();
    throw new InvalidOperationException("boom");
}
