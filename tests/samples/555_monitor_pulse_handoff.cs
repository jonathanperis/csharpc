// Monitor.Wait/Pulse handoffs under contention: a bounded buffer on two SemaphoreSlims (Monitor-based),
// with cancellation registrations around the waits as BlockingCollection makes them. A pulse that met a
// waiter's poll-slice timeout used to be lost, leaving a producer waiting with free slots (a hang).
using System;
using System.Threading;
using System.Threading.Tasks;

long total = 0;
for (int iter = 0; iter < 40; iter++)
{
    using var cts = new CancellationTokenSource();
    var free = new SemaphoreSlim(4);
    var used = new SemaphoreSlim(0);
    long sum = 0;
    var tasks = new Task[5];
    for (int p = 0; p < 3; p++)
    {
        int producer = p;
        tasks[p] = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                using (cts.Token.Register(static () => { })) free.Wait(cts.Token);
                Interlocked.Add(ref sum, producer * 1000 + i);
                used.Release();
            }
        });
    }
    for (int c = 3; c < 5; c++)
        tasks[c] = Task.Run(() =>
        {
            for (int i = 0; i < 150; i++)
            {
                using (cts.Token.Register(static () => { })) used.Wait(cts.Token);
                free.Release();
            }
        });
    if (!Task.WaitAll(tasks, 20000)) { Console.WriteLine($"iteration {iter}: stuck"); return; }
    total += sum;
}
Console.WriteLine($"40 rounds, total {total}");

// Pulse wakes the oldest waiter only; PulseAll wakes the rest.
var gate = new object();
int woken = 0, waiting = 0;
var waiters = new Thread[3];
for (int i = 0; i < waiters.Length; i++)
{
    // `waiting` counts up under the lock, which a waiter only gives up inside Monitor.Wait.
    waiters[i] = new Thread(() => { lock (gate) { waiting++; Monitor.Wait(gate); woken++; } });
    waiters[i].Start();
}
while (true) { lock (gate) { if (waiting == 3) break; } Thread.Sleep(5); }
lock (gate) Monitor.Pulse(gate);
while (Volatile.Read(ref woken) < 1) Thread.Sleep(5);
Thread.Sleep(50);
Console.WriteLine($"after Pulse: {Volatile.Read(ref woken)} woken");
lock (gate) Monitor.PulseAll(gate);
foreach (var t in waiters) t.Join();
Console.WriteLine($"after PulseAll: {woken} woken");
lock (gate) Console.WriteLine($"timed wait without a pulse: {Monitor.Wait(gate, 20)}");

