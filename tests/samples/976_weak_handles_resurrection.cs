// Weak references and GC handles around finalization: a short weak reference (WeakReference, GCHandleType.Weak) is
// cleared as soon as its target is unreachable, even though the target's finalizer still has to run (and may
// resurrect it); one that tracks resurrection (trackResurrection: true, GCHandleType.WeakTrackResurrection) is
// cleared only once the object is gone for good. Counted over many objects: the collector is conservative, so
// a stale word can keep a few alive.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

const int N = 60;
var shortRefs = new WeakReference[N];
var longRefs = new WeakReference[N];
var weakHandles = new GCHandle[N];
var longHandles = new GCHandle[N];

Make();
Drain();
Console.WriteLine($"short weak references after finalization: mostly cleared {AliveRefs(shortRefs) <= N / 4}");
Console.WriteLine($"long weak references after finalization: mostly alive {AliveRefs(longRefs) >= N * 3 / 4}");
Console.WriteLine($"weak handles: mostly cleared {AliveHandles(weakHandles) <= N / 4}, long handles mostly alive {AliveHandles(longHandles) >= N * 3 / 4}");
Console.WriteLine($"finalized: most {Resurrected.Count >= N * 3 / 4}");

Release();
Drain();
Console.WriteLine($"after the program lets go: long references mostly cleared {AliveRefs(longRefs) <= N / 4}, long handles {AliveHandles(longHandles) <= N / 4}");

foreach (var h in weakHandles) h.Free();
foreach (var h in longHandles) h.Free();
var plain = GCHandle.Alloc("text", GCHandleType.Weak);
Console.WriteLine($"{plain.IsAllocated} {plain.Target}");
plain.Target = "other";
Console.WriteLine(plain.Target);
plain.Free();
Console.WriteLine(plain.IsAllocated);
try { _ = plain.Target; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
var normal = GCHandle.Alloc(new object());
Console.WriteLine(normal.Target is not null);
normal.Free();

[MethodImpl(MethodImplOptions.NoInlining)]
void Make()
{
    for (int i = 0; i < N; i++)
    {
        var o = new Res();
        shortRefs[i] = new WeakReference(o, trackResurrection: false);
        longRefs[i] = new WeakReference(o, trackResurrection: true);
        weakHandles[i] = GCHandle.Alloc(o, GCHandleType.Weak);
        longHandles[i] = GCHandle.Alloc(o, GCHandleType.WeakTrackResurrection);
    }
}

[MethodImpl(MethodImplOptions.NoInlining)]
void Release() { for (int i = 0; i < Resurrected.Saved.Length; i++) Resurrected.Saved[i] = null; }

static void Drain()
{
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

static int AliveRefs(WeakReference[] refs) { int n = 0; foreach (var r in refs) if (r.IsAlive) n++; return n; }
static int AliveHandles(GCHandle[] handles) { int n = 0; foreach (var h in handles) if (h.Target != null) n++; return n; }

class Res
{
    ~Res() { Resurrected.Add(this); }
}

static class Resurrected
{
    // Plain array and Interlocked: nothing else (a list, a lock) keeps the objects reachable once the program lets go.
    public static readonly Res?[] Saved = new Res?[256];
    static int _count;
    public static int Count => Volatile.Read(ref _count);
    public static void Add(Res r) { Saved[Interlocked.Increment(ref _count) - 1] = r; }
}
