// PLINQ (System.Linq.Parallel compiled from IL: its operators override generic virtual methods in generic
// classes, its tasks attach to a root task) and the task features it relies on: attached child tasks,
// TaskCompletionSource.SetException with several exceptions, and Task as IDisposable.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

var numbers = Enumerable.Range(1, 200).ToArray();
Console.WriteLine($"ordered: {string.Join(",", numbers.AsParallel().AsOrdered().Where(n => n % 17 == 0).Select(n => n * 2))}");
Console.WriteLine($"sum {numbers.AsParallel().Sum()}, max {numbers.AsParallel().Max()}, count {numbers.AsParallel().Count(n => n % 3 == 0)}");
Console.WriteLine($"ordered take: {string.Join(",", numbers.AsParallel().AsOrdered().Skip(5).Take(5))}");
Console.WriteLine($"sorted: {string.Join(",", new[] { 5, 3, 9, 1, 7 }.AsParallel().WithDegreeOfParallelism(4).OrderBy(x => x).Select(x => x * 10))}");
var words = new[] { "pear", "fig", "apple", "kiwi", "plum", "date" };
Console.WriteLine($"grouped: {string.Join(" ", words.AsParallel().GroupBy(w => w.Length).OrderBy(g => g.Key).Select(g => $"{g.Key}:{string.Join("/", g.OrderBy(w => w))}"))}");
Console.WriteLine($"select many: {string.Join("", words.AsParallel().AsOrdered().SelectMany(w => w.Take(1)))}");
var bag = new ConcurrentBag<int>();
numbers.AsParallel().ForAll(n => { if (n % 50 == 0) bag.Add(n); });
Console.WriteLine($"ForAll: {string.Join(",", bag.OrderBy(n => n))}");
Console.WriteLine($"with degree: {numbers.AsParallel().WithDegreeOfParallelism(2).Aggregate(0L, (acc, n) => acc + n, (a, b) => a + b, r => r)}");
try { numbers.AsParallel().Select(n => n == 100 ? throw new InvalidOperationException("bad element") : n).ToArray(); }
catch (AggregateException e) { Console.WriteLine($"{e.GetType().Name}: {string.Join(", ", e.InnerExceptions.Select(x => x.Message))}"); }
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    try { numbers.AsParallel().WithCancellation(cts.Token).Sum(); }
    catch (OperationCanceledException e) { Console.WriteLine($"canceled: {e.GetType().Name}"); }
}

// Attached children: the parent completes after them, and their failures fault it.
var log = new ConcurrentQueue<string>();
var parent = Task.Factory.StartNew(() =>
{
    Task.Factory.StartNew(() => { Thread.Sleep(30); log.Enqueue("child done"); }, TaskCreationOptions.AttachedToParent);
    Task.Factory.StartNew(() => log.Enqueue("detached child"));
    log.Enqueue("parent body done");
});
parent.Wait();
Console.WriteLine($"parent {parent.Status}, child finished first: {log.Contains("child done")}");
var failingParent = Task.Factory.StartNew(() =>
{
    Task.Factory.StartNew(() => throw new FormatException("child failed"), TaskCreationOptions.AttachedToParent);
    var cold = new Task(() => throw new TimeoutException("cold child failed"), TaskCreationOptions.AttachedToParent);
    cold.Start();
});
try { failingParent.Wait(); }
catch (AggregateException e)
{
    Console.WriteLine($"failing parent {failingParent.Status}: {e.InnerExceptions.Count} child aggregate(s)");
    Console.WriteLine($"flattened: {string.Join(", ", e.Flatten().InnerExceptions.Select(x => x.GetType().Name).OrderBy(n => n))}");
}
var denied = Task.Factory.StartNew(() =>
{
    Task.Factory.StartNew(() => Thread.Sleep(30), TaskCreationOptions.AttachedToParent);
}, TaskCreationOptions.DenyChildAttach);
denied.Wait();
Console.WriteLine($"DenyChildAttach parent: {denied.Status}");

// TaskCompletionSource with several exceptions; Task as IDisposable.
var tcs = new TaskCompletionSource<int>();
tcs.SetException(new Exception[] { new ArgumentException("first"), new InvalidOperationException("second") });
Console.WriteLine($"tcs: {tcs.Task.Status} {tcs.Task.Exception!.InnerExceptions.Count} [{string.Join(", ", tcs.Task.Exception.InnerExceptions.Select(x => x.Message))}]");
try { await tcs.Task; }
catch (Exception e) { Console.WriteLine($"await rethrows the first: {e.GetType().Name} {e.Message}"); }
Console.WriteLine($"TrySetException again: {tcs.TrySetException(new[] { new Exception("late") })}");
try { new TaskCompletionSource().SetException(Array.Empty<Exception>()); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
using (Task done = Task.CompletedTask)
    Console.WriteLine($"IDisposable task: {done is IDisposable}");
