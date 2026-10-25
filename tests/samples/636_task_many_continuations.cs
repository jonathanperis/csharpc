// One task with no, one and many continuations of every kind (awaits, OnCompleted actions, ContinueWith,
// WhenAll/WhenAny), registered before it completes; continuations run in registration order.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

static class Program
{
    static readonly List<string> s_log = new();

    static async Task Await(Task<int> t, string name)
    {
        int v = await t;
        lock (s_log) s_log.Add($"{name}={v}");
    }

    static async Task Main()
    {
        var none = new TaskCompletionSource<int>();
        none.SetResult(1);
        Console.WriteLine(await none.Task);

        var one = new TaskCompletionSource<int>();
        var single = Await(one.Task, "single");
        one.SetResult(2);
        await single;

        var many = new TaskCompletionSource<int>();
        var awaiters = new List<Task>();
        for (int i = 0; i < 5; i++) awaiters.Add(Await(many.Task, $"await{i}"));
        TaskAwaiter<int> awaiter = many.Task.GetAwaiter();
        var actionRan = new TaskCompletionSource();  // .NET may run the action on the pool: wait for it
        awaiter.OnCompleted(() => { lock (s_log) s_log.Add("action"); actionRan.SetResult(); });
        var cont = many.Task.ContinueWith(t => { lock (s_log) s_log.Add($"continue={t.Result}"); }, TaskContinuationOptions.ExecuteSynchronously);
        var all = Task.WhenAll(many.Task, Task.FromResult(9));
        var any = Task.WhenAny(many.Task, new TaskCompletionSource<int>().Task);
        many.SetResult(3);
        await Task.WhenAll(awaiters);
        await cont;
        await actionRan.Task;
        Console.WriteLine($"{string.Join(",", (await all))} {(await any).Result}");
        lock (s_log) { s_log.Sort(StringComparer.Ordinal); Console.WriteLine(string.Join(" ", s_log)); }

        var faulted = new TaskCompletionSource<int>();
        var f1 = Await(faulted.Task, "never");
        var f2 = Await(faulted.Task, "never");
        faulted.SetException(new InvalidOperationException("boom"));
        foreach (var f in new[] { f1, f2 })
            try { await f; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
    }
}
