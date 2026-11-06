#:property AllowUnsafeBlocks=true
// ObjectiveCMarshal reference tracking, as the .NET for macOS bindings use it: Initialize registers the native
// callbacks, a class with [ObjectiveCTrackedType] and a finalizer gets tagged memory and reference-tracking handles,
// each collection asks isReferenced(taggedMemory) for the tracked objects nothing else reaches (an object the
// native side still references survives), and an object that enters the finalizer queue is announced to
// trackedObjectEnteredFinalization before its finalizer runs. Real .NET runs this only on macOS (elsewhere every
// member is a PlatformNotSupportedException, sample 970), so the other platforms print a line and stop; macOS
// records the expectation live (tests/platform-skips.txt). Results are counted over many objects, as in the
// finalizer samples: the collector is conservative.
#pragma warning disable CA1416
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
using System.Threading;

if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("not macOS");
    return;
}

const int N = 40;

try { ObjectiveCMarshal.GetOrCreateReferenceTrackingMemory(new Tracked(-1)); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

unsafe
{
    try { ObjectiveCMarshal.Initialize(null, &Native.IsReferenced, &Native.Entered, Native.Propagate); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    ObjectiveCMarshal.Initialize(&Native.BeginEnd, &Native.IsReferenced, &Native.Entered, Native.Propagate);
    try { ObjectiveCMarshal.Initialize(&Native.BeginEnd, &Native.IsReferenced, &Native.Entered, Native.Propagate); }
    catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
}

try { ObjectiveCMarshal.CreateReferenceTrackingHandle(new Untracked(), out _); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { ObjectiveCMarshal.CreateReferenceTrackingHandle(null!, out _); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

var handles = new GCHandle[N];
var memories = new IntPtr[N];
Make();

// Even ids stay referenced from the "native side" (a non-zero first tagged word); odd ones don't.
Drain();
int beginEnd = Volatile.Read(ref Native.BeginEndCalls);
Console.WriteLine($"callbacks bracket each collection: {beginEnd >= 2 && beginEnd % 2 == 0}");
Console.WriteLine($"isReferenced asked about the tracked objects: {Volatile.Read(ref Native.Asked) >= N / 2}");
Console.WriteLine($"native-referenced objects survived: {AliveHandles() >= N / 2 * 3 / 4}, their finalizers did not run: {Native.Finalized(even: true) == 0}");
Console.WriteLine($"the others were finalized: {Native.Finalized(even: false) >= N / 2 * 3 / 4}, announced first: {Volatile.Read(ref Native.EnteredCalls) >= N / 2 * 3 / 4}");
Console.WriteLine($"tagged memory handed to the callbacks is the one the program got: {Volatile.Read(ref Native.WrongTagged) == 0}");

// The native side lets go of the even ones: the next collections finalize them too.
unsafe { for (int i = 0; i < N; i += 2) ((nint*)memories[i])[0] = 0; }
Drain();
Console.WriteLine($"after the native side let go, they were finalized: {Native.Finalized(even: true) >= N / 2 * 3 / 4}");

foreach (var h in handles) h.Free();

[MethodImpl(MethodImplOptions.NoInlining)]
void Make()
{
    for (int i = 0; i < N; i++)
    {
        var o = new Tracked(i);
        handles[i] = ObjectiveCMarshal.CreateReferenceTrackingHandle(o, out var memory);
        unsafe
        {
            fixed (IntPtr* p = memory) memories[i] = (IntPtr)p;
            memory[0] = i % 2 == 0 ? 1 : 0;
        }
        Native.Expected[i] = memories[i];
    }
}

static void Drain()
{
    for (int i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

int AliveHandles() { int n = 0; foreach (var h in handles) if (h.Target != null) n++; return n; }

static unsafe class Native
{
    public static int BeginEndCalls, Asked, EnteredCalls, WrongTagged;
    public static readonly IntPtr[] Expected = new IntPtr[64];
    public static readonly List<int> Log = new();

    public static int Finalized(bool even) { int n = 0; lock (Log) foreach (int id in Log) if ((id % 2 == 0) == even) n++; return n; }

    [UnmanagedCallersOnly]
    public static void BeginEnd() { Interlocked.Increment(ref BeginEndCalls); }

    [UnmanagedCallersOnly]
    public static int IsReferenced(IntPtr tagged)
    {
        Interlocked.Increment(ref Asked);
        return ((nint*)tagged)[0] != 0 ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    public static void Entered(IntPtr tagged)
    {
        Interlocked.Increment(ref EnteredCalls);
        bool known = false;
        for (int i = 0; i < Expected.Length; i++) if (Expected[i] == tagged) known = true;
        if (!known) Interlocked.Increment(ref WrongTagged);
    }

    public static delegate* unmanaged<IntPtr, void> Propagate(Exception exception, RuntimeMethodHandle lastMethod, out IntPtr context)
    {
        context = IntPtr.Zero;
        return null;
    }
}

[ObjectiveCTrackedType]
class Tracked
{
    readonly int _id;
    public Tracked(int id) { _id = id; }
    ~Tracked() { lock (Native.Log) Native.Log.Add(_id); }
}

class Untracked { ~Untracked() { } }
