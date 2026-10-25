// ExecutionContext.Capture/Run/Restore with AsyncLocal values, SynchronizationContext's defaults,
// GC.AllocateUninitializedArray, MemoryMarshal.CreateFromPinnedArray/AsMemory, UnixDomainSocketEndPoint.
#nullable disable
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    static readonly AsyncLocal<string> Local = new AsyncLocal<string>();

    static void Main()
    {
        Local.Value = "captured";
        var context = ExecutionContext.Capture();
        Local.Value = "changed";
        ExecutionContext.Run(context, s => Console.WriteLine($"{s}: {Local.Value}"), "inside Run");
        Console.WriteLine($"after Run: {Local.Value}");
        ExecutionContext.Restore(context);
        Console.WriteLine($"after Restore: {Local.Value} {ExecutionContext.IsFlowSuppressed()}");
        try { ExecutionContext.Run(null, _ => { }, null); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

        Console.WriteLine(SynchronizationContext.Current == null);
        var sync = new SynchronizationContext();
        using var done = new ManualResetEventSlim();
        sync.Post(s => { Console.WriteLine($"posted {s}"); done.Set(); }, 1);
        done.Wait();
        sync.Send(s => Console.WriteLine($"sent {s}"), 2);
        Console.WriteLine(sync.CreateCopy().GetType().Name);

        var array = GC.AllocateUninitializedArray<byte>(16, pinned: true);
        var pinned = GC.AllocateArray<int>(4, pinned: true);
        Console.WriteLine($"{array.Length} {pinned.Length} {pinned[0]}");
        var memory = MemoryMarshal.CreateFromPinnedArray(array, 4, 8);
        memory.Span[0] = 42;
        Console.WriteLine($"{memory.Length} {array[4]} {MemoryMarshal.CreateFromPinnedArray<byte>(null, 0, 0).IsEmpty}");
        ReadOnlyMemory<byte> readOnly = memory;
        var writable = MemoryMarshal.AsMemory(readOnly);
        writable.Span[1] = 7;
        Console.WriteLine($"{array[5]} {writable.Length}");

        var endPoint = new UnixDomainSocketEndPoint("/tmp/csharpc.sock");
        Console.WriteLine($"{endPoint} {endPoint.AddressFamily} {endPoint.Equals(new UnixDomainSocketEndPoint("/tmp/csharpc.sock"))}");
        try { new UnixDomainSocketEndPoint(""); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
    }
}
