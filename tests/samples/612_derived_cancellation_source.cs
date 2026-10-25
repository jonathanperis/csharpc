// Classes deriving from CancellationTokenSource (as Kestrel pools them): their own fields, the
// Dispose(bool) override reached from Dispose(), `using` and IDisposable, base.Dispose, TryReset, tokens
// and callbacks, the delayed base constructor and type tests.
using System;
using System.Threading;
using System.Threading.Tasks;

class PooledSource : CancellationTokenSource
{
    private readonly string _name;
    public int Disposals;

    public PooledSource(string name) { _name = name; }

    protected override void Dispose(bool disposing)
    {
        Disposals++;
        Console.WriteLine($"{_name}: Dispose({disposing}) reset={(disposing && TryReset())}");
        if (Disposals > 1) base.Dispose(disposing);
    }

    public override string ToString() => "PooledSource " + _name;
}

class DelayedSource : CancellationTokenSource
{
    public DelayedSource(int ms) : base(ms) { }
}

class Program
{
    static async Task Main()
    {
        var source = new PooledSource("a");
        var token = source.Token;
        token.Register(() => Console.WriteLine("callback ran"));
        Console.WriteLine($"{source.IsCancellationRequested} {token.CanBeCanceled} {source} {source is CancellationTokenSource}");
        source.Dispose();                       // TryReset succeeds: the source is reusable
        Console.WriteLine($"{source.Disposals} {source.Token.IsCancellationRequested}");
        source.Cancel();                        // the callback was dropped by TryReset
        Console.WriteLine($"{source.IsCancellationRequested} {token.IsCancellationRequested}");
        source.Dispose();                       // canceled: TryReset fails, base.Dispose runs
        try { _ = source.Token; Console.WriteLine("token after dispose"); }
        catch (ObjectDisposedException) { Console.WriteLine("disposed"); }

        using (var scoped = new PooledSource("b")) { scoped.Cancel(); }
        IDisposable disposable = new PooledSource("c");
        disposable.Dispose();

        CancellationTokenSource asBase = new PooledSource("d");
        asBase.Dispose();
        Console.WriteLine(asBase.GetType().Name);

        var delayed = new DelayedSource(30);
        Console.WriteLine(delayed.IsCancellationRequested);
        try { await Task.Delay(5000, delayed.Token); }
        catch (TaskCanceledException) { Console.WriteLine($"delayed canceled {delayed.IsCancellationRequested}"); }
        var immediate = new DelayedSource(0);
        Console.WriteLine(immediate.IsCancellationRequested);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(new PooledSource("e").Token, immediate.Token);
        Console.WriteLine(linked.IsCancellationRequested);
    }
}
