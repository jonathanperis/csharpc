// ConcurrentDictionary<string, V>.AlternateLookup<ReadOnlySpan<char>> (Microsoft.Extensions.Caching.Memory's
// string keys) with the default, Ordinal and OrdinalIgnoreCase comparers, and ConcurrentExclusiveSchedulerPair
// (Npgsql's NpgsqlCommand): exclusive work runs alone, concurrent work up to the level, Complete/Completion.
using System.Collections.Concurrent;

foreach (var comparer in new IEqualityComparer<string>?[] { null, StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase })
{
    var d = comparer is null ? new ConcurrentDictionary<string, int>() : new ConcurrentDictionary<string, int>(comparer);
    d["alpha"] = 1;
    var lookup = d.GetAlternateLookup<ReadOnlySpan<char>>();
    ReadOnlySpan<char> text = "xxALPHAbeta-gamma".AsSpan();
    Console.Write((comparer == null ? "default" : comparer == StringComparer.Ordinal ? "ordinal" : "ignore-case") + ": ");
    Console.Write(lookup.TryGetValue(text.Slice(2, 5), out var v) ? $"found {v} " : "missing ");
    Console.Write(lookup.TryGetValue("alpha".AsSpan(), out var actualKey, out v) ? $"{actualKey}={v} " : "none ");
    Console.Write(lookup.TryAdd(text.Slice(7, 4), 2) + " " + lookup.TryAdd("beta".AsSpan(), 3) + " ");
    lookup[text.Slice(12, 5)] = 4;
    Console.Write(d["beta"] + " " + d["gamma"] + " " + lookup.ContainsKey("GAMMA") + " ");
    Console.Write(lookup.TryRemove("beta".AsSpan(), out var removedKey, out v) ? $"removed {removedKey}={v} " : "kept ");
    Console.WriteLine(string.Join(",", d.Keys.Order()) + " " + ReferenceEquals(lookup.Dictionary, d));
    try { _ = lookup["missing"]; } catch (KeyNotFoundException e) { Console.WriteLine("  " + e.GetType().Name); }
}
for (int i = 0; i < 300; i++) new ConcurrentDictionary<string, int>().GetAlternateLookup<ReadOnlySpan<char>>().TryAdd(("k" + i).AsSpan(), i);
var many = new ConcurrentDictionary<string, int>();
var manyLookup = many.GetAlternateLookup<ReadOnlySpan<char>>();
for (int i = 0; i < 1000; i++) manyLookup.TryAdd(("key" + i).AsSpan(), i);
Console.WriteLine(many.Count + " " + many["key999"] + " " + manyLookup.TryGetValue("key500", out var five) + " " + five);
Console.WriteLine(new ConcurrentDictionary<int, int>().TryGetAlternateLookup<long>(out _));
try { new ConcurrentDictionary<string, int>(StringComparer.InvariantCulture).GetAlternateLookup<int>(); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

var pair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 2);
Console.WriteLine(pair.ConcurrentScheduler.MaximumConcurrencyLevel + " " + pair.ExclusiveScheduler.MaximumConcurrencyLevel);
int running = 0, maxConcurrent = 0, exclusiveOverlaps = 0;
var gate = new object();
var concurrent = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
{
    int now = Interlocked.Increment(ref running);
    lock (gate) maxConcurrent = Math.Max(maxConcurrent, now);
    Thread.Sleep(20);
    Interlocked.Decrement(ref running);
    return TaskScheduler.Current == pair.ConcurrentScheduler;
}, CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler)).ToArray();
var exclusive = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
{
    if (Interlocked.Increment(ref running) != 1) Interlocked.Increment(ref exclusiveOverlaps);
    Thread.Sleep(5);
    Interlocked.Decrement(ref running);
    return TaskScheduler.Current == pair.ExclusiveScheduler;
}, CancellationToken.None, TaskCreationOptions.None, pair.ExclusiveScheduler)).ToArray();
Task.WaitAll(concurrent.Concat<Task>(exclusive).ToArray());
Console.WriteLine($"concurrent ok {concurrent.All(t => t.Result)}, at most 2 {maxConcurrent <= 2}, exclusive alone {exclusiveOverlaps == 0} {exclusive.All(t => t.Result)}");
pair.Complete();
pair.Completion.Wait();
Console.WriteLine("completed " + pair.Completion.Status);
try { Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler); }
catch (TaskSchedulerException e) { Console.WriteLine(e.GetType().Name + " / " + e.InnerException?.GetType().Name); }
try { new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
