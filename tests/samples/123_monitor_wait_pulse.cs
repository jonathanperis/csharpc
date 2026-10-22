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

var semaphore = new SemaphoreSlim(2);
int inside = 0, maxInside = 0;
object gate = new();
var workers = new Thread[6];
for (int i = 0; i < workers.Length; i++)
{
    workers[i] = new Thread(() =>
    {
        semaphore.Wait();
        lock (gate) { inside++; if (inside > maxInside) maxInside = inside; }
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

var auto = new AutoResetEvent(false);
int handoffs = 0;
var ponger = new Thread(() => { for (int i = 0; i < 3; i++) { auto.WaitOne(); handoffs++; } });
ponger.Start();
for (int i = 0; i < 3; i++) { auto.Set(); Thread.Sleep(10); }
ponger.Join();
Console.WriteLine($"handoffs: {handoffs}");
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
