// TaskExtensions.Unwrap and ConfigureAwait(IAsyncDisposable, bool).
using System;
using System.Threading;
using System.Threading.Tasks;

sealed class Resource : IAsyncDisposable
{
    public string Name = "";
    public async ValueTask DisposeAsync() { await Task.Yield(); Console.WriteLine($"disposed {Name}"); }
}

class Program
{
    static async Task<int> Compute(int x) { await Task.Delay(5); return x * 2; }

    static async Task Main()
    {
        Task<Task<int>> nested = Task.Factory.StartNew(() => Compute(21));
        Console.WriteLine(await nested.Unwrap());
        Task<Task> nestedVoid = Task.Factory.StartNew(async () => { await Task.Delay(1); Console.WriteLine("inner ran"); });
        await nestedVoid.Unwrap();
        Console.WriteLine("after inner");

        var done = Task.FromResult(Task.FromResult(7));
        Console.WriteLine($"{done.Unwrap().Result} {ReferenceEquals(done.Unwrap(), done.Result)}");

        var faultedOuter = Task.FromException<Task<int>>(new InvalidOperationException("outer failed"));
        try { await faultedOuter.Unwrap(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        Func<Task<int>> failing = () => Task.FromException<int>(new ArgumentException("inner failed"));
        Task<Task<int>> wrapped = Task.Factory.StartNew(failing);
        try { await wrapped.Unwrap(); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        var unwrappedFault = wrapped.Unwrap();
        try { unwrappedFault.Wait(); } catch (AggregateException e) { Console.WriteLine($"{e.InnerException!.GetType().Name} {unwrappedFault.Status}"); }

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceledOuter = Task.FromCanceled<Task<int>>(cts.Token);
        var u = canceledOuter.Unwrap();
        try { await u; } catch (OperationCanceledException) { Console.WriteLine($"canceled {u.Status}"); }
        Task<Task<int>> nullInner = Task.Factory.StartNew<Task<int>>(() => null!);
        var n = nullInner.Unwrap();
        try { await n; } catch (OperationCanceledException) { Console.WriteLine($"null inner {n.Status}"); }
        try { ((Task<Task>)null!).Unwrap(); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        var r = new Resource { Name = "a" };
        await using (r.ConfigureAwait(false))
        {
            Console.WriteLine("using a");
        }
        await using (var b = new Resource { Name = "b" })
        {
            Console.WriteLine("using b");
        }
    }
}
