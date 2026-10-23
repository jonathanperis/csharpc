using System.Runtime.CompilerServices;

namespace IlAsyncLib;

public static class Work
{
    public static async Task<int> DoubleAsync(int x)
    {
        await Task.Yield();
        return x * 2;
    }

    // Not called by IlAsyncApp: csharpc never lowers it (only reachable methods are compiled from IL).
    public static async Task<string> UnusedAsync()
    {
        await Task.Delay(1);
        return "unused";
    }

    // Completes synchronously: every awaited task is already complete.
    public static async Task<int> SyncAsync(int x)
    {
        await Task.CompletedTask;
        var y = await Task.FromResult(x + 1);
        return y * 10;
    }

    public static async Task NoResultAsync(List<string> log)
    {
        log.Add("start");
        await Task.Delay(5);
        log.Add("after delay");
        await Task.Yield();
        log.Add("end");
    }

    public static async Task<int> ThrowBeforeAwaitAsync(int x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), "negative");
        await Task.Yield();
        return x;
    }

    public static async Task<int> ThrowAfterAwaitAsync(string message)
    {
        await Task.Delay(1);
        throw new InvalidOperationException(message);
    }

    public static async Task<string> TryCatchFinallyAsync(List<string> log)
    {
        try
        {
            log.Add("try");
            await ThrowAfterAwaitAsync("inner failure");
            log.Add("unreachable");
        }
        catch (InvalidOperationException e)
        {
            log.Add("catch " + e.Message);
            await Task.Yield(); // await in catch
            log.Add("after await in catch");
        }
        finally
        {
            log.Add("finally");
            await Task.Delay(1); // await in finally
            log.Add("after await in finally");
        }
        return string.Join(",", log);
    }

    public static async Task<int> ConfigureAwaitAsync(int x)
    {
        await Task.Delay(1).ConfigureAwait(false);
        var y = await DoubleAsync(x).ConfigureAwait(false);
        await Task.Yield();
        return y + 1;
    }

    public static async ValueTask<int> ValueTaskAsync(int x)
    {
        if (x == 0) return 0; // synchronous completion: no task allocated in .NET either
        await Task.Yield();
        return x + await ValueTaskAsync(x - 1);
    }

    public static async ValueTask ValueTaskNoResultAsync(List<string> log)
    {
        await Task.Delay(1);
        log.Add("valuetask done");
    }

    public static async Task<T> EchoAsync<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    public static async Task<int> FibAsync(int n)
    {
        if (n < 2) return n;
        var a = FibAsync(n - 1);
        var b = FibAsync(n - 2);
        return await a + await b;
    }

    public static async Task<long> LoopAsync(int n)
    {
        long sum = 0;
        for (var i = 0; i < n; i++)
        {
            await Task.Yield();
            sum += i;
        }
        return sum;
    }

    public static async Task<int[]> WhenAllAsync()
    {
        var tasks = new List<Task<int>>();
        for (var i = 1; i <= 5; i++) tasks.Add(DoubleAsync(i));
        return await Task.WhenAll(tasks);
    }

    public static async Task<int> RunAsync()
    {
        var a = await Task.Run(() => 20);
        var b = await Task.Run(async () => { await Task.Yield(); return 22; });
        return a + b;
    }

    // Async lambdas and closures: the state machine lives in a display class.
    public static async Task<string> LambdaAsync(int k)
    {
        Func<int, Task<int>> f = async v => { await Task.Delay(1); return v * k; };
        var r = await f(3) + await f(4);
        return $"lambda {r}";
    }

    // Awaiting a task produced by the caller's (source) code.
    public static async Task<int> CallbackAsync(Func<Task<int>> callback)
    {
        var a = await callback();
        var b = await callback();
        return a * 100 + b;
    }

    public static async Task<int> CustomAwaitableAsync()
    {
        var a = await new Immediate(5);
        var b = await new Deferred(7);
        return a + b;
    }

    public static async Task CanceledAsync(CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
    }

    public static async void FireAndForget(TaskCompletionSource<string> done, string text)
    {
        await Task.Delay(1);
        done.SetResult(text.ToUpperInvariant());
    }
}

public sealed class Counter
{
    private int _value;
    public string Name { get; }

    public Counter(string name) => Name = name;

    public async Task<int> AddAsync(int delta)
    {
        await Task.Yield();
        _value += delta;
        return _value;
    }

    public async Task<string> DescribeAsync()
    {
        var v = await AddAsync(0);
        return $"{Name}={v}";
    }
}

public readonly struct Immediate(int value)
{
    public Awaiter GetAwaiter() => new(value);

    public readonly struct Awaiter(int value) : INotifyCompletion
    {
        public bool IsCompleted => true;
        public int GetResult() => value;
        public void OnCompleted(Action continuation) => throw new InvalidOperationException("never suspends");
    }
}

public sealed class Deferred(int value)
{
    public DeferredAwaiter GetAwaiter() => new(value);
}

public sealed class DeferredAwaiter(int value) : ICriticalNotifyCompletion
{
    private bool _done;
    public bool IsCompleted => _done;
    public int GetResult() => value * 2;
    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation)
    {
        _done = true;
        Task.Run(continuation);
    }
}
