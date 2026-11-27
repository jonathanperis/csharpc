#:property AllowUnsafeBlocks=true
// Short memory copies, inline in csharpc since #309: every length from 0 to 80 bytes, element sizes 1 to 16, forward and backward
// overlaps by every shift, through Span.CopyTo, Array.Copy, Buffer.BlockCopy, Buffer.MemoryCopy and Unsafe.CopyBlockUnaligned,
// plus the framework's StringBuilder (its appends copy with Buffer.Memmove). A checksum per case family keeps the output short.
using System;
using System.Runtime.CompilerServices;
using System.Text;

struct Pair { public long A, B; }

class Program
{
    static ulong Sum<T>(T[] array) where T : unmanaged
    {
        ulong h = 1469598103934665603;
        foreach (var b in System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(array)) h = (h ^ b) * 1099511628211;
        return h;
    }

    static ulong Overlaps<T>(Func<int, T> make) where T : unmanaged
    {
        ulong total = 0;
        for (var length = 0; length <= 80 / Unsafe.SizeOf<T>() + 1; length++)
            for (var shift = -6; shift <= 6; shift++)
            {
                var array = new T[length + 16];
                for (var i = 0; i < array.Length; i++) array[i] = make(i);
                var from = 8;
                var to = 8 + shift;
                array.AsSpan(from, length).CopyTo(array.AsSpan(to, length));
                total = total * 31 + Sum(array);
                for (var i = 0; i < array.Length; i++) array[i] = make(i * 7);
                Array.Copy(array, from, array, to, length);
                total = total * 31 + Sum(array);
            }
        return total;
    }

    static unsafe ulong Raw()
    {
        ulong total = 0;
        for (var length = 0; length <= 80; length++)
            for (var shift = -9; shift <= 9; shift += 3)
            {
                var bytes = new byte[length + 32];
                for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 13 + 1);
                Buffer.BlockCopy(bytes, 12, bytes, 12 + shift, length);
                total = total * 31 + Sum(bytes);
                fixed (byte* p = bytes)
                {
                    Buffer.MemoryCopy(p + 12, p + 12 - shift, bytes.Length - 12 + shift, length);
                    total = total * 31 + Sum(bytes);
                    Unsafe.CopyBlockUnaligned(p + 13, p + 12 + shift, (uint)length);
                    total = total * 31 + Sum(bytes);
                }
            }
        return total;
    }

    static void Main()
    {
        Console.WriteLine($"bytes   {Overlaps(i => (byte)(i * 5 + 3))}");
        Console.WriteLine($"chars   {Overlaps(i => (char)('a' + i % 26))}");
        Console.WriteLine($"ints    {Overlaps(i => i * 1000003)}");
        Console.WriteLine($"longs   {Overlaps(i => (long)i << 33 | (uint)i)}");
        Console.WriteLine($"pairs   {Overlaps(i => new Pair { A = i, B = -i })}");
        Console.WriteLine($"raw     {Raw()}");

        var builder = new StringBuilder();
        for (var length = 0; length <= 40; length++) builder.Append(new string((char)('A' + length % 26), length)).Append('|');
        builder.Insert(5, "insert").Replace("BB", "b").Remove(3, 7);
        var text = builder.ToString();
        Console.WriteLine($"builder {text.Length} {text.Substring(0, 30)} {text.Substring(text.Length - 30)}");
    }
}
