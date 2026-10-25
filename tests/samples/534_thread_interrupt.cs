// Thread.Interrupt: a thread blocked in Sleep, Monitor.Wait or a wait handle wakes with
// ThreadInterruptedException (Join is not interruptible on Unix); an interrupt sent before the thread
// blocks waits for its next wait
// (docs-standard-threading-pausing-and-resuming-threads-InterruptThread).
using System;
using System.Threading;

static void Run(string name, Action wait)
{
    var started = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
        Console.WriteLine($"{name}: about to block");
        started.Set();
        try { wait(); Console.WriteLine($"{name}: returned"); }
        catch (ThreadInterruptedException e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
        finally { Console.WriteLine($"{name}: finally"); }
    });
    thread.Name = name;
    thread.Start();
    started.Wait();
    Thread.Sleep(100); // let it block
    thread.Interrupt();
    thread.Join();
    Console.WriteLine($"{name}: joined, alive={thread.IsAlive}");
}

Run("sleep", () => Thread.Sleep(Timeout.Infinite));
Run("sleep-timeout", () => Thread.Sleep(60_000));
var gate = new object();
Run("monitor-wait", () => { lock (gate) { Monitor.Wait(gate); Console.WriteLine("monitor-wait: owns the lock again: " + Monitor.IsEntered(gate)); } });
Run("monitor-wait-held", () =>
{
    lock (gate)
    {
        try { Monitor.Wait(gate); }
        finally { Console.WriteLine("monitor-wait-held: owns the lock in finally: " + Monitor.IsEntered(gate)); }
    }
});
var never = new ManualResetEvent(false);
Run("event", () => never.WaitOne());
Run("event-timeout", () => Console.WriteLine("event-timeout: " + never.WaitOne(60_000)));
// Join doesn't observe the interrupt; the Sleep after it does.
var other = new Thread(() => Thread.Sleep(400));
other.Start();
Run("join", () => { other.Join(); Console.WriteLine($"join: returned, other alive={other.IsAlive}"); Thread.Sleep(10); });

// An interrupt while running is delivered at the next blocking call.
var ready = new ManualResetEventSlim();
var go = new ManualResetEventSlim();
var later = new Thread(() =>
{
    ready.Set();
    while (!go.IsSet) Thread.SpinWait(100);
    Console.WriteLine("later: still running after Interrupt");
    try { Thread.Sleep(10); }
    catch (ThreadInterruptedException) { Console.WriteLine("later: interrupted at the next Sleep"); }
    Thread.Sleep(10);
    Console.WriteLine("later: a second Sleep is not interrupted");
});
later.Start();
ready.Wait();
later.Interrupt();
go.Set();
later.Join();

// The main thread is interruptible too.
Thread.CurrentThread.Interrupt();
try { Thread.Sleep(0); }
catch (ThreadInterruptedException) { Console.WriteLine("main: Sleep(0) after its own Interrupt throws"); }
Thread.Sleep(1);
Console.WriteLine("main: done");
