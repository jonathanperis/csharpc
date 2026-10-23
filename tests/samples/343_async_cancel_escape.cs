// An OperationCanceledException escaping an async method cancels the task (not faults it), and
// `await` rethrows that same exception object; .Result wraps a TaskCanceledException.
using System;
using System.Threading;
using System.Threading.Tasks;

class MyCanceled : OperationCanceledException
{
    public MyCanceled() : base("custom cancel") { }
}

static class Program
{
    static async Task Cancels(CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
    }

    static async Task<int> CancelsCustom(OperationCanceledException e)
    {
        await Task.Delay(1);
        throw e;
    }

    static async Task<int> CancelsSync()
    {
        if (Environment.ProcessorCount > 0) throw new OperationCanceledException("sync");
        await Task.Yield();
        return 0;
    }

    static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var t = Cancels(cts.Token);
        try { await t; }
        catch (OperationCanceledException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} canceled={t.IsCanceled} faulted={t.IsFaulted} exception={t.Exception is null}"); }

        var custom = new MyCanceled();
        var t2 = CancelsCustom(custom);
        try { await t2; }
        catch (OperationCanceledException e) { Console.WriteLine($"{e.GetType().Name} same={ReferenceEquals(e, custom)} canceled={t2.IsCanceled}"); }

        try { _ = CancelsCustom(new MyCanceled()).Result; }
        catch (AggregateException e) { Console.WriteLine($"Result: {e.InnerException!.GetType().Name}: {e.InnerException.Message}"); }

        var t3 = CancelsSync();
        Console.WriteLine($"sync: completed={t3.IsCompleted} canceled={t3.IsCanceled}");
        try { await t3; }
        catch (OperationCanceledException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        // A faulting exception that is not an OperationCanceledException still faults.
        var t4 = Task.Run(async () => { await Task.Yield(); throw new InvalidOperationException("boom"); });
        try { await t4; }
        catch (InvalidOperationException e) { Console.WriteLine($"{e.Message} faulted={t4.IsFaulted}"); }
    }
}
