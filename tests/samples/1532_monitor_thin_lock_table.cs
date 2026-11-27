// Monitors keep their entry for as long as the object lives and are taken with one atomic operation; the collector turns the
// entries of dead objects into tombstones that later objects reuse (#350). Each case checks a behaviour that depends on it:
// ownership and recursion, the exceptions without ownership, many objects at once, collections while locks are held and
// while entries die, Wait/Pulse hand-offs, timeouts and interruption.
using System;
using System.Collections.Generic;
using System.Threading;

static class Program
{
    static void Main()
    {
        var o = new object();
        Monitor.Enter(o);
        Monitor.Enter(o);
        Console.WriteLine("entered " + Monitor.IsEntered(o));
        Monitor.Exit(o);
        Console.WriteLine("after one exit " + Monitor.IsEntered(o));
        Monitor.Exit(o);
        Console.WriteLine("after two " + Monitor.IsEntered(o));
        try { Monitor.Exit(o); } catch (SynchronizationLockException e) { Console.WriteLine("exit: " + e.Message); }
        try { Monitor.Pulse(o); } catch (SynchronizationLockException e) { Console.WriteLine("pulse: " + e.Message); }
        try { Monitor.Wait(o, 0); } catch (SynchronizationLockException e) { Console.WriteLine("wait: " + e.Message); }
        try { Monitor.Enter(null!); } catch (ArgumentNullException e) { Console.WriteLine("null: " + e.ParamName); }

        // Another thread can't exit or see a monitor this thread holds, and TryEnter times out on it.
        lock (o)
        {
            bool otherEntered = true, otherTry = true; string? otherExit = null;
            var t = new Thread(() =>
            {
                otherEntered = Monitor.IsEntered(o);
                otherTry = Monitor.TryEnter(o, 20);
                try { Monitor.Exit(o); } catch (SynchronizationLockException) { otherExit = "refused"; }
            });
            t.Start(); t.Join();
            Console.WriteLine($"other thread: entered {otherEntered}, try {otherTry}, exit {otherExit}");
        }

        // Many objects, many threads, collections while locks are held and while locked objects die.
        long total = 0;
        for (int round = 0; round < 20; round++)
        {
            var gates = new List<object>();
            for (int i = 0; i < 64; i++) gates.Add(new object());
            var counts = new int[64];
            var threads = new Thread[4];
            for (int t = 0; t < 4; t++)
            {
                int seed = t;
                threads[t] = new Thread(() =>
                {
                    for (int i = 0; i < 4000; i++)
                    {
                        int k = (i * 7 + seed * 13) % 64;
                        lock (gates[k])
                        {
                            counts[k]++;
                            if (i % 997 == 0) GC.Collect();
                        }
                        lock (new object()) { }  // an entry that dies right away
                    }
                });
                threads[t].Start();
            }
            foreach (var th in threads) th.Join();
            foreach (var c in counts) total += c;
            gates = null!;
            GC.Collect();
        }
        Console.WriteLine("locked " + total);

        // Producer/consumer over Wait/Pulse with a bounded buffer.
        var gate = new object();
        var buffer = new Queue<int>();
        long consumed = 0;
        var producer = new Thread(() =>
        {
            for (int i = 1; i <= 20000; i++)
                lock (gate)
                {
                    while (buffer.Count == 8) Monitor.Wait(gate);
                    buffer.Enqueue(i);
                    Monitor.PulseAll(gate);
                }
        });
        var consumer = new Thread(() =>
        {
            for (int i = 0; i < 20000; i++)
                lock (gate)
                {
                    while (buffer.Count == 0) Monitor.Wait(gate);
                    consumed += buffer.Dequeue();
                    Monitor.PulseAll(gate);
                }
        });
        producer.Start(); consumer.Start(); producer.Join(); consumer.Join();
        Console.WriteLine("consumed " + consumed);

        // Wait with a timeout, recursion restored after Wait, Pulse with no waiter.
        lock (o)
        {
            lock (o)
            {
                bool pulsed = Monitor.Wait(o, 10);
                Console.WriteLine("timed wait " + pulsed + ", still entered " + Monitor.IsEntered(o));
                Monitor.Pulse(o);
            }
            Console.WriteLine("outer still entered " + Monitor.IsEntered(o));
        }

        // Interrupt ends a Wait with ThreadInterruptedException, after the monitor is taken back. An interrupt of a thread that
        // has not started yet is kept until it first blocks, so it lands on this Wait whatever the timing.
        string? interrupted = null;
        var sleeper = new Thread(() =>
        {
            lock (o)
            {
                try { Monitor.Wait(o); }
                catch (ThreadInterruptedException) { interrupted = "interrupted, entered " + Monitor.IsEntered(o); }
            }
        });
        sleeper.Interrupt();
        sleeper.Start();
        sleeper.Join();
        Console.WriteLine(interrupted);

        // System.Threading.Lock and Monitor on the same object stay separate locks.
        var l = new Lock();
        using (l.EnterScope())
        {
            Console.WriteLine("lock held " + l.IsHeldByCurrentThread + ", monitor " + Monitor.IsEntered(l));
        }
    }
}
