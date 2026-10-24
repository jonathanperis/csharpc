// System.Threading.Timer, TimeProvider.System and CancellationTokenSource.CancelAfter(TimeSpan), as
// Kestrel's heartbeat and Microsoft.Extensions.Hosting's shutdown timeout use them.
var once = new TaskCompletionSource<object?>();
using (var timer = new Timer(state => once.TrySetResult(state), "state", 10, Timeout.Infinite))
    Console.WriteLine("one-shot: " + await once.Task);

var ticks = 0;
var three = new TaskCompletionSource();
var periodic = new Timer(_ => { if (Interlocked.Increment(ref ticks) == 3) three.TrySetResult(); }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
await three.Task;
Console.WriteLine(periodic.Change(Timeout.Infinite, Timeout.Infinite) + " " + (Volatile.Read(ref ticks) >= 3));
periodic.Dispose();
periodic.Dispose();
try { periodic.Change(1, 1); } catch (ObjectDisposedException) { Console.WriteLine("disposed"); }

var changed = new TaskCompletionSource();
var later = new Timer(_ => changed.TrySetResult());
Console.WriteLine(later.Change(TimeSpan.FromMilliseconds(5), Timeout.InfiniteTimeSpan));
await changed.Task;
await later.DisposeAsync();
Console.WriteLine("changed fired");

try { new Timer(_ => { }, null, -2, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { new Timer(null!, null, 0, 0); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

var provider = TimeProvider.System;
long start = provider.GetTimestamp();
await Task.Delay(20);
var elapsed = provider.GetElapsedTime(start);
Console.WriteLine($"{ReferenceEquals(provider, TimeProvider.System)} {provider.TimestampFrequency > 0} {elapsed >= TimeSpan.FromMilliseconds(15)} {elapsed < TimeSpan.FromSeconds(30)}");
Console.WriteLine(provider.GetElapsedTime(0, provider.TimestampFrequency));
var fromProvider = new TaskCompletionSource<object?>();
using (var providerTimer = provider.CreateTimer(s => fromProvider.TrySetResult(s), 42, TimeSpan.FromMilliseconds(5), Timeout.InfiniteTimeSpan))
    Console.WriteLine("provider timer: " + await fromProvider.Task);

using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMilliseconds(10));
try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (TaskCanceledException) { Console.WriteLine("canceled after delay"); }
using var never = new CancellationTokenSource();
never.CancelAfter(Timeout.InfiniteTimeSpan);
Console.WriteLine(never.IsCancellationRequested);
try { never.CancelAfter(TimeSpan.FromMilliseconds(-2)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
