#:property AllowUnsafeBlocks=true
// System.Runtime.InteropServices.ObjectiveC.ObjectiveCMarshal, the hooks Microsoft.macOS / Microsoft.iOS call at
// startup (Initialize with [UnmanagedCallersOnly] callbacks and an exception-propagation handler, a
// reference-tracking handle for every NSObject). Off macOS every member is a PlatformNotSupportedException;
// the program is the issue's, compiled into a binary that links the bindings' calls.
#pragma warning disable CA1416
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;

static void Show(string name, Action action)
{
    try { action(); Console.WriteLine(name + ": ok"); }
    catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
}

unsafe
{
    Show("Initialize", () => ObjectiveCMarshal.Initialize(&BeginEnd, &IsReferenced, &EnteredFinalization, UnhandledException));
    Show("Initialize null", () => ObjectiveCMarshal.Initialize(null, null, null, null));
    Show("Initialize again", () => ObjectiveCMarshal.Initialize(&BeginEnd, &IsReferenced, &EnteredFinalization, UnhandledException));
}

Show("CreateReferenceTrackingHandle", () =>
{
    var handle = ObjectiveCMarshal.CreateReferenceTrackingHandle(new Tracked(), out Span<nint> taggedMemory);
    Console.WriteLine("tagged memory: " + taggedMemory.Length + " words, target alive: " + (handle.Target is Tracked));
    handle.Free();
});
Show("CreateReferenceTrackingHandle null", () => ObjectiveCMarshal.CreateReferenceTrackingHandle(null!, out _));
Show("CreateReferenceTrackingHandle untracked", () => ObjectiveCMarshal.CreateReferenceTrackingHandle(new Plain(), out _));
Show("GetOrCreateReferenceTrackingMemory", () => ObjectiveCMarshal.GetOrCreateReferenceTrackingMemory(new Tracked()));
Show("SetMessageSendCallback", () => ObjectiveCMarshal.SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction.MsgSend, 1));
Show("SetMessageSendCallback bad", () => ObjectiveCMarshal.SetMessageSendCallback((ObjectiveCMarshal.MessageSendFunction)9, 1));
Show("SetMessageSendPendingException", () => ObjectiveCMarshal.SetMessageSendPendingException(new InvalidOperationException("x")));
Show("SetMessageSendPendingException null", () => ObjectiveCMarshal.SetMessageSendPendingException(null));
Console.WriteLine("done");

[UnmanagedCallersOnly]
static void BeginEnd() { }

[UnmanagedCallersOnly]
static int IsReferenced(nint taggedMemory) => 1;

[UnmanagedCallersOnly]
static void EnteredFinalization(nint taggedMemory) { }

static unsafe delegate* unmanaged<nint, void> UnhandledException(Exception exception, RuntimeMethodHandle lastMethod, out nint context)
{
    context = 0;
    return null;
}

[ObjectiveCTrackedType]
class Tracked
{
    ~Tracked() { }
}

class Plain { }
