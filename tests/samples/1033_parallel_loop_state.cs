// ParallelLoopState and the Parallel loops that take it: Stop and Break (which iterations still run, the result's
// IsCompleted and LowestBreakIteration, the checks for Break after Stop and the reverse), IsStopped /
// IsExceptional / ShouldExitCurrentIteration, iteration indexes, thread-local state, ParallelOptions, loops over
// arrays, lists, enumerables and partitioners, how body exceptions and cancellation surface, Parallel.Invoke
// with options and its exceptions, and ForEachAsync / ForAsync. One worker where the order matters.
#pragma warning disable CS8600, CS8602, CS8604, CS8625, CS8605
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}" + (e is AggregateException ag ? " [" + string.Join("; ", ag.InnerExceptions.Select(x => x.GetType().Name + ":" + x.Message)) + "]" : "")); }
}
static string R(ParallelLoopResult r) => $"completed={r.IsCompleted} lowest={(r.LowestBreakIteration.HasValue ? r.LowestBreakIteration.ToString() : "null")}";

var opts1 = new ParallelOptions { MaxDegreeOfParallelism = 1 };
Console.WriteLine(R(Parallel.For(0, 10, opts1, (i, s) => { if (i == 4) s.Break(); })));
Console.WriteLine(R(Parallel.For(0, 10, opts1, (i, s) => { if (i == 4) s.Stop(); })));
Console.WriteLine(R(Parallel.For(0, 10, opts1, (i, s) => { })));
Console.WriteLine(R(Parallel.For(0L, 10L, opts1, (i, s) => { if (i == 7) s.Break(); })));
var seen = new List<int>();
Parallel.For(0, 10, opts1, (i, s) => { seen.Add(i); if (i == 3) s.Break(); });
Console.WriteLine(string.Join(",", seen));
seen.Clear();
Parallel.For(0, 10, opts1, (i, s) => { seen.Add(i); if (i == 3) s.Stop(); });
Console.WriteLine(string.Join(",", seen));
seen.Clear();
Parallel.For(0, 10, opts1, (i, s) => { seen.Add(i); if (i == 3) { s.Break(); } if (i == 5) s.Stop(); });
Console.WriteLine(string.Join(",", seen));
Try("break after stop", () => Parallel.For(0, 3, opts1, (i, s) => { s.Stop(); s.Break(); }));
Try("stop after break", () => Parallel.For(0, 3, opts1, (i, s) => { s.Break(); s.Stop(); }));
Try("break twice", () => Console.WriteLine(R(Parallel.For(0, 5, opts1, (i, s) => { s.Break(); s.Break(); }))));
Try("state props", () => Parallel.For(0, 1, opts1, (i, s) => Console.WriteLine($"{s.IsStopped} {s.IsExceptional} {s.ShouldExitCurrentIteration} {(s.LowestBreakIteration.HasValue ? "v" : "null")}")));
Try("state props after break", () => Parallel.For(0, 2, opts1, (i, s) => { if (i == 0) { s.Break(); Console.WriteLine($"{s.IsStopped} {s.IsExceptional} {s.ShouldExitCurrentIteration} {s.LowestBreakIteration}"); } }));
Try("state props after stop", () => Parallel.For(0, 2, opts1, (i, s) => { if (i == 0) { s.Stop(); Console.WriteLine($"{s.IsStopped} {s.IsExceptional} {s.ShouldExitCurrentIteration} {s.LowestBreakIteration}"); } }));
Try("state after exception", () =>
{
    bool seenExceptional = false;
    try { Parallel.For(0, 4, opts1, (i, s) => { if (i == 1) throw new InvalidOperationException("x"); if (i > 1) seenExceptional = s.IsExceptional; }); }
    finally { Console.WriteLine("exceptional seen: " + seenExceptional); }
});
Try("exception", () => Parallel.For(0, 5, opts1, (i, s) => { if (i == 2) throw new ArgumentException("b" + i); }));
Try("local init", () =>
{
    long total = 0;
    var r = Parallel.For(0, 10, opts1, () => 0L, (i, s, local) => { if (i == 6) s.Break(); return local + i; }, local => Interlocked.Add(ref total, local));
    Console.WriteLine(total + " " + R(r));
});
Try("local init no opts", () =>
{
    long total = 0;
    var r = Parallel.For(1, 5, () => 0L, (i, s, local) => local + i, local => Interlocked.Add(ref total, local));
    Console.WriteLine(total + " " + R(r));
});
Try("local init long", () =>
{
    long total = 0;
    var r = Parallel.For<long>(1L, 5L, () => 0L, (i, s, local) => local + i, local => Interlocked.Add(ref total, local));
    Console.WriteLine(total + " " + R(r));
});
Try("ForEach array state", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(new[] { 1, 2, 3, 4, 5, 6 }, opts1, (x, s) => { seen.Add(x); if (x == 3) s.Break(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach list index", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(new List<int> { 10, 20, 30, 40 }, opts1, (x, s, i) => { seen.Add((int)i); if (i == 1) s.Stop(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach enumerable break", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(Enumerable.Range(0, 8).ToList(), opts1, (x, s, i) => { seen.Add((int)i); if (i == 2) s.Break(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach enumerable plain state", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(Enumerable.Range(0, 8).ToList(), opts1, (x, s) => { seen.Add(x); if (x == 2) s.Break(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach enumerable local", () =>
{
    long total = 0;
    var r = Parallel.ForEach(Enumerable.Range(0, 8), opts1, () => 0L, (x, s, local) => local + x, local => Interlocked.Add(ref total, local));
    Console.WriteLine(total + " " + R(r));
});
Try("ForEach enumerable local index", () =>
{
    long total = 0;
    var r = Parallel.ForEach(Enumerable.Range(0, 8), opts1, () => 0L, (x, s, i, local) => local + i, local => Interlocked.Add(ref total, local));
    Console.WriteLine(total + " " + R(r));
});
Try("ForEach orderable", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(Partitioner.Create(new List<int> { 1, 2, 3, 4, 5 }, true), opts1, (x, s, i) => { seen.Add((int)i); if (i == 2) s.Break(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach partitioner state", () => { seen.Clear(); Console.WriteLine(R(Parallel.ForEach(Partitioner.Create(new List<int> { 1, 2, 3, 4, 5 }, true), opts1, (x, s) => { seen.Add(x); if (x == 2) s.Stop(); }))); Console.WriteLine(string.Join(",", seen)); });
Try("ForEach partitioner state break", () => { Parallel.ForEach(Partitioner.Create(Enumerable.Range(0, 5)), opts1, (x, s) => { s.Break(); }); });
Try("ForEach partitioner break (non orderable)", () => { Parallel.ForEach(new NonOrderable(), opts1, (x, s) => { s.Break(); }); });
Try("ForEach partitioner lowest (non orderable)", () => { Parallel.ForEach(new NonOrderable(), opts1, (x, s) => { Console.WriteLine(s.LowestBreakIteration); }); });
Try("ForEach partitioner stop (non orderable)", () => { Parallel.ForEach(new NonOrderable(), opts1, (x, s) => { s.Stop(); }); });
Try("ForEach orderable not normalized", () => Parallel.ForEach(new NotNormalized(), opts1, (x, s, i) => { }));
Try("ForEach orderable not normalized plain", () => Parallel.ForEach(new NotNormalized(), opts1, x => { }));
Try("ForEach null body", () => Parallel.ForEach(new[] { 1 }, (Action<int, ParallelLoopState>)null));
Try("ForEach null source", () => Parallel.ForEach((IEnumerable<int>)null, (x, s) => { }));
Try("For null body", () => Parallel.For(0, 1, (Action<int, ParallelLoopState>)null));
Try("For null opts", () => Parallel.For(0, 1, null, (i, s) => { }));
Try("For local null init", () => Parallel.For(0, 1, (Func<int>)null, (i, s, l) => l, l => { }));
Try("For local null finally", () => Parallel.For(0, 1, () => 0, (i, s, l) => l, null));
Try("canceled before", () => { var c = new CancellationTokenSource(); c.Cancel(); Parallel.For(0, 5, new ParallelOptions { CancellationToken = c.Token, MaxDegreeOfParallelism = 1 }, (i, s) => { }); });
Try("canceled during", () => { var c = new CancellationTokenSource(); var n = 0; Parallel.For(0, 50, new ParallelOptions { CancellationToken = c.Token, MaxDegreeOfParallelism = 1 }, (i, s) => { n++; if (i == 3) c.Cancel(); }); });
Try("canceled during shouldexit", () => { var c = new CancellationTokenSource(); var any = false; try { Parallel.For(0, 50, new ParallelOptions { CancellationToken = c.Token, MaxDegreeOfParallelism = 1 }, (i, s) => { if (i == 3) c.Cancel(); if (i > 3) any |= s.ShouldExitCurrentIteration; }); } catch (OperationCanceledException) { Console.WriteLine("shouldexit " + any); throw; } });
Try("ForEach empty", () => Console.WriteLine(R(Parallel.ForEach(new int[0], opts1, (x, s) => { }))));
Try("For empty", () => Console.WriteLine(R(Parallel.For(5, 5, opts1, (i, s) => { }))));
Try("For reversed", () => Console.WriteLine(R(Parallel.For(9, 5, opts1, (i, s) => { }))));
Try("ForEach with many workers", () =>
{
    var count = 0;
    var r = Parallel.ForEach(Enumerable.Range(0, 1000), (x, s) => Interlocked.Increment(ref count));
    Console.WriteLine(count + " " + R(r));
});
Try("For many workers break", () =>
{
    var ran = new ConcurrentBag<int>();
    var r = Parallel.For(0, 200, (i, s) => { ran.Add(i); if (i == 50) s.Break(); });
    Console.WriteLine(R(r).StartsWith("completed=False") + " " + (r.LowestBreakIteration <= 50) + " " + Enumerable.Range(0, (int)r.LowestBreakIteration.Value).All(x => ran.Contains(x)));
});
Try("For many workers stop", () =>
{
    var r = Parallel.For(0, 200, (i, s) => { if (i == 10) s.Stop(); });
    Console.WriteLine(R(r));
});
Try("Invoke options", () => { var n = 0; Parallel.Invoke(new ParallelOptions { MaxDegreeOfParallelism = 1 }, () => n++, () => n++); Console.WriteLine(n); });
Try("Invoke empty", () => Parallel.Invoke());
Try("Invoke null", () => Parallel.Invoke((Action[])null));
Try("Invoke null element", () => Parallel.Invoke(() => { }, null));
Try("Invoke throws", () => Parallel.Invoke(() => throw new InvalidOperationException("a"), () => { }));
Try("ForEachAsync", () =>
{
    var sum = 0;
    Parallel.ForEachAsync(Enumerable.Range(1, 10), new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (x, ct) => { await Task.Yield(); Interlocked.Add(ref sum, x); }).Wait();
    Console.WriteLine(sum);
});
Try("ForEachAsync plain", () =>
{
    var sum = 0;
    Parallel.ForEachAsync(new List<int> { 1, 2, 3 }, (x, ct) => { Interlocked.Add(ref sum, x); return ValueTask.CompletedTask; }).Wait();
    Console.WriteLine(sum);
});
Try("ForEachAsync throws", () => Parallel.ForEachAsync(Enumerable.Range(0, 5), (x, ct) => { if (x == 2) throw new ArgumentException("two"); return ValueTask.CompletedTask; }).Wait());
Try("ForEachAsync null", () => Parallel.ForEachAsync((IEnumerable<int>)null, (x, ct) => ValueTask.CompletedTask));
Try("ForEachAsync null body", () => Parallel.ForEachAsync(new[] { 1 }, (Func<int, CancellationToken, ValueTask>)null));
Try("ForEachAsync canceled", () => { var c = new CancellationTokenSource(); c.Cancel(); Parallel.ForEachAsync(new[] { 1 }, c.Token, (x, ct) => ValueTask.CompletedTask).Wait(); });
Try("ForAsync", () =>
{
    var sum = 0;
    Parallel.ForAsync(0, 5, async (i, ct) => { await Task.Yield(); Interlocked.Add(ref sum, i); }).Wait();
    Console.WriteLine(sum);
});
Try("ForAsync long", () =>
{
    var sum = 0L;
    Parallel.ForAsync(0L, 5L, new ParallelOptions { MaxDegreeOfParallelism = 2 }, (i, ct) => { Interlocked.Add(ref sum, i); return ValueTask.CompletedTask; }).Wait();
    Console.WriteLine(sum);
});
Try("ForAsync token dop", () => { var o = new ParallelOptions { MaxDegreeOfParallelism = 2 }; Parallel.ForAsync(0, 3, o, (i, ct) => ValueTask.CompletedTask).Wait(); });

class NonOrderable : Partitioner<int>
{
    public override bool SupportsDynamicPartitions => true;
    public override IList<IEnumerator<int>> GetPartitions(int n) => new[] { Enumerable.Range(0, 4).GetEnumerator() };
    public override IEnumerable<int> GetDynamicPartitions() => Enumerable.Range(0, 4);
}
class NotNormalized : OrderablePartitioner<int>
{
    public NotNormalized() : base(true, true, false) { }
    public override bool SupportsDynamicPartitions => true;
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int n) => new[] { Enumerable.Range(0, 4).Select(i => new KeyValuePair<long, int>(i * 10, i)).GetEnumerator() };
    public override IEnumerable<KeyValuePair<long, int>> GetOrderableDynamicPartitions() => Enumerable.Range(0, 4).Select(i => new KeyValuePair<long, int>(i * 10, i));
}
