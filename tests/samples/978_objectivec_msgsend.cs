#:property AllowUnsafeBlocks=true
// ObjectiveCMarshal's objc_msgSend overrides: SetMessageSendCallback makes the P/Invokes of libobjc's objc_msgSend
// call the registered function instead (once per kind: setting it again is an InvalidOperationException), and an
// exception stored with SetMessageSendPendingException while that function runs is thrown by the P/Invoke when it
// returns. Real .NET runs this only on macOS (elsewhere every member is a PlatformNotSupportedException, sample 970),
// so the other platforms print a line and stop; macOS records the expectation live (tests/platform-skips.txt).
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
using System.Threading;

if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("not macOS");
    return;
}

unsafe
{
    IntPtr libobjc = NativeLibrary.Load("/usr/lib/libobjc.dylib");
    Native.Real = NativeLibrary.GetExport(libobjc, "objc_msgSend");
    try { ObjectiveCMarshal.SetMessageSendCallback((ObjectiveCMarshal.MessageSendFunction)99, Native.Real); }
    catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
    IntPtr mine = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&Native.MsgSend;
    ObjectiveCMarshal.SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction.MsgSend, mine);
    try { ObjectiveCMarshal.SetMessageSendCallback(ObjectiveCMarshal.MessageSendFunction.MsgSend, mine); }
    catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
}

IntPtr cls = Native.objc_getClass("NSObject");
IntPtr newSelector = Native.sel_registerName("new");
IntPtr hashSelector = Native.sel_registerName("hash");

IntPtr obj = Native.objc_msgSend(cls, newSelector);
Console.WriteLine($"[NSObject new] through the override: {obj != IntPtr.Zero}, calls {Native.Calls}");

Native.ThrowNext = true;
try
{
    Native.objc_msgSend(obj, hashSelector);
    Console.WriteLine("no exception");
}
catch (InvalidOperationException e)
{
    Console.WriteLine("pending exception thrown after the call: " + e.Message);
}
Console.WriteLine($"calls {Native.Calls}");

Native.objc_msgSend(obj, hashSelector);
Console.WriteLine($"the exception is thrown once: calls {Native.Calls}");

ObjectiveCMarshal.SetMessageSendPendingException(new InvalidOperationException("discarded"));
ObjectiveCMarshal.SetMessageSendPendingException(null);
Native.objc_msgSend(obj, hashSelector);
Console.WriteLine($"a pending exception set to null is discarded: calls {Native.Calls}");

static unsafe class Native
{
    public static IntPtr Real;
    public static int Calls;
    public static bool ThrowNext;

    [DllImport("/usr/lib/libobjc.dylib")] public static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.dylib")] public static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.dylib")] public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [UnmanagedCallersOnly]
    public static IntPtr MsgSend(IntPtr receiver, IntPtr selector)
    {
        Interlocked.Increment(ref Calls);
        if (ThrowNext)
        {
            ThrowNext = false;
            ObjectiveCMarshal.SetMessageSendPendingException(new InvalidOperationException("from the callback"));
        }
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)Real)(receiver, selector);
    }
}
