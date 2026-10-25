// System.Threading.Timer: one-shot, periodic, Change, Dispose, argument checks.
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static Task SleepAsync(int ms)
    {
        var tcs = new TaskCompletionSource<bool>();
        var timer = new Timer(_ => tcs.TrySetResult(true), null, ms, Timeout.Infinite);
        tcs.Task.ContinueWith(_ => timer.Dispose(), TaskScheduler.Default);
        return tcs.Task;
    }

    static async Task Main()
    {
        await SleepAsync(50);
        Console.WriteLine("slept");

        int ticks = 0;
        var done = new TaskCompletionSource();
        using (var periodic = new Timer(state =>
        {
            int n = Interlocked.Increment(ref ticks);
            if (n == 3) done.TrySetResult();
        }, "state", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)))
        {
            await done.Task;
        }
        Console.WriteLine($"ticks >= 3: {Volatile.Read(ref ticks) >= 3}");

        var fired = new TaskCompletionSource<object?>();
        var t = new Timer(s => fired.TrySetResult(s), 42, Timeout.Infinite, Timeout.Infinite);
        Console.WriteLine(fired.Task.IsCompleted);
        Console.WriteLine(t.Change(20, Timeout.Infinite));
        Console.WriteLine(await fired.Task);
        t.Dispose();
        t.Dispose();
        Console.WriteLine(t.Change(1, 1));

        var never = new TaskCompletionSource();
        var stopped = new Timer(_ => never.TrySetResult(), null, 30, 0);
        stopped.Change(Timeout.Infinite, Timeout.Infinite);
        await Task.Delay(80);
        Console.WriteLine($"stopped fired: {never.Task.IsCompleted}");
        await stopped.DisposeAsync();

        try { new Timer(null!, null, 1, 1); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { new Timer(_ => { }, null, -2, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
        try { new Timer(_ => { }, null, 1, -5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
    }
}
