// BinaryPrimitives over nint/nuint: as wide as the target's pointers, so the bytes written differ on 32-bit targets (wasm32).
using System;
using System.Buffers.Binary;
using System.Linq;

class P
{
    static string Hex(byte[] b) => Convert.ToHexString(b);
    static void IntPtrBig()
    {
        byte[] buf = new byte[32];
        nint v = (nint)(-123456);
        BinaryPrimitives.WriteIntPtrBigEndian(buf, v);
        Console.WriteLine("IntPtrBig " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadIntPtrBigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadIntPtrBigEndian(buf, out nint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadIntPtrBigEndian(small, out nint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteIntPtrBigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteIntPtrBigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteIntPtrBigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadIntPtrBigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void IntPtrLittle()
    {
        byte[] buf = new byte[32];
        nint v = (nint)(-123456);
        BinaryPrimitives.WriteIntPtrLittleEndian(buf, v);
        Console.WriteLine("IntPtrLittle " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadIntPtrLittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadIntPtrLittleEndian(buf, out nint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadIntPtrLittleEndian(small, out nint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteIntPtrLittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteIntPtrLittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteIntPtrLittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadIntPtrLittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UIntPtrBig()
    {
        byte[] buf = new byte[32];
        nuint v = (nuint)0xABCDEF;
        BinaryPrimitives.WriteUIntPtrBigEndian(buf, v);
        Console.WriteLine("UIntPtrBig " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUIntPtrBigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUIntPtrBigEndian(buf, out nuint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUIntPtrBigEndian(small, out nuint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUIntPtrBigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUIntPtrBigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUIntPtrBigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUIntPtrBigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UIntPtrLittle()
    {
        byte[] buf = new byte[32];
        nuint v = (nuint)0xABCDEF;
        BinaryPrimitives.WriteUIntPtrLittleEndian(buf, v);
        Console.WriteLine("UIntPtrLittle " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUIntPtrLittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUIntPtrLittleEndian(buf, out nuint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUIntPtrLittleEndian(small, out nuint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUIntPtrLittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUIntPtrLittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUIntPtrLittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUIntPtrLittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Reverse()
    {
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((nint)0x1234567));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((nuint)0x1234567));
        nint[] ni = { 0x01020304, 5 }; nint[] nid = new nint[2];
        BinaryPrimitives.ReverseEndianness(ni, nid); Console.WriteLine(string.Join(",", nid));
        nuint[] nu = { 0x01020304, 5 }; nuint[] nud = new nuint[2];
        BinaryPrimitives.ReverseEndianness(nu, nud); Console.WriteLine(string.Join(",", nud));
    }

    static void Main()
    {
        IntPtrBig();
        IntPtrLittle();
        UIntPtrBig();
        UIntPtrLittle();
        Reverse();
    }
}
