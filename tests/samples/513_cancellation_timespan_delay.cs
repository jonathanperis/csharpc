// new CancellationTokenSource(TimeSpan) and CancelAfter(TimeSpan), with .NET's range check ("delay").
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try { await Task.Delay(5000, cts.Token); } catch (TaskCanceledException) { Console.WriteLine("canceled"); }
        Console.WriteLine(cts.IsCancellationRequested);
        using var infinite = new CancellationTokenSource(Timeout.InfiniteTimeSpan);
        Console.WriteLine($"{infinite.Token.CanBeCanceled} {infinite.IsCancellationRequested}");
        using var cts3 = new CancellationTokenSource();
        cts3.CancelAfter(TimeSpan.FromMilliseconds(10));
        try { await Task.Delay(5000, cts3.Token); } catch (TaskCanceledException) { Console.WriteLine("canceled 3"); }
        try { _ = new CancellationTokenSource(TimeSpan.FromMilliseconds(-2)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        try { cts3.CancelAfter(TimeSpan.FromDays(60)); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
        using var never = new CancellationTokenSource(TimeSpan.FromDays(20));
        Console.WriteLine(never.IsCancellationRequested);
    }
}
