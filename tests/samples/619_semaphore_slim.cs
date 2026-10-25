// SemaphoreSlim: counts, Release's previous count, the maximum, asynchronous waiters released in order,
// timeouts and cancellation of synchronous and asynchronous waits, argument checks and Dispose.
#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var s = new SemaphoreSlim(2, 3);
        Console.WriteLine($"{s.CurrentCount} {s.Wait(0)} {s.Wait(0)} {s.Wait(0)} {s.CurrentCount}");
        Console.WriteLine($"{s.Release()} {s.Release(2)} {s.CurrentCount}");
        try { s.Release(); } catch (SemaphoreFullException e) { Console.WriteLine(e.Message); }
        try { s.Release(0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

        var gate = new SemaphoreSlim(0);
        var first = gate.WaitAsync();
        var second = gate.WaitAsync(5000);
        var third = gate.WaitAsync();
        Console.WriteLine($"{first.IsCompleted} {second.IsCompleted} {third.IsCompleted}");
        gate.Release();
        await first;
        Console.WriteLine($"first {third.IsCompleted}");
        gate.Release(2);
        Console.WriteLine($"second {await second}");
        await third;
        Console.WriteLine($"third {gate.CurrentCount}");

        Console.WriteLine($"timed out: {await gate.WaitAsync(20)} {gate.Wait(20)} {gate.Wait(TimeSpan.FromMilliseconds(10))}");
        using (var cts = new CancellationTokenSource(30))
        {
            try { await gate.WaitAsync(cts.Token); } catch (OperationCanceledException) { Console.WriteLine("async canceled"); }
        }
        using (var cts = new CancellationTokenSource(30))
        {
            try { gate.Wait(cts.Token); } catch (OperationCanceledException) { Console.WriteLine("sync canceled"); }
        }
        var released = Task.Run(async () => { await Task.Delay(30); gate.Release(); });
        Console.WriteLine($"woken {gate.Wait(5000)}");
        await released;

        try { new SemaphoreSlim(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        try { new SemaphoreSlim(1, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        try { gate.Wait(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        gate.Dispose();
        try { gate.Release(); } catch (ObjectDisposedException e) { Console.WriteLine(e.Message); }
    }
}
