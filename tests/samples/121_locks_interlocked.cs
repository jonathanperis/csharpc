// lock / Monitor / System.Threading.Lock / Interlocked / Volatile with exact final results.
using System.Threading;

var counter = new Counter();
RunAll(8, () => { for (int i = 0; i < 100_000; i++) counter.Add(); });
Console.WriteLine($"lock counter: {counter.Value}");

long atomic = 0;
int ints = 0;
RunAll(8, () =>
{
    for (int i = 0; i < 100_000; i++)
    {
        Interlocked.Increment(ref atomic);
        Interlocked.Add(ref ints, 2);
    }
});
Console.WriteLine($"interlocked: {atomic} {ints}");

int casTotal = 0;
RunAll(4, () =>
{
    for (int i = 0; i < 50_000; i++)
    {
        int seen, next;
        do { seen = Volatile.Read(ref casTotal); next = seen + 3; }
        while (Interlocked.CompareExchange(ref casTotal, next, seen) != seen);
    }
});
Console.WriteLine($"cas: {casTotal}");

string? slot = null;
Console.WriteLine(Interlocked.Exchange(ref slot, "first") ?? "null");
Console.WriteLine(Interlocked.CompareExchange(ref slot, "second", "first"));
Console.WriteLine(slot);
Console.WriteLine(Interlocked.Decrement(ref ints));

var gate = new Lock();
int lockTotal = 0;
RunAll(4, () =>
{
    for (int i = 0; i < 50_000; i++)
        lock (gate) { lockTotal++; }
});
Console.WriteLine($"Lock: {lockTotal} held={gate.IsHeldByCurrentThread}");

object sync = new();
Console.WriteLine(Monitor.IsEntered(sync));
lock (sync)
{
    lock (sync) Console.WriteLine($"reentrant: {Monitor.IsEntered(sync)}");
}
Console.WriteLine(Monitor.TryEnter(sync));
Monitor.Exit(sync);
try { Monitor.Exit(sync); }
catch (SynchronizationLockException e) { Console.WriteLine(e.GetType().Name); }

var flag = new Flag();
var setter = new Thread(() => { Thread.Sleep(20); flag.Set(); });
setter.Start();
while (!flag.IsSet) Thread.Yield();
setter.Join();
Console.WriteLine("volatile flag observed");

static void RunAll(int count, Action work)
{
    var threads = new Thread[count];
    for (int i = 0; i < count; i++) { threads[i] = new Thread(() => work()); threads[i].Start(); }
    foreach (var t in threads) t.Join();
}

class Counter
{
    private readonly object _gate = new();
    private int _value;
    public void Add() { lock (_gate) _value++; }
    public int Value { get { lock (_gate) return _value; } }
}

class Flag
{
    private volatile bool _set;
    public void Set() => _set = true;
    public bool IsSet => _set;
}
