#:property AllowUnsafeBlocks=true
// ObjectiveCMarshal reference tracking, as the .NET for macOS bindings use it: Initialize registers the native
// callbacks, a class with [ObjectiveCTrackedType] and a finalizer gets tagged memory and reference-tracking handles,
// each collection asks isReferenced(taggedMemory) for the tracked objects nothing else reaches (an object the
// native side still references survives), and an object that enters the finalizer queue is announced to
// trackedObjectEnteredFinalization before its finalizer runs. The callbacks run while the collector has stopped the
// world, so they must be native code (a managed [UnmanagedCallersOnly] callback aborts real .NET): libc's strlen
// reads "referenced" off the tagged memory (a non-zero first byte), time(tagged) is the entered-finalization callback
// (it stores the time into the tagged memory, which a finalizer can see) and getpid stands for the begin/end callback.
// Real .NET runs this only on macOS (elsewhere every member is a PlatformNotSupportedException, sample 970), so the
// other platforms print a line and stop; macOS records the expectation live (tests/platform-skips.txt). Results are
// counted over many objects, as in the finalizer samples: the collector is conservative.
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
    IntPtr libc = NativeLibrary.Load("/usr/lib/libSystem.dylib");
    var beginEnd = (delegate* unmanaged<void>)NativeLibrary.GetExport(libc, "getpid");
    var isReferenced = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(libc, "strlen");
    var entered = (delegate* unmanaged<IntPtr, void>)NativeLibrary.GetExport(libc, "time");
    try { ObjectiveCMarshal.Initialize(null, isReferenced, entered, Propagate); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    ObjectiveCMarshal.Initialize(beginEnd, isReferenced, entered, Propagate);
    try { ObjectiveCMarshal.Initialize(beginEnd, isReferenced, entered, Propagate); }
    catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
}

try { ObjectiveCMarshal.CreateReferenceTrackingHandle(new Untracked(), out _); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { ObjectiveCMarshal.CreateReferenceTrackingHandle(null!, out _); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

var handles = new GCHandle[N];
var memories = new IntPtr[N];
Make();

// Even ids stay referenced from the "native side" (a non-zero first byte of the tagged memory); odd ones don't.
Drain();
Console.WriteLine($"native-referenced objects survived: {AliveHandles() >= N / 2 * 3 / 4}, their finalizers did not run: {Finalized(even: true) == 0}");
Console.WriteLine($"the others were finalized: {Finalized(even: false) >= N / 2 * 3 / 4}, announced first: {Announced(even: false) >= N / 2 * 3 / 4}");

// The native side lets go of the even ones: the next collections finalize them too.
unsafe { for (int i = 0; i < N; i += 2) ((byte*)memories[i])[0] = 0; }
Drain();
Console.WriteLine($"after the native side let go, they were finalized: {Finalized(even: true) >= N / 2 * 3 / 4}, announced first: {Announced(even: true) >= N / 2 * 3 / 4}");

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
            ((byte*)memories[i])[0] = (byte)(i % 2 == 0 ? 'x' : 0);
        }
        o.Tagged = memories[i];
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
static int Finalized(bool even) { int n = 0; lock (Tracked.Log) foreach (var (id, _) in Tracked.Log) if ((id % 2 == 0) == even) n++; return n; }
static int Announced(bool even) { int n = 0; lock (Tracked.Log) foreach (var (id, announced) in Tracked.Log) if ((id % 2 == 0) == even && announced) n++; return n; }

static unsafe delegate* unmanaged<IntPtr, void> Propagate(Exception exception, RuntimeMethodHandle lastMethod, out IntPtr context)
{
    context = IntPtr.Zero;
    return null;
}

[ObjectiveCTrackedType]
unsafe class Tracked
{
    public static readonly List<(int, bool)> Log = new();
    readonly int _id;
    public IntPtr Tagged;
    public Tracked(int id) { _id = id; }
    // trackedObjectEnteredFinalization (time) stored the time into the first word of the tagged memory before this runs.
    ~Tracked() { lock (Log) Log.Add((_id, Tagged != IntPtr.Zero && ((long*)Tagged)[0] > 1_000_000_000L)); }
}

class Untracked { ~Untracked() { } }
