#:property AllowUnsafeBlocks=true
// Marshal memory and string members: AllocHGlobal/ReAllocHGlobal/CoTaskMem, Read*/Write*, Copy of every primitive array
// type in both directions (and its argument errors), UnsafeAddrOfPinnedArrayElement, string <-> native conversions.
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

    static void Memory()
    {
        IntPtr p = Marshal.AllocHGlobal(16);
        Marshal.WriteInt64(p, 0x0102030405060708);
        Marshal.WriteInt32(p, 8, 77);
        Marshal.WriteInt16(p, 12, (short)-2);
        Marshal.WriteByte(p, 14, 200);
        Marshal.WriteIntPtr(p, 8, (IntPtr)12345);
        Console.WriteLine($"{Marshal.ReadInt64(p):X} {Marshal.ReadIntPtr(p, 8)} {Marshal.ReadByte(p)} {Marshal.ReadInt16(p, 2)}");
        p = Marshal.ReAllocHGlobal(p, (IntPtr)64);
        Console.WriteLine(Marshal.ReadInt64(p));
        Marshal.FreeHGlobal(p);
        IntPtr c = Marshal.AllocCoTaskMem(8);
        Marshal.WriteInt64(c, 99);
        Console.WriteLine(Marshal.ReadInt64(c));
        c = Marshal.ReAllocCoTaskMem(c, 32);
        Console.WriteLine(Marshal.ReadInt64(c));
        Marshal.FreeCoTaskMem(c);
        Marshal.FreeHGlobal(IntPtr.Zero);

        int[] ints = { 1, 2, 3, 4 };
        IntPtr buf = Marshal.AllocHGlobal(16);
        Marshal.Copy(ints, 0, buf, 4);
        Console.WriteLine($"{Marshal.ReadInt32(buf, 8)}");
        int[] ints2 = new int[4];
        Marshal.Copy(buf, ints2, 1, 3);
        Console.WriteLine(string.Join(",", ints2));
        byte[] bytes = { 9, 8, 7 };
        Marshal.Copy(bytes, 0, buf, 3);
        byte[] bytes2 = new byte[3];
        Marshal.Copy(buf, bytes2, 0, 3);
        Console.WriteLine(string.Join(",", bytes2));
        long[] longs = { 5, 6 };
        Marshal.Copy(longs, 0, buf, 2);
        long[] longs2 = new long[2];
        Marshal.Copy(buf, longs2, 0, 2);
        Console.WriteLine(string.Join(",", longs2));
        short[] shorts = { 5, -6 };
        Marshal.Copy(shorts, 0, buf, 2);
        short[] shorts2 = new short[2];
        Marshal.Copy(buf, shorts2, 0, 2);
        Console.WriteLine(string.Join(",", shorts2));
        char[] chars = { 'a', 'b' };
        Marshal.Copy(chars, 0, buf, 2);
        char[] chars2 = new char[2];
        Marshal.Copy(buf, chars2, 0, 2);
        Console.WriteLine(new string(chars2));
        float[] floats = { 1.5f, -2.5f };
        Marshal.Copy(floats, 0, buf, 2);
        float[] floats2 = new float[2];
        Marshal.Copy(buf, floats2, 0, 2);
        Console.WriteLine(string.Join(",", floats2));
        double[] doubles = { 1.5, -2.5 };
        Marshal.Copy(doubles, 0, buf, 2);
        double[] doubles2 = new double[2];
        Marshal.Copy(buf, doubles2, 0, 2);
        Console.WriteLine(string.Join(",", doubles2));
        IntPtr[] ptrs = { (IntPtr)1, (IntPtr)2 };
        Marshal.Copy(ptrs, 0, buf, 2);
        IntPtr[] ptrs2 = new IntPtr[2];
        Marshal.Copy(buf, ptrs2, 0, 2);
        Console.WriteLine(string.Join(",", ptrs2));
        Try("copy range", () => Marshal.Copy(ints, 2, buf, 5));
        Try("copy null", () => Marshal.Copy((int[])null, 0, buf, 1));
        Try("copy negative", () => Marshal.Copy(ints, -1, buf, 1));
        Console.WriteLine(Marshal.UnsafeAddrOfPinnedArrayElement<int>(ints, 2) - Marshal.UnsafeAddrOfPinnedArrayElement<int>(ints, 0));
        Console.WriteLine(Marshal.UnsafeAddrOfPinnedArrayElement<int>(ints, 1) - Marshal.UnsafeAddrOfPinnedArrayElement<int>(ints, 0));
        Marshal.FreeHGlobal(buf);
    }

    static void Strings()
    {
        IntPtr a = Marshal.StringToHGlobalAnsi("héllo");
        Console.WriteLine(Marshal.PtrToStringAnsi(a));
        Console.WriteLine(Marshal.PtrToStringAnsi(a, 2));
        Marshal.FreeHGlobal(a);
        IntPtr u = Marshal.StringToHGlobalUni("héllo");
        Console.WriteLine(Marshal.PtrToStringUni(u));
        Console.WriteLine(Marshal.PtrToStringUni(u, 3));
        Marshal.FreeHGlobal(u);
        IntPtr u8 = Marshal.StringToCoTaskMemUTF8("héllo");
        Console.WriteLine(Marshal.PtrToStringUTF8(u8));
        Console.WriteLine(Marshal.PtrToStringUTF8(u8, 3));
        Marshal.ZeroFreeCoTaskMemUTF8(u8);
        IntPtr au = Marshal.StringToCoTaskMemAuto("auto");
        Console.WriteLine(Marshal.PtrToStringAuto(au));
        Marshal.FreeCoTaskMem(au);
        IntPtr b = Marshal.StringToBSTR("bstr");
        Console.WriteLine(Marshal.PtrToStringBSTR(b));
        Marshal.FreeBSTR(b);
        Console.WriteLine(Marshal.PtrToStringAnsi(IntPtr.Zero) is null);
        Console.WriteLine(Marshal.StringToHGlobalAnsi(null) == IntPtr.Zero);
    }

    static void Main()
    {
        Memory();
        Strings();
    }
}
