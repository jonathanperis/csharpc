// MemoryMarshal.TryGetArray over large arrays allocated since the last collection, at addresses below
// older ones (the collector's large-object table is only sorted at collections; Kestrel's socket
// transport sends its 4 KiB pool blocks through TryGetArray and reset connections when it failed).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

static class Program
{
    static void Main()
    {
        var early = new List<byte[]>();
        for (int i = 0; i < 64; i++) early.Add(new byte[4096 + i]);
        var survivors = new List<byte[]>();
        for (int i = 0; i < 16; i++) survivors.Add(new byte[8192 + i]);
        early.Clear();
        GC.Collect();

        var fresh = new List<byte[]>();
        for (int i = 0; i < 48; i++) fresh.Add(GC.AllocateUninitializedArray<byte>(4096 + i, pinned: i % 2 == 0));
        int found = 0, offsetsRight = 0;
        foreach (var array in fresh)
        {
            ReadOnlyMemory<byte> memory = array.AsMemory(100, 200);
            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
            {
                found++;
                if (ReferenceEquals(segment.Array, array) && segment.Offset == 100 && segment.Count == 200) offsetsRight++;
            }
        }
        foreach (var array in survivors)
            if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)array.AsMemory(1), out ArraySegment<byte> segment) && ReferenceEquals(segment.Array, array))
                found++;
        Console.WriteLine($"{found} {offsetsRight}");
        GC.KeepAlive(survivors);
    }
}
