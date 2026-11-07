// Producer/consumer with Monitor.Wait/Pulse, plus wait handles.
using System.Threading;

var queue = new BoundedQueue(4);
long consumed = 0;
var consumer = new Thread(() =>
{
    while (true)
    {
        int item = queue.Take();
        if (item < 0) break;
        consumed += item;
    }
});
consumer.Start();
for (int i = 1; i <= 10_000; i++) queue.Add(i);
queue.Add(-1);
consumer.Join();
Console.WriteLine($"consumed {consumed}");

var ready = new ManualResetEventSlim(false);
int observed = 0;
var waiter = new Thread(() => { ready.Wait(); observed = 7; });
waiter.Start();
Thread.Sleep(20);
Console.WriteLine($"before set: {observed} {ready.IsSet}");
ready.Set();
waiter.Join();
Console.WriteLine($"after set: {observed} {ready.IsSet}");

// The first two workers to get a permit wait for each other inside the section, so two are inside at
// once whatever the scheduler does (a fixed sleep would only make that likely on a loaded machine).
var semaphore = new SemaphoreSlim(2);
int inside = 0, maxInside = 0, entered = 0;
var rendezvous = new CountdownEvent(2);
object gate = new();
var workers = new Thread[6];
for (int i = 0; i < workers.Length; i++)
{
    workers[i] = new Thread(() =>
    {
        semaphore.Wait();
        lock (gate) { inside++; if (inside > maxInside) maxInside = inside; }
        if (Interlocked.Increment(ref entered) <= 2) { rendezvous.Signal(); rendezvous.Wait(); }
        Thread.Sleep(10);
        lock (gate) inside--;
        semaphore.Release();
    });
    workers[i].Start();
}
foreach (var w in workers) w.Join();
Console.WriteLine($"max concurrent: {maxInside} free: {semaphore.CurrentCount}");

var countdown = new CountdownEvent(3);
for (int i = 0; i < 3; i++) new Thread(() => countdown.Signal()).Start();
countdown.Wait();
Console.WriteLine($"countdown done: {countdown.IsSet}");

// An AutoResetEvent that is already set stays set: a second Set before the waiter consumed the first is
// absorbed. Each Set therefore waits for the waiter's acknowledgement instead of for a fixed sleep, which
// a loaded machine can outlast (the waiter then missed a signal and blocked forever).
var auto = new AutoResetEvent(false);
var ack = new AutoResetEvent(false);
int handoffs = 0;
var ponger = new Thread(() => { for (int i = 0; i < 3; i++) { auto.WaitOne(); handoffs++; ack.Set(); } });
ponger.Start();
for (int i = 0; i < 3; i++) { auto.Set(); ack.WaitOne(); }
ponger.Join();
Console.WriteLine($"handoffs: {handoffs}");

// The same handshake many times over: every Set wakes a waiter that is about to block or already blocked.
int pings = 0, pongs = 0;
var ping = new AutoResetEvent(false);
var pong = new AutoResetEvent(false);
var echo = new Thread(() => { for (int i = 0; i < 2000; i++) { ping.WaitOne(); pings++; pong.Set(); } });
echo.Start();
for (int i = 0; i < 2000; i++) { ping.Set(); pong.WaitOne(); pongs++; }
echo.Join();
Console.WriteLine($"ping-pong: {pings} {pongs}");
Console.WriteLine(Monitor.Wait(gate, 0) ? "?" : "no wait outside lock?");

class BoundedQueue(int capacity)
{
    private readonly int[] _items = new int[capacity];
    private int _head, _count;
    private readonly object _gate = new();

    public void Add(int item)
    {
        lock (_gate)
        {
            while (_count == _items.Length) Monitor.Wait(_gate);
            _items[(_head + _count) % _items.Length] = item;
            _count++;
            Monitor.PulseAll(_gate);
        }
    }

    public int Take()
    {
        lock (_gate)
        {
            while (_count == 0) Monitor.Wait(_gate);
            int item = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            Monitor.PulseAll(_gate);
            return item;
        }
    }
}
