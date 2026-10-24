// Task.Factory.StartNew, TaskFactory / TaskFactory<T>, TaskScheduler (Default, Current, custom
// schedulers through QueueTask / TryExecuteTask) and ContinueWith with its options.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

static string Outcome(Task t)
{
    try { t.Wait(); return "ran"; }
    catch (AggregateException e) { return $"{(t.IsCanceled ? "canceled" : "faulted")}: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"; }
}

Console.WriteLine($"{TaskScheduler.Default == TaskScheduler.Current} {TaskScheduler.Default.MaximumConcurrencyLevel == int.MaxValue} {TaskScheduler.Default.Id == TaskScheduler.Default.Id && TaskScheduler.Default.Id > 0}");

// StartNew on the default scheduler.
Task.Factory.StartNew(() => Console.WriteLine($"action, current is default: {TaskScheduler.Current == TaskScheduler.Default}")).Wait();
Console.WriteLine(Task.Factory.StartNew(() => 6 * 7).Result);
Task.Factory.StartNew(s => Console.WriteLine("state " + s), "S1").Wait();
Console.WriteLine(Task.Factory.StartNew(s => (int)s! + 1, 41).Result);
Console.WriteLine(Task.Factory.StartNew(() => "long", CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Result);
Console.WriteLine(Task<int>.Factory.StartNew(() => 5).Result + new TaskFactory<string>().StartNew(() => "x").Result);
var factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, TaskScheduler.Default);
Console.WriteLine($"{factory.CreationOptions} {factory.ContinuationOptions} {factory.Scheduler == TaskScheduler.Default} {Task.Factory.Scheduler == null} {factory.StartNew(() => 1).Result}");

