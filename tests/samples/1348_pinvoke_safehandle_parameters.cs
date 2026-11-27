#:property AllowUnsafeBlocks=true
// SafeHandle arguments of P/Invokes, as .NET's marshaller passes them: the handle's value, add-ref'ed for the call and
// released after it; a null handle is an ArgumentNullException ("pHandle") and a closed one an ObjectDisposedException,
// thrown before the call, and a handle that already got through is not left add-ref'ed.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

unsafe
{
    var handle = new FdHandle(Native.open("/dev/null", 0));
    Console.WriteLine($"invalid {handle.IsInvalid} closed {handle.IsClosed}");
    byte b;
    Console.WriteLine($"read {Native.read(handle, &b, 1)} lseek {Native.lseek(handle, 0, 1)}");

    // The calls released what they add-ref'ed: the handle is still open and takes another reference.
    bool added = false;
    handle.DangerousAddRef(ref added);
    handle.DangerousRelease();
    Console.WriteLine($"add-ref after calls {added}");

    try { Native.read(null!, &b, 1); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

    handle.Dispose();
    Console.WriteLine($"closed {handle.IsClosed} released {FdHandle.Released}");
    try { Native.read(handle, &b, 1); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

    var first = new FdHandle(Native.open("/dev/null", 0));
    try { Native.lseek2(first, null!); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    first.Dispose();
    Console.WriteLine($"first closed {first.IsClosed} released {FdHandle.Released}");

    var missing = new FdHandle(Native.open("/nonexistent/csharpc", 0));
    Console.WriteLine($"missing invalid {missing.IsInvalid}");
    missing.Dispose();
    Console.WriteLine($"released {FdHandle.Released}");
}

sealed class FdHandle : SafeHandleMinusOneIsInvalid
{
    public static int Released;
    public FdHandle(int fd) : base(true) { SetHandle(fd); }
    protected override bool ReleaseHandle() { Released++; return Native.close((int)handle) == 0; }
}

static unsafe class Native
{
    [DllImport("libc")] public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc")] public static extern nint read(FdHandle fd, byte* buffer, nuint count);
    [DllImport("libc")] public static extern long lseek(FdHandle fd, long offset, int whence);
    // lseek's signature with two handles, only to show the second one's null check after the first got through.
    [DllImport("libc", EntryPoint = "lseek")] public static extern long lseek2(FdHandle fd, FdHandle other);
    [DllImport("libc")] public static extern int close(int fd);
}
