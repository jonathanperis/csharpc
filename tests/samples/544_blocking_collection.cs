// BlockingCollection<T> (ported from .NET) over ConcurrentQueue/Stack/Bag, and SemaphoreSlim /
// ManualResetEventSlim waits that a CancellationToken ends.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

var queue = new BlockingCollection<(int Id, string Name)>();
var producer = new Thread(() =>
{
    for (int i = 0; i < 5; i++) queue.Add((i, "item" + i));
    queue.CompleteAdding();
});
producer.Start();
foreach (var item in queue.GetConsumingEnumerable()) Console.WriteLine($"consumed {item.Id} {item.Name}");
producer.Join();
Console.WriteLine($"completed {queue.IsCompleted}, adding completed {queue.IsAddingCompleted}, count {queue.Count}");
try { queue.Add((9, "late")); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { queue.Take(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

var bounded = new BlockingCollection<int>(new ConcurrentStack<int>(), boundedCapacity: 2);
Console.WriteLine($"bounded {bounded.BoundedCapacity}: {bounded.TryAdd(1)} {bounded.TryAdd(2)} {bounded.TryAdd(3)} {bounded.TryAdd(3, 20)}");
Console.WriteLine($"stack order: {string.Join(",", bounded.ToArray())}, take {bounded.Take()}");
Console.WriteLine($"TryTake: {bounded.TryTake(out int x)} {x}, again {bounded.TryTake(out int y, 10)} {y}");
var copy = new int[3];
bounded.Add(7);
bounded.CopyTo(copy, 1);
Console.WriteLine($"copy {string.Join(",", copy)}, enumerate {string.Join(",", bounded)}");

// A producer blocked on a full collection resumes when a consumer takes.
var full = new BlockingCollection<int>(1);
full.Add(1);
var blocked = Task.Run(() => { full.Add(2); return "added after take"; });
Thread.Sleep(20);
Console.WriteLine($"blocked producer done early: {blocked.IsCompleted}");
Console.WriteLine($"took {full.Take()}, {blocked.Result}, then {full.Take()}");

// Cancellation ends a blocked Take.
using (var cts = new CancellationTokenSource())
{
    var empty = new BlockingCollection<string>();
    var taker = Task.Run(() =>
    {
        try { return empty.Take(cts.Token); }
        catch (OperationCanceledException e) { return $"{e.GetType().Name}: {e.Message} (token matches: {e.CancellationToken == cts.Token})"; }
    });
    Thread.Sleep(20);
    cts.Cancel();
    Console.WriteLine(taker.Result);
}

var bag = new BlockingCollection<int>(new ConcurrentBag<int>());
bag.Add(3);
Console.WriteLine($"bag take {bag.Take()}, count {bag.Count}");
bag.Dispose();
try { bag.Add(1); }
catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name); }

// SemaphoreSlim / ManualResetEventSlim with a token.
var semaphore = new SemaphoreSlim(0);
Console.WriteLine($"semaphore timeout: {semaphore.Wait(10, CancellationToken.None)}");
semaphore.Release();
Console.WriteLine($"semaphore available: {semaphore.Wait(TimeSpan.FromMilliseconds(10))}");
using (var cts = new CancellationTokenSource())
{
    var waiter = Task.Run(() =>
    {
        try { semaphore.Wait(cts.Token); return "acquired"; }
        catch (OperationCanceledException) { return "canceled"; }
    });
    Thread.Sleep(20);
    cts.Cancel();
    Console.WriteLine($"semaphore wait: {waiter.Result}, count {semaphore.CurrentCount}");
    try { semaphore.Wait(0, cts.Token); }
    catch (OperationCanceledException e) { Console.WriteLine($"already canceled: {e.Message}"); }
}
try { semaphore.Wait(-2, CancellationToken.None); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message.Split(Environment.NewLine)[0]); }

var gate = new ManualResetEventSlim(false);
using (var cts = new CancellationTokenSource(30))
{
    try { gate.Wait(cts.Token); }
    catch (OperationCanceledException) { Console.WriteLine("event wait canceled by timeout"); }
}
gate.Set();
Console.WriteLine($"event set: {gate.Wait(TimeSpan.Zero, CancellationToken.None)}");

// Many producers and consumers: every item is taken exactly once.
var shared = new BlockingCollection<int>(boundedCapacity: 4);
var producers = Enumerable.Range(0, 3).Select(p => Task.Run(() => { for (int i = 0; i < 100; i++) shared.Add(p * 1000 + i); })).ToArray();
var consumers = Enumerable.Range(0, 2).Select(_ => Task.Run(() => shared.GetConsumingEnumerable().Sum(v => (long)v))).ToArray();
Task.WaitAll(producers);
shared.CompleteAdding();
Console.WriteLine($"sum {consumers.Sum(c => c.Result)}");

// OperationCanceledException.CancellationToken
using (var source = new CancellationTokenSource())
{
    source.Cancel();
    try { source.Token.ThrowIfCancellationRequested(); }
    catch (OperationCanceledException e) { Console.WriteLine($"ThrowIfCancellationRequested token: {e.CancellationToken == source.Token}"); }
    Console.WriteLine($"ctor token: {new OperationCanceledException("m", source.Token).CancellationToken == source.Token}, " +
        $"none: {new OperationCanceledException().CancellationToken == CancellationToken.None}, " +
        $"task canceled: {new TaskCanceledException("t", null, source.Token).CancellationToken.IsCancellationRequested}");
}
