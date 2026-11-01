// csharpc-benchmark: via-il
// Pooled async methods (PoolingAsyncValueTaskMethodBuilder<T>, as Kestrel, Pipelines, SslStream and Npgsql use
// it): two levels of methods that suspend on a reusable signal and complete inline when it is set, so every
// call suspends and the cost is the builder's (box, delegate, task) rather than the thread pool's. Compiled
// through the IL front-end (the builder only exists in IL; source async methods are coroutines).
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

const int Rounds = 1_000_000;
var signal = new Signal();
long sum = 0;
for (int i = 0; i < Rounds; i++)
{
    ValueTask<int> pending = Outer(signal, i);
    signal.Set(i & 1023);
    signal.Set(i & 511);
    signal.Set(i & 255);
    sum += pending.Result;
}
Console.WriteLine($"pooled async sum: {sum}");

[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
static async ValueTask<int> Outer(Signal signal, int i)
{
    int a = await Inner(signal);
    int b = await Inner(signal);
    int c = await Inner(signal);
    return a + b * 3 + c * 7 + (i & 1);
}

[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
static async ValueTask<int> Inner(Signal signal)
{
    int v = await signal.Wait();
    return v + 1;
}

/// <summary>A reusable one-waiter signal: Set runs the waiter's continuation inline.</summary>
sealed class Signal : IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _core;

    public ValueTask<int> Wait() => new ValueTask<int>(this, _core.Version);

    public void Set(int value) => _core.SetResult(value);

    public int GetResult(short token)
    {
        int result = _core.GetResult(token);
        _core.Reset();
        return result;
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}
