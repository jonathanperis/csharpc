// Synchronization primitives' throughput, the program behind the ManualResetEventSlim / SpinWait / CountdownEvent / Barrier row of
// docs/il-first/System.Threading.md (#414): uncontended Set/Reset/IsSet, a cross-thread ping-pong over two events, CountdownEvent
// rounds across threads, Barrier phases, SpinWait.SpinUntil hand-offs. The checksum counts operations only.
using System;
using System.Threading;

class Program
{
    static void Main()
    {
        long check = 0;

        var ev = new ManualResetEventSlim();
        for (int i = 0; i < 5_000_000; i++) { ev.Set(); if (ev.IsSet) check++; ev.Reset(); }

        var ping = new ManualResetEventSlim();
        var pong = new ManualResetEventSlim();
        const int rounds = 100_000;
        var other = new Thread(() => { for (int i = 0; i < rounds; i++) { ping.Wait(); ping.Reset(); pong.Set(); } });
        other.Start();
        for (int i = 0; i < rounds; i++) { ping.Set(); pong.Wait(); pong.Reset(); check++; }
        other.Join();

        for (int r = 0; r < 20_000; r++)
        {
            using var countdown = new CountdownEvent(2);
            var t = new Thread(() => { countdown.Signal(); countdown.Signal(); });
            if (r % 1000 == 0) { t.Start(); countdown.Wait(); t.Join(); }
            else { countdown.Signal(2); countdown.Wait(); }
            check += countdown.CurrentCount + 1;
        }

        using (var barrier = new Barrier(3))
        {
            var workers = new Thread[3];
            for (int w = 0; w < 3; w++) { workers[w] = new Thread(() => { for (int p = 0; p < 20_000; p++) barrier.SignalAndWait(); }); workers[w].Start(); }
            foreach (var w in workers) w.Join();
            check += barrier.CurrentPhaseNumber;
        }

        int turn = 0;
        var spinner = new Thread(() => { for (int i = 0; i < 50_000; i++) { SpinWait.SpinUntil(() => Volatile.Read(ref turn) == 1); Volatile.Write(ref turn, 0); } });
        spinner.Start();
        for (int i = 0; i < 50_000; i++) { SpinWait.SpinUntil(() => Volatile.Read(ref turn) == 0); Volatile.Write(ref turn, 1); check++; }
        spinner.Join();

        Console.WriteLine(check);
    }
}
