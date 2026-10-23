using IlAsyncLib;

Console.WriteLine(Work.DoubleAsync(21).Result);
Console.WriteLine(await Work.DoubleAsync(5));

var sync = Work.SyncAsync(4);
Console.WriteLine($"sync: completed={sync.IsCompleted} result={sync.Result}");

var log = new List<string>();
await Work.NoResultAsync(log);
Console.WriteLine(string.Join(" | ", log));

// Exceptions thrown before the first await are stored in the task, not thrown by the call.
var early = Work.ThrowBeforeAwaitAsync(-1);
Console.WriteLine($"early: faulted={early.IsFaulted}");
try { await early; }
catch (ArgumentOutOfRangeException e) { Console.WriteLine($"caught {e.GetType().Name}: {e.Message}"); }

try { await Work.ThrowAfterAwaitAsync("late"); }
catch (InvalidOperationException e) { Console.WriteLine($"caught {e.GetType().Name}: {e.Message}"); }

try { _ = Work.ThrowAfterAwaitAsync("via Result").Result; }
catch (AggregateException e) { Console.WriteLine($"aggregate: {e.InnerException!.GetType().Name}: {e.InnerException.Message}"); }

Console.WriteLine(await Work.TryCatchFinallyAsync(new List<string>()));
Console.WriteLine(await Work.ConfigureAwaitAsync(10));
Console.WriteLine(await Work.ValueTaskAsync(4));
Console.WriteLine($"valuetask sync: {Work.ValueTaskAsync(0).IsCompletedSuccessfully}");
var vlog = new List<string>();
await Work.ValueTaskNoResultAsync(vlog);
Console.WriteLine(vlog[0]);

Console.WriteLine(await Work.EchoAsync("echo"));
Console.WriteLine(await Work.EchoAsync(3.5));
Console.WriteLine(await Work.EchoAsync((1, "tuple")));
Console.WriteLine(await Work.FibAsync(15));
Console.WriteLine(await Work.LoopAsync(1000));
Console.WriteLine(string.Join(",", await Work.WhenAllAsync()));
Console.WriteLine(await Work.RunAsync());
Console.WriteLine(await Work.LambdaAsync(6));
Console.WriteLine(await Work.CallbackAsync(async () => { await Task.Delay(1); return 7; }));
Console.WriteLine(await Work.CustomAwaitableAsync());

var counter = new Counter("c");
await counter.AddAsync(2);
await counter.AddAsync(3);
Console.WriteLine(await counter.DescribeAsync());

var done = new TaskCompletionSource<string>();
Work.FireAndForget(done, "async void");
Console.WriteLine(await done.Task);

Console.WriteLine(await Streams.SumAsync(Streams.RangeAsync(10)));
var slog = new List<string>();
Console.WriteLine(await Streams.FirstTwoAsync(Streams.WordsAsync(slog)));
Console.WriteLine(string.Join(",", slog));
slog.Clear();
await foreach (var w in Streams.WordsAsync(slog)) Console.Write(w + " ");
Console.WriteLine(string.Join(",", slog));
var seen = 0;
try { await foreach (var x in Streams.FailingAsync()) seen += x; }
catch (InvalidOperationException e) { Console.WriteLine($"stream: {e.Message} after {seen}"); }
await using (var e = Streams.RangeAsync(3).GetAsyncEnumerator())
{
    while (await e.MoveNextAsync()) Console.Write(e.Current);
    Console.WriteLine();
}
using (var ctsRange = new CancellationTokenSource())
{
    var got = 0;
    try
    {
        await foreach (var x in Streams.RangeAsync(100).WithCancellation(ctsRange.Token))
        {
            got++;
            if (x == 2) ctsRange.Cancel();
        }
    }
    catch (OperationCanceledException) { Console.WriteLine($"range canceled after {got}"); }
}

using var cts = new CancellationTokenSource();
cts.Cancel();
var canceled = Work.CanceledAsync(cts.Token);
try { await canceled; }
catch (OperationCanceledException e) { Console.WriteLine($"canceled: {e.GetType().Name} {canceled.IsCanceled} {canceled.IsFaulted}"); }
