// Task.WhenAll's exception order: WhenAll(Task[]) takes failed inputs in completion order, WhenAll(Task<T>[])
// in array order; already-completed inputs count in registration order; the first canceled input (with its
// token) cancels the result when none faulted; tasks faulted with several exceptions contribute them all.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();
        Task all = Task.WhenAll(a.Task, b.Task);
        b.SetException(new InvalidOperationException("second input, completed first"));
        a.SetException(new InvalidOperationException("first input, completed last"));
        await Report("Task[] completion order", all);

        var ga = new TaskCompletionSource<int>();
        var gb = new TaskCompletionSource<int>();
        Task<int[]> gall = Task.WhenAll(ga.Task, gb.Task);
        gb.SetException(new InvalidOperationException("second input, completed first"));
        ga.SetException(new InvalidOperationException("first input, completed last"));
        await Report("Task<T>[] array order", gall);

        Task done = Task.WhenAll(Task.FromException(new ArgumentException("one")), Task.FromException(new FormatException("two")));
        await Report("already completed", done);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var c1 = new TaskCompletionSource();
        var c2 = new TaskCompletionSource();
        Task canceled = Task.WhenAll(c1.Task, c2.Task);
        c2.SetCanceled(cts.Token);
        c1.SetCanceled();
        try { await canceled; }
        catch (TaskCanceledException e) { Console.WriteLine($"canceled: token matches {e.CancellationToken == cts.Token}, status {canceled.Status}"); }

        var m = new TaskCompletionSource();
        var s = new TaskCompletionSource();
        Task many = Task.WhenAll(m.Task, s.Task);
        s.SetException(new Exception("single"));
        m.SetException(new Exception[] { new Exception("many 1"), new Exception("many 2") });
        await Report("several exceptions", many);

        var f = new TaskCompletionSource();
        var k = new TaskCompletionSource();
        Task mixed = Task.WhenAll(f.Task, k.Task);
        k.SetCanceled();
        f.SetException(new Exception("faulted wins"));
        await Report("faulted and canceled", mixed);
    }

    static async Task Report(string what, Task task)
    {
        try { await task; }
        catch (Exception e) { Console.WriteLine($"{what}: awaited {e.Message}"); }
        Console.WriteLine($"  {task.Status}: {string.Join(" | ", task.Exception!.InnerExceptions.Select(x => x.Message))}");
    }
}
