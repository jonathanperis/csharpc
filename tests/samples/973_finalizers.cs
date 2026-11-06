// Finalizers: ~T() runs on a finalizer thread after the collector finds the object unreachable, at most once;
// GC.SuppressFinalize cancels it; base finalizers run after the derived one; a finalizer may resurrect its
// object, and GC.ReRegisterForFinalize makes it run again; GC.WaitForPendingFinalizers returns once the queue
// is drained. Objects are created in non-inlined methods and in bulk, and results are counted rather than
// listed: the collector decides when each is found and the order is not part of the contract.
using System.Runtime.CompilerServices;
using System.Threading;
using static Logs;

const int N = 60;

Console.WriteLine("before: " + Log.Count);

Plain(N);
Drain();
Console.WriteLine($"plain: most finalized {Count("plain") >= N * 3 / 4}, none twice {Distinct("plain") == Count("plain")}");

Suppressed(N);
Drain();
Console.WriteLine($"suppressed: odd finalized {Count("odd") >= N / 2 * 3 / 4}, even finalized {Count("even")}");

Chain(N);
Drain();
Console.WriteLine($"chain: most finalized {Count("D") >= N * 3 / 4}, base always after derived {ChainOrdered()}");

Revive(40);
Drain();
Console.WriteLine($"resurrected: most survived {Revived.Count >= 30}");
Rescue();
Drain();
Console.WriteLine($"finalized again: {Revived.Count >= 70 && Revived.Count <= 80}");

Cloned(N);
Drain();
Console.WriteLine($"clones: originals and copies finalized {Count("clone") >= N * 2 * 3 / 4 && Count("clone") <= N * 2}");

Disposed(N);
Drain();
Console.WriteLine($"disposed: only the leaked one may report {Count("leak") <= 1}");

[MethodImpl(MethodImplOptions.NoInlining)] static void Plain(int n) { for (int i = 0; i < n; i++) _ = new PlainObject(i); }
[MethodImpl(MethodImplOptions.NoInlining)] static void Suppressed(int n)
{
    for (int i = 0; i < n; i++)
    {
        var o = new SuppressedObject(i);
        if (i % 2 == 0) GC.SuppressFinalize(o);
    }
}
[MethodImpl(MethodImplOptions.NoInlining)] static void Chain(int n) { for (int i = 0; i < n; i++) _ = new Derived(i); }
[MethodImpl(MethodImplOptions.NoInlining)] static void Revive(int n) { for (int i = 0; i < n; i++) _ = new Revived(i); }
[MethodImpl(MethodImplOptions.NoInlining)] static void Rescue()
{
    for (int i = 0; i < Revived.Saved.Length; i++)
    {
        var s = Revived.Saved[i];
        if (s == null) continue;
        Revived.Saved[i] = null;
        GC.ReRegisterForFinalize(s);
    }
}
[MethodImpl(MethodImplOptions.NoInlining)] static void Cloned(int n) { for (int i = 0; i < n; i++) _ = new CloneMe(i).Copy(); }
[MethodImpl(MethodImplOptions.NoInlining)] static void Disposed(int n)
{
    for (int i = 0; i < n; i++)
    {
        using var d = new Disposable();
    }
    _ = new Disposable();  // never disposed: its finalizer reports a leak
}

static void Drain()
{
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

static int Count(string prefix) { lock (Log) return Log.Count(l => l.StartsWith(prefix)); }
static int Distinct(string prefix) { lock (Log) return Log.Where(l => l.StartsWith(prefix)).Distinct().Count(); }
static bool ChainOrdered()
{
    lock (Log)
    {
        for (int i = 0; i < N; i++)
        {
            int d = Log.IndexOf("D" + i), b = Log.IndexOf("B" + i);
            if ((d >= 0) != (b >= 0) || (d >= 0 && d > b)) return false;
        }
        return true;
    }
}


class PlainObject { readonly int _id; public PlainObject(int id) { _id = id; } ~PlainObject() { lock (Log) Log.Add("plain" + _id); } }
class SuppressedObject
{
    readonly int _id;
    public SuppressedObject(int id) { _id = id; }
    ~SuppressedObject() { lock (Log) Log.Add((_id % 2 == 0 ? "even" : "odd") + _id); }
}
class Base
{
    protected readonly int Id;
    protected Base(int id) { Id = id; }
    ~Base() { lock (Log) Log.Add("B" + Id); }
}
class Derived : Base
{
    public Derived(int id) : base(id) { }
    ~Derived() { lock (Log) Log.Add("D" + Id); }
}
class Revived
{
    // Resurrection: the finalizer stores `this` where the program can reach it. Plain array and Interlocked,
    // so nothing else (a list, a lock) keeps these objects reachable after the program lets go of them.
    public static readonly Revived?[] Saved = new Revived?[256];
    static int _count;
    public static int Count => Volatile.Read(ref _count);
    public Revived(int id) { }
    ~Revived() { Saved[Interlocked.Increment(ref _count) - 1] = this; }
}
class CloneMe
{
    readonly int _id;
    public CloneMe(int id) { _id = id; }
    public CloneMe Copy() => (CloneMe)MemberwiseClone();  // the copy is finalizable too
    ~CloneMe() { lock (Log) Log.Add("clone" + _id); }
}
class Disposable : IDisposable
{
    bool _disposed;
    public void Dispose() { _disposed = true; GC.SuppressFinalize(this); }
    ~Disposable() { if (!_disposed) lock (Log) Log.Add("leak"); }
}

static class Logs { public static readonly List<string> Log = new(); }
