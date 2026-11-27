// ManualResetEventSlim as the framework's code implements it: Set/Reset/IsSet, Wait with and without a timeout, a waiter woken by
// Set from another thread (the event waits on a System.Threading.Lock's condition), Set waking every waiter, a token that ends a
// wait, WaitHandle in step with the state, SpinCount and its checks, and the disposed behaviour. Each step waits for a state,
// not for a time.
using System;
using System.Threading;

static class Program
{
    static void Main()
    {
        using var ev = new ManualResetEventSlim();
        Console.WriteLine($"new: IsSet {ev.IsSet}, SpinCount>0 {ev.SpinCount > 0}, Wait(0) {ev.Wait(0)}, Wait(20) {ev.Wait(20)}");
        ev.Set();
        Console.WriteLine($"Set: IsSet {ev.IsSet}, Wait(0) {ev.Wait(0)}, Wait() returns");
        ev.Wait();
        ev.Reset();
        Console.WriteLine($"Reset: IsSet {ev.IsSet}, Wait(TimeSpan 10ms) {ev.Wait(TimeSpan.FromMilliseconds(10))}");

        // Waiters blocked on the event, released together by one Set.
        int woken = 0;
        var started = new CountdownEvent(3);
        var threads = new Thread[3];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() => { started.Signal(); ev.Wait(); Interlocked.Increment(ref woken); });
            threads[i].Start();
        }
        started.Wait();
        Console.WriteLine($"before Set: woken {Volatile.Read(ref woken)}");
        ev.Set();
        foreach (var t in threads) t.Join();
        Console.WriteLine($"after Set: woken {woken}");
        ev.Reset();

        // A timed wait that a Set from another thread ends early.
        var setter = new Thread(() => { Thread.Sleep(30); ev.Set(); });
        setter.Start();
        Console.WriteLine($"Wait(10000) ended by Set: {ev.Wait(10000)}");
        setter.Join();
        ev.Reset();

        // Tokens: canceled before, canceled during.
        try { ev.Wait(new CancellationToken(true)); } catch (OperationCanceledException e) { Console.WriteLine($"pre-canceled: {e.GetType().Name}"); }
        using (var cts = new CancellationTokenSource())
        {
            var canceler = new Thread(() => { Thread.Sleep(30); cts.Cancel(); });
            canceler.Start();
            try { ev.Wait(cts.Token); Console.WriteLine("not canceled"); }
            catch (OperationCanceledException e) { Console.WriteLine($"canceled while waiting: {e.GetType().Name}, token matches {e.CancellationToken == cts.Token}"); }
            canceler.Join();
        }
        Console.WriteLine($"Wait(5, token) on unset: {ev.Wait(5, CancellationToken.None)}");

        // Thread.Interrupt ends the Wait (whether it arrives before or during it).
        var interrupted = new Thread(() =>
        {
            try { ev.Wait(); Console.WriteLine("not interrupted"); }
            catch (ThreadInterruptedException) { Console.WriteLine("interrupted in Wait"); }
        });
        interrupted.Start();
        interrupted.Interrupt();
        interrupted.Join();

        // WaitHandle follows Set/Reset.
        var handle = ev.WaitHandle;
        Console.WriteLine($"handle unset: {handle.WaitOne(0)}");
        ev.Set();
        Console.WriteLine($"handle set: {handle.WaitOne(0)}");
        ev.Reset();
        Console.WriteLine($"handle reset: {handle.WaitOne(0)}");

        // Constructor checks and initial state.
        Console.WriteLine($"new(true): {new ManualResetEventSlim(true).IsSet}, new(false, 7).SpinCount {new ManualResetEventSlim(false, 7).SpinCount}");
        try { new ManualResetEventSlim(false, -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"spinCount -1: {e.Message}"); }
        try { new ManualResetEventSlim(false, 2048); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"spinCount 2048: {e.Message}"); }
        try { ev.Wait(-2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"Wait(-2): {e.ParamName}"); }

        // Disposed: IsSet still reads, waits and Set throw.
        var gone = new ManualResetEventSlim(true);
        gone.Dispose();
        Console.WriteLine($"disposed IsSet: {gone.IsSet}");
        try { gone.Wait(0); } catch (ObjectDisposedException e) { Console.WriteLine($"disposed Wait: {e.Message}"); }
        try { gone.Reset(); Console.WriteLine("disposed Reset: no exception"); } catch (ObjectDisposedException e) { Console.WriteLine($"disposed Reset: {e.GetType().Name}"); }
        gone.Dispose();
        Console.WriteLine("second Dispose: no exception");
    }
}
