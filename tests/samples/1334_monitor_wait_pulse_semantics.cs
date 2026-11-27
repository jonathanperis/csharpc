// Monitor.Wait / Pulse / PulseAll, contended Enter and SemaphoreSlim, the semantics the runtime's monitor must keep while it
// spins before blocking and wakes one waiter per Pulse (#350): Pulse releases exactly one waiter, the oldest; PulseAll releases
// all; Wait releases a recursively held lock completely and restores the recursion; a timed-out Wait returns false with the lock
// held again; Thread.Interrupt ends a Wait (and a pending one the next Wait) with the lock held again; TryEnter on a held lock fails
// at once or after its timeout. Each step waits for a state, never for a time, so the output does not depend on scheduling.
using System;
using System.Collections.Generic;
using System.Threading;

static class Program
{
    static readonly object Gate = new();
    static int s_waiting;
    static readonly List<int> s_woken = new();

    // Blocks until `count` waiters are inside Monitor.Wait on Gate (each counts itself under the lock right before waiting).
    static void UntilWaiting(int count)
    {
        while (true)
        {
            lock (Gate) if (s_waiting == count) return;
            Thread.Sleep(1);
        }
    }

    static int Woken() { lock (Gate) return s_woken.Count; }

    static void UntilWoken(int count)
    {
        while (Woken() < count) Thread.Sleep(1);
    }

    static Thread Waiter(int id)
    {
        var t = new Thread(() =>
        {
            lock (Gate)
            {
                s_waiting++;
                Monitor.Wait(Gate);
                s_waiting--;
                s_woken.Add(id);
            }
        });
        t.Start();
        return t;
    }

    static void Main()
    {
        // Pulse: one waiter per Pulse, oldest first.
        var waiters = new List<Thread>();
        for (int i = 0; i < 3; i++) { waiters.Add(Waiter(i)); UntilWaiting(i + 1); }
        lock (Gate) Monitor.Pulse(Gate);
        UntilWoken(1);
        Thread.Sleep(50); // a broadcast would have woken the others by now; they must still be waiting
        lock (Gate) Console.WriteLine($"after one Pulse: woken [{string.Join(",", s_woken)}], still waiting {s_waiting}");
        lock (Gate) Monitor.Pulse(Gate);
        UntilWoken(2);
        lock (Gate) Console.WriteLine($"after two Pulses: woken [{string.Join(",", s_woken)}], still waiting {s_waiting}");

        // PulseAll: the rest, plus new waiters.
        waiters.Add(Waiter(3)); UntilWaiting(2);
        lock (Gate) Monitor.PulseAll(Gate);
        foreach (var t in waiters) t.Join();
        lock (Gate) { s_woken.Sort(); Console.WriteLine($"after PulseAll: woken [{string.Join(",", s_woken)}], still waiting {s_waiting}"); }

        // A Pulse with no waiter is lost, as in .NET: a later Wait times out.
        lock (Gate)
        {
            Monitor.Pulse(Gate);
            Console.WriteLine($"Wait(20) after an unawaited Pulse: {Monitor.Wait(Gate, 20)}, still entered {Monitor.IsEntered(Gate)}");
        }

        // Wait releases a recursive lock completely and restores the recursion.
        bool otherEntered = false;
        lock (Gate)
        lock (Gate)
        {
            var other = new Thread(() => { lock (Gate) { otherEntered = true; Monitor.Pulse(Gate); } });
            other.Start();
            while (!otherEntered) Monitor.Wait(Gate);
            other.Join();
            Console.WriteLine($"another thread entered during a recursive Wait: {otherEntered}");
        }
        Console.WriteLine($"entered after both exits: {Monitor.IsEntered(Gate)}");
        try { Monitor.Exit(Gate); }
        catch (SynchronizationLockException) { Console.WriteLine("a third Exit: SynchronizationLockException"); }

        // TryEnter on a lock another thread holds: false at once, and false after its timeout.
        using (var held = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var owner = new Thread(() => { lock (Gate) { held.Set(); release.Wait(); } });
            owner.Start();
            held.Wait();
            Console.WriteLine($"TryEnter(0) on a held lock: {Monitor.TryEnter(Gate)}");
            Console.WriteLine($"TryEnter(30) on a held lock: {Monitor.TryEnter(Gate, 30)}");
            release.Set();
            owner.Join();
            bool entered = Monitor.TryEnter(Gate, 1000);
            Console.WriteLine($"TryEnter after the owner left: {entered}");
            if (entered) Monitor.Exit(Gate);
        }

        // Thread.Interrupt: during a Wait, and pending before one; the lock is held again in the handler.
        var interrupted = new Thread(() =>
        {
            lock (Gate)
            {
                s_waiting++;
                try { Monitor.Wait(Gate); Console.WriteLine("not interrupted"); }
                catch (ThreadInterruptedException) { Console.WriteLine($"interrupted in Wait, entered {Monitor.IsEntered(Gate)}"); }
                s_waiting--;
            }
        });
        interrupted.Start();
        UntilWaiting(1);
        interrupted.Interrupt();
        interrupted.Join();
        var pending = new Thread(() =>
        {
            Thread.CurrentThread.Interrupt();
            lock (Gate)
            {
                try { Monitor.Wait(Gate, 5000); Console.WriteLine("not interrupted"); }
                catch (ThreadInterruptedException) { Console.WriteLine($"pending interrupt ends the next Wait, entered {Monitor.IsEntered(Gate)}"); }
            }
        });
        pending.Start();
        pending.Join();

        // SemaphoreSlim: Wait(0) and a timed Wait on an empty count, Release handing the count to a blocked waiter.
        using var semaphore = new SemaphoreSlim(0, 2);
        Console.WriteLine($"Wait(0) on 0: {semaphore.Wait(0)}, Wait(20) on 0: {semaphore.Wait(20)}");
        int got = 0;
        var taker = new Thread(() => { semaphore.Wait(); Interlocked.Increment(ref got); });
        taker.Start();
        semaphore.Release();
        taker.Join();
        Console.WriteLine($"blocked Wait got the released count: {got}, count now {semaphore.CurrentCount}");
        semaphore.Release(2);
        try { semaphore.Release(); }
        catch (SemaphoreFullException) { Console.WriteLine($"Release past the maximum: SemaphoreFullException, count {semaphore.CurrentCount}"); }
    }
}
