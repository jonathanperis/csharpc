// ThreadPool work items in every queueing form: WaitCallback with state, Action<TState> with reference
// and value-type state, IThreadPoolWorkItem, multicast callbacks; QueueUserWorkItem flows AsyncLocal
// values, the Unsafe forms don't. Results are collected and printed in a fixed order.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

sealed class Item(string name, ConcurrentBag<string> results, CountdownEvent done) : IThreadPoolWorkItem
{
    public void Execute()
    {
        results.Add($"work item {name}");
        done.Signal();
    }
}

static class Program
{
    static readonly AsyncLocal<string> s_local = new();

    static void Main()
    {
        var results = new ConcurrentBag<string>();
        var done = new CountdownEvent(10);
        s_local.Value = "flowed";

        ThreadPool.QueueUserWorkItem(state => { results.Add($"wait callback {state} {s_local.Value ?? "none"}"); done.Signal(); }, "s1");
        ThreadPool.UnsafeQueueUserWorkItem(state => { results.Add($"unsafe wait callback {state} {s_local.Value ?? "none"}"); done.Signal(); }, "s2");
        ThreadPool.QueueUserWorkItem(s => { results.Add($"generic ref {s.Length} {s_local.Value ?? "none"}"); done.Signal(); }, "four", preferLocal: false);
        ThreadPool.UnsafeQueueUserWorkItem(s => { results.Add($"unsafe generic ref {s.Length} {s_local.Value ?? "none"}"); done.Signal(); }, "seven!!", preferLocal: true);
        ThreadPool.QueueUserWorkItem(n => { results.Add($"generic value {n * 2}"); done.Signal(); }, 21, preferLocal: false);
        ThreadPool.UnsafeQueueUserWorkItem(t => { results.Add($"unsafe generic tuple {t.Item1}{t.Item2}"); done.Signal(); }, ("a", 1), preferLocal: false);
        ThreadPool.UnsafeQueueUserWorkItem(new Item("one", results, done), preferLocal: false);
        ThreadPool.UnsafeQueueUserWorkItem(new Item("two", results, done), preferLocal: true);
        WaitCallback both = s => results.Add($"first {s}");
        both += s => { results.Add($"second {s}"); done.Signal(); done.Signal(); };
        ThreadPool.QueueUserWorkItem(both, "multicast");

        done.Wait();
        foreach (var line in results.OrderBy(r => r, StringComparer.Ordinal)) Console.WriteLine(line);
        try { ThreadPool.UnsafeQueueUserWorkItem((IThreadPoolWorkItem)null!, false); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { ThreadPool.QueueUserWorkItem<int>(null!, 1, false); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
    }
}
