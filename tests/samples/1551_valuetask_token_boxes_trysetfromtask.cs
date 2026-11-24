// Boxed ValueTask and CancellationToken (type tests, equality, ToString) and TaskCompletionSource.TrySetFromTask (#96).
using System;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static void Main()
    {
        object a = new ValueTask(), b = new ValueTask();
        Console.WriteLine($"{a is ValueTask} {a is ValueTask<int>} {a.Equals(b)} {a} {a.GetType().FullName}");
        object generic = new ValueTask<int>(5);
        Console.WriteLine($"{generic is ValueTask<int>} {generic is ValueTask}");
        var task = Task.FromResult(1);
        object c = new ValueTask(task), d = new ValueTask(task), e = new ValueTask(Task.FromResult(2));
        Console.WriteLine($"{c.Equals(d)} {c.Equals(e)} {c.GetHashCode() == d.GetHashCode()}");

        var cts = new CancellationTokenSource();
        object f = cts.Token, g = cts.Token, none = CancellationToken.None;
        Console.WriteLine($"{f is CancellationToken} {f is ValueType} {f.Equals(g)} {f.Equals(none)} {f} {f.GetHashCode() == g.GetHashCode()}");
        Console.WriteLine(f is CancellationToken token ? $"can be canceled: {token.CanBeCanceled}" : "no");

        var source = new TaskCompletionSource<int>();
        Console.WriteLine($"result: {source.TrySetFromTask(Task.FromResult(42))} {source.Task.Result} again: {source.TrySetFromTask(Task.FromResult(7))}");
        var plain = new TaskCompletionSource();
        Console.WriteLine($"completed: {plain.TrySetFromTask(Task.CompletedTask)} {plain.Task.Status}");
        var faulted = new TaskCompletionSource<int>();
        Console.WriteLine($"faulted: {faulted.TrySetFromTask(Task.FromException<int>(new InvalidOperationException("x")))} {faulted.Task.Status} {faulted.Task.Exception!.InnerException!.Message}");
        var canceledSource = new CancellationTokenSource();
        canceledSource.Cancel();
        var canceled = new TaskCompletionSource();
        Console.WriteLine($"canceled: {canceled.TrySetFromTask(Task.FromCanceled(canceledSource.Token))} {canceled.Task.Status}");
        try { canceled.Task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException oce) { Console.WriteLine($"token kept: {oce.CancellationToken == canceledSource.Token}"); }
        var pending = new TaskCompletionSource<int>();
        try { new TaskCompletionSource<int>().TrySetFromTask(pending.Task); }
        catch (ArgumentException ae) { Console.WriteLine($"{ae.GetType().Name}: {ae.Message}"); }
        try { new TaskCompletionSource().TrySetFromTask(null!); }
        catch (ArgumentException ae) { Console.WriteLine($"{ae.GetType().Name}: {ae.Message}"); }
    }
}
