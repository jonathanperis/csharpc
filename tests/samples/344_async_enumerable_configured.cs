// await foreach over ConfiguredCancelableAsyncEnumerable (WithCancellation / ConfigureAwait) and a
// hand-written IAsyncEnumerable whose MoveNextAsync returns ValueTask<bool> over an IValueTaskSource.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

sealed class Countdown : IAsyncEnumerable<int>
{
    private readonly int _from;
    public Countdown(int from) => _from = from;
    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => new Enumerator(_from, token);

    private sealed class Enumerator : IAsyncEnumerator<int>, IValueTaskSource<bool>
    {
        private ManualResetValueTaskSourceCore<bool> _core;
        private readonly CancellationToken _token;
        private int _next;
        public int Current { get; private set; }

        public Enumerator(int from, CancellationToken token) { _next = from; _token = token; }

        public ValueTask<bool> MoveNextAsync()
        {
            _core.Reset();
            var version = _core.Version;
            var hasNext = _next > 0;
            if (hasNext) Current = _next--;
            if (_next % 2 == 0)
                _core.SetResult(hasNext); // synchronous completion
            else
                Task.Run(async () =>
                {
                    await Task.Delay(1);
                    if (_token.IsCancellationRequested) _core.SetException(new OperationCanceledException(_token));
                    else _core.SetResult(hasNext);
                });
            return new ValueTask<bool>(this, version);
        }

        public ValueTask DisposeAsync()
        {
            Console.WriteLine("disposed");
            return default;
        }

        public bool GetResult(short token) => _core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}

static class Program
{
    static async Task Main()
    {
        await foreach (var x in new Countdown(5)) Console.Write(x + " ");
        Console.WriteLine();

        await foreach (var x in new Countdown(4).ConfigureAwait(false)) Console.Write(x + " ");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        var seen = 0;
        try
        {
            await foreach (var x in new Countdown(10).WithCancellation(cts.Token))
            {
                seen++;
                if (x == 8) cts.Cancel();
            }
        }
        catch (OperationCanceledException e) { Console.WriteLine($"canceled after {seen}: {e.Message}"); }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, CancellationToken.None);
        Console.WriteLine($"linked: {linked.IsCancellationRequested}");
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        var both = CancellationTokenSource.CreateLinkedTokenSource(a.Token, b.Token);
        Console.WriteLine($"before: {both.IsCancellationRequested}");
        b.Cancel();
        Console.WriteLine($"after: {both.IsCancellationRequested} {a.IsCancellationRequested}");
        Console.WriteLine($"equal: {a.Token.Equals(a.Token)} {a.Token.Equals(b.Token)} {CancellationToken.None.Equals(default(CancellationToken))}");
    }
}
