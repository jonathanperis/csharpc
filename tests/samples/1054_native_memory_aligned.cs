#:property AllowUnsafeBlocks=true
// NativeMemory: Alloc/AllocZeroed/Realloc/Free/Clear/Fill/Copy and the aligned allocators (alignment of the
// returned address, contents kept by AlignedRealloc, a non-power-of-two alignment).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

unsafe class P
{
    static void Try(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Native()
    {
        byte* p = (byte*)NativeMemory.Alloc(8);
        NativeMemory.Clear(p, 8);
        NativeMemory.Fill(p, 4, 0x7F);
        Console.WriteLine(string.Join(",", new ReadOnlySpan<byte>(p, 8).ToArray()));
        byte* q = (byte*)NativeMemory.AllocZeroed(8);
        NativeMemory.Copy(p, q + 2, 4);
        Console.WriteLine(string.Join(",", new ReadOnlySpan<byte>(q, 8).ToArray()));
        q = (byte*)NativeMemory.Realloc(q, 16);
        Console.WriteLine(string.Join(",", new ReadOnlySpan<byte>(q, 8).ToArray()));
        NativeMemory.Free(q);
        NativeMemory.Free(p);
        NativeMemory.Free(null);
        int* arr = (int*)NativeMemory.Alloc(4, sizeof(int));
        for (int i = 0; i < 4; i++) arr[i] = i * i;
        Console.WriteLine($"{arr[0]} {arr[3]}");
        NativeMemory.Free(arr);
        int* z = (int*)NativeMemory.AllocZeroed(3, sizeof(int));
        Console.WriteLine($"{z[0]} {z[1]} {z[2]}");
        NativeMemory.Free(z);
    }

    static void Aligned()
    {
        void* a = NativeMemory.AlignedAlloc(100, 64);
        Console.WriteLine((nuint)a % 64);
        NativeMemory.Clear(a, 100);
        a = NativeMemory.AlignedRealloc(a, 200, 128);
        Console.WriteLine((nuint)a % 128);
        NativeMemory.AlignedFree(a);
        Try("align not power of two", () => NativeMemory.AlignedAlloc(10, 3));
    }

    static void Main()
    {
        Native();
        Aligned();
    }
}
