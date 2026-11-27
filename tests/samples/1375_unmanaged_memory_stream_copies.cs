#:property AllowUnsafeBlocks=true
// UnmanagedMemoryStream over native memory: Read, Write, Seek and the span overloads copy with the framework's
// Buffer.Memmove (SpanHelpers.Memmove), whose IL asks Unsafe.OpportunisticMisalignment for the source's alignment.
// Embedded resources (ResourceManager, GetManifestResourceStream) read through the same stream.
using System;
using System.IO;
using System.Runtime.InteropServices;

unsafe
{
    const int size = 300;
    byte* memory = (byte*)NativeMemory.AllocZeroed(size);
    try
    {
        using var stream = new UnmanagedMemoryStream(memory, size, size, FileAccess.ReadWrite);
        var pattern = new byte[257];
        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 7 + 3);

        // Odd offsets and lengths: misaligned source and destination, sizes around the vector widths.
        stream.Position = 1;
        stream.Write(pattern, 3, 250);
        Console.WriteLine($"after write: position {stream.Position}, length {stream.Length}");

        foreach (int count in new[] { 0, 1, 7, 15, 16, 31, 33, 64, 65, 129, 249 })
        {
            stream.Position = 1;
            var buffer = new byte[count + 5];
            int read = stream.Read(buffer, 2, count);
            int sum = 0;
            for (int i = 0; i < buffer.Length; i++) sum = sum * 31 + buffer[i];
            Console.WriteLine($"read {count,3}: got {read,3}, checksum {sum}");
        }

        Span<byte> span = stackalloc byte[40];
        stream.Position = 100;
        Console.WriteLine($"span read: {stream.Read(span)} bytes, first {span[0]}, last {span[39]}");

        stream.Position = 290;
        stream.Write(new ReadOnlySpan<byte>(pattern, 0, 10));
        Console.WriteLine($"tail: {memory[290]} {memory[299]}");

        try { stream.Write(pattern, 0, 1); }
        catch (NotSupportedException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        stream.Position = 5;
        var copy = new MemoryStream();
        stream.CopyTo(copy, 17);
        Console.WriteLine($"copied {copy.Length} bytes, byte 0 = {copy.ToArray()[0]}");
    }
    finally
    {
        NativeMemory.Free(memory);
    }
}
