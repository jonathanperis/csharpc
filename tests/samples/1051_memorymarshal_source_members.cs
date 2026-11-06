#:property AllowUnsafeBlocks=true
// System.Runtime.InteropServices.MemoryMarshal from source: references to span and array data, spans over
// references, casts, Read/Write of unmanaged values, array/string/memory-manager queries, null-terminated spans.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Assigned only through memory in this program.
#pragma warning disable CS0649
struct Pair { public int A; public int B; }
struct WithRef { public string Text; }
#pragma warning restore CS0649

unsafe class P
{
    static void Try(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        int[] arr = { 1, 2, 3, 4, 5, 6 };
        Span<int> span = arr;
        ReadOnlySpan<int> ro = arr;
        Console.WriteLine(MemoryMarshal.GetReference(span));
        Console.WriteLine(MemoryMarshal.GetReference(ro));
        MemoryMarshal.GetReference(span) = 100;
        Console.WriteLine(arr[0]);
        Console.WriteLine(MemoryMarshal.GetArrayDataReference(arr));
        Console.WriteLine(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(Span<int>.Empty)));
        var cs = MemoryMarshal.CreateSpan(ref arr[1], 3);
        Console.WriteLine(string.Join(",", cs.ToArray()));
        var cro = MemoryMarshal.CreateReadOnlySpan(ref arr[2], 2);
        Console.WriteLine(string.Join(",", cro.ToArray()));
        var bytes = MemoryMarshal.AsBytes(span);
        Console.WriteLine(bytes.Length);
        Console.WriteLine(bytes[0]);
        var robytes = MemoryMarshal.AsBytes(ro);
        Console.WriteLine(robytes.Length);
        var longs = MemoryMarshal.Cast<int, long>(span);
        Console.WriteLine($"{longs.Length} {longs[0]:X16}");
        var back = MemoryMarshal.Cast<byte, int>(bytes);
        Console.WriteLine(back.Length);
        var rocast = MemoryMarshal.Cast<int, short>(ro);
        Console.WriteLine(rocast.Length);

        byte[] data = { 1, 0, 0, 0, 2, 0, 0, 0, 9 };
        Console.WriteLine(MemoryMarshal.Read<int>(data));
        Console.WriteLine(MemoryMarshal.Read<Pair>(data).B);
        Console.WriteLine(MemoryMarshal.TryRead<Pair>(data, out var pr));
        Console.WriteLine(pr.B);
        Console.WriteLine(MemoryMarshal.TryRead<long>(new byte[3], out var lr));
        Console.WriteLine(lr);
        Try("Read short", () => MemoryMarshal.Read<long>(new byte[3]));
        byte[] outb = new byte[8];
        MemoryMarshal.Write(outb, in pr);
        Console.WriteLine(string.Join(",", outb));
        Console.WriteLine(MemoryMarshal.TryWrite(outb, in pr));
        Console.WriteLine(MemoryMarshal.TryWrite(new byte[4], in pr));
        long lv = 0x0102030405060708;
        MemoryMarshal.Write(outb, in lv);
        Console.WriteLine(string.Join(",", outb));

        ReadOnlyMemory<int> rom = arr;
        Console.WriteLine(MemoryMarshal.TryGetArray(rom, out ArraySegment<int> seg));
        Console.WriteLine($"{seg.Offset} {seg.Count}");
        Memory<int> mem = arr.AsMemory(2, 3);
        Console.WriteLine(MemoryMarshal.TryGetArray<int>(mem, out var seg2));
        Console.WriteLine($"{seg2.Offset} {seg2.Count}");
        Console.WriteLine(string.Join(",", MemoryMarshal.ToEnumerable<int>(rom)));
        Console.WriteLine(MemoryMarshal.AsMemory(rom).Length);
        var am = MemoryMarshal.CreateFromPinnedArray(arr, 1, 2);
        Console.WriteLine(am.Length);
        string heap = new string('a', 2) + "bcdef";
        Console.WriteLine(MemoryMarshal.TryGetString(heap.AsMemory(1, 3), out var s, out int start, out int length));
        Console.WriteLine($"{s} {start} {length}");
        Console.WriteLine(MemoryMarshal.TryGetString(default, out s, out start, out length));
        Console.WriteLine(MemoryMarshal.TryGetString(new char[3], out s, out start, out length));
        Console.WriteLine(MemoryMarshal.TryGetMemoryManager<int, System.Buffers.MemoryManager<int>>(rom, out var mgr));
        Console.WriteLine(string.Join(",", MemoryMarshal.Cast<char, byte>("ab".AsSpan()).ToArray()));
        ref char c0 = ref MemoryMarshal.GetReference("xyz".AsSpan());
        Console.WriteLine(c0);
        Console.WriteLine(Unsafe.As<byte, int>(ref MemoryMarshal.GetArrayDataReference((Array)arr)));

        // AsRef over a byte span, spans over null-terminated data
        byte[] raw = new byte[8];
        MemoryMarshal.AsRef<Pair>(raw).B = 77;
        Console.WriteLine($"{raw[4]} {MemoryMarshal.AsRef<Pair>((ReadOnlySpan<byte>)raw).B}");
        Try("AsRef short", () => MemoryMarshal.AsRef<long>(new byte[4]));
        byte[] z = { 65, 66, 67, 0, 68 };
        fixed (byte* zp = z) Console.WriteLine(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(zp).Length);
        char[] zc = { 'h', 'i', '\0', 'x' };
        fixed (char* zcp = zc) Console.WriteLine(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(zcp).ToString());
        Console.WriteLine(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)null).Length);
        // GetArrayDataReference on a multi-dimensional array and through the Array overload
        int[,] grid = { { 1, 2 }, { 3, 4 } };
        Console.WriteLine(Unsafe.Add(ref Unsafe.As<byte, int>(ref MemoryMarshal.GetArrayDataReference((Array)grid)), 3));
        Console.WriteLine(Unsafe.As<byte, int>(ref MemoryMarshal.GetArrayDataReference((Array)new[] { 7, 8 })));
        Try("null array", () => MemoryMarshal.GetArrayDataReference((int[])null));
        Console.WriteLine(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(default(ReadOnlySpan<int>))));
        // Spans over a ref, written through
        int local = 5;
        var one = MemoryMarshal.CreateSpan(ref local, 1);
        one[0] = 6;
        Console.WriteLine(local);
        Console.WriteLine(MemoryMarshal.CreateSpan(ref local, -1).Length);
        Console.WriteLine(MemoryMarshal.CreateReadOnlySpan(ref local, -2).Length);
        Try("references", () => MemoryMarshal.AsBytes(new WithRef[1].AsSpan()));
    }
}