// Cancellation and failures.
var canceled = new CancellationTokenSource();
canceled.Cancel();
var neverRuns = Task.Factory.StartNew(() => Console.WriteLine("never"), canceled.Token);
Console.WriteLine(Outcome(neverRuns));
Console.WriteLine(Outcome(Task.Factory.StartNew(() => throw new InvalidOperationException("boom"))));
var cts = new CancellationTokenSource();
Console.WriteLine(Outcome(Task.Factory.StartNew(() => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); }, cts.Token)));
Console.WriteLine(Outcome(Task.Factory.StartNew(() => throw new OperationCanceledException())));
try { Task.Factory.StartNew(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { Task.Factory.StartNew(() => { }, (TaskCreationOptions)0x1000); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { TaskScheduler.FromCurrentSynchronizationContext(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

// A custom scheduler that runs tasks inline: Current is that scheduler inside, and StartNew without a
// scheduler there uses it too.
var inline = new InlineScheduler();
var nested = Task.Factory.StartNew(() =>
{
    Console.WriteLine($"inline: current is custom {TaskScheduler.Current == inline}");
    return Task.Factory.StartNew(() => TaskScheduler.Current == inline).Result;
}, CancellationToken.None, TaskCreationOptions.None, inline);
Console.WriteLine($"nested on custom: {nested.Result}, queued {inline.Queued}, id {inline.Id > TaskScheduler.Default.Id}");
var hidden = Task.Factory.StartNew(() => TaskScheduler.Current == TaskScheduler.Default, CancellationToken.None, TaskCreationOptions.HideScheduler, inline);
Console.WriteLine($"HideScheduler: {hidden.Result}");

// A scheduler that queues work and runs it later, in its own order.
var deferred = new DeferredScheduler();
var order = new List<string>();
var a = Task.Factory.StartNew(() => order.Add("a"), CancellationToken.None, TaskCreationOptions.None, deferred);
var b = Task.Factory.StartNew(() => { order.Add("b"); return 2; }, CancellationToken.None, TaskCreationOptions.None, deferred);
Console.WriteLine($"{a.IsCompleted} {b.IsCompleted} {deferred.Count}");
deferred.RunAll(reverse: true);
Console.WriteLine($"{string.Join(",", order)} {a.IsCompleted} {b.Result} {deferred.RunAgain()}");

// ContinueWith.
var first = Task.Factory.StartNew(() => 10);
Console.WriteLine(first.ContinueWith(t => t.Result * 2).Result);
Console.WriteLine(first.ContinueWith((t, s) => t.Result + (int)s!, 5).Result);
first.ContinueWith(t => Console.WriteLine("action continuation " + t.Result)).Wait();
first.ContinueWith((t, s) => Console.WriteLine("state continuation " + s), "st").Wait();
Task plain = Task.Factory.StartNew(() => { });
Console.WriteLine(plain.ContinueWith(t => t.IsCompletedSuccessfully ? "ok" : "no").Result);
Console.WriteLine(plain.ContinueWith((t, s) => s + "!", "state").Result);
var failing = Task.Factory.StartNew(() => throw new FormatException("bad format"));
Console.WriteLine(failing.ContinueWith(t => $"{t.IsFaulted} {t.Exception!.InnerException!.Message}").Result);
Console.WriteLine(Outcome(failing.ContinueWith(_ => Console.WriteLine("never"), TaskContinuationOptions.OnlyOnRanToCompletion)));
Console.WriteLine(Outcome(first.ContinueWith(_ => Console.WriteLine("never"), TaskContinuationOptions.OnlyOnFaulted)));
Console.WriteLine(failing.ContinueWith(_ => "on faulted", TaskContinuationOptions.OnlyOnFaulted).Result);
Console.WriteLine(Outcome(neverRuns.ContinueWith(_ => { }, TaskContinuationOptions.NotOnCanceled)));
Console.WriteLine(neverRuns.ContinueWith(t => "after cancel " + t.IsCanceled, TaskContinuationOptions.OnlyOnCanceled).Result);
Console.WriteLine(Outcome(first.ContinueWith(_ => { }, canceled.Token)));
Console.WriteLine(Outcome(first.ContinueWith(_ => throw new InvalidOperationException("in continuation"))));
var gate = new TaskCompletionSource();
var early = new CancellationTokenSource();
var pending = gate.Task.ContinueWith(_ => Console.WriteLine("never"), early.Token);
early.Cancel();
Console.WriteLine($"canceled before its antecedent: {pending.IsCanceled} {gate.Task.IsCompleted}");
var lazy = gate.Task.ContinueWith(_ => { }, early.Token, TaskContinuationOptions.LazyCancellation, TaskScheduler.Default);
Console.WriteLine($"lazy waits: {lazy.IsCompleted}");
gate.SetResult();
Console.WriteLine($"lazy after: {Outcome(lazy)}");
first.ContinueWith(_ => Console.WriteLine($"continuation on custom: {TaskScheduler.Current == inline}"), CancellationToken.None, TaskContinuationOptions.None, inline).Wait();
try { first.ContinueWith(_ => { }, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.NotOnFaulted | TaskContinuationOptions.NotOnCanceled); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { first.ContinueWith(_ => { }, TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { first.ContinueWith((Action<Task<int>>)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { first.ContinueWith(_ => 1, (TaskScheduler)null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

sealed class InlineScheduler : TaskScheduler
{
    public int Queued;
    protected override void QueueTask(Task task) { Queued++; TryExecuteTask(task); }
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
    protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
}

sealed class DeferredScheduler : TaskScheduler
{
    private readonly List<Task> _tasks = new();
    private Task? _last;
    public int Count => _tasks.Count;
    protected override void QueueTask(Task task) => _tasks.Add(task);
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;
    public void RunAll(bool reverse)
    {
        var tasks = new List<Task>(_tasks);
        if (reverse) tasks.Reverse();
        _tasks.Clear();
        foreach (var t in tasks) { TryExecuteTask(t); _last = t; }
    }
    public bool RunAgain() => TryExecuteTask(_last!);
}
