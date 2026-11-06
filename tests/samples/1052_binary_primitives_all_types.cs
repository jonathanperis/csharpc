// BinaryPrimitives over every integer, floating-point and 128-bit type: Read/TryRead/Write/TryWrite in both byte orders,
// too-short spans (exceptions and false), ReverseEndianness for values and spans.
using System;
using System.Buffers.Binary;
using System.Linq;

class P
{
    static string Hex(byte[] b) => Convert.ToHexString(b);
    static void Int16Big()
    {
        byte[] buf = new byte[32];
        short v = (short)-1234;
        BinaryPrimitives.WriteInt16BigEndian(buf, v);
        Console.WriteLine("Int16Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt16BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt16BigEndian(buf, out short r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt16BigEndian(small, out short r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt16BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt16BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt16BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt16BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int16Little()
    {
        byte[] buf = new byte[32];
        short v = (short)-1234;
        BinaryPrimitives.WriteInt16LittleEndian(buf, v);
        Console.WriteLine("Int16Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt16LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt16LittleEndian(buf, out short r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt16LittleEndian(small, out short r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt16LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt16LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt16LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt16LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt16Big()
    {
        byte[] buf = new byte[32];
        ushort v = (ushort)0xABCD;
        BinaryPrimitives.WriteUInt16BigEndian(buf, v);
        Console.WriteLine("UInt16Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt16BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt16BigEndian(buf, out ushort r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt16BigEndian(small, out ushort r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt16BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt16BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt16BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt16BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt16Little()
    {
        byte[] buf = new byte[32];
        ushort v = (ushort)0xABCD;
        BinaryPrimitives.WriteUInt16LittleEndian(buf, v);
        Console.WriteLine("UInt16Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt16LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt16LittleEndian(buf, out ushort r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt16LittleEndian(small, out ushort r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt16LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt16LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt16LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt16LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int32Big()
    {
        byte[] buf = new byte[32];
        int v = -123456789;
        BinaryPrimitives.WriteInt32BigEndian(buf, v);
        Console.WriteLine("Int32Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt32BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt32BigEndian(buf, out int r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt32BigEndian(small, out int r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt32BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt32BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt32BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt32BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int32Little()
    {
        byte[] buf = new byte[32];
        int v = -123456789;
        BinaryPrimitives.WriteInt32LittleEndian(buf, v);
        Console.WriteLine("Int32Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt32LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt32LittleEndian(buf, out int r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt32LittleEndian(small, out int r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt32LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt32LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt32LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt32LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt32Big()
    {
        byte[] buf = new byte[32];
        uint v = 0xDEADBEEFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, v);
        Console.WriteLine("UInt32Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt32BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt32BigEndian(buf, out uint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt32BigEndian(small, out uint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt32BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt32BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt32BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt32BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt32Little()
    {
        byte[] buf = new byte[32];
        uint v = 0xDEADBEEFu;
        BinaryPrimitives.WriteUInt32LittleEndian(buf, v);
        Console.WriteLine("UInt32Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt32LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt32LittleEndian(buf, out uint r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt32LittleEndian(small, out uint r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt32LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt32LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt32LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt32LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int64Big()
    {
        byte[] buf = new byte[32];
        long v = -1234567890123456789L;
        BinaryPrimitives.WriteInt64BigEndian(buf, v);
        Console.WriteLine("Int64Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt64BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt64BigEndian(buf, out long r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt64BigEndian(small, out long r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt64BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt64BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt64BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt64BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int64Little()
    {
        byte[] buf = new byte[32];
        long v = -1234567890123456789L;
        BinaryPrimitives.WriteInt64LittleEndian(buf, v);
        Console.WriteLine("Int64Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt64LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt64LittleEndian(buf, out long r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt64LittleEndian(small, out long r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt64LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt64LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt64LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt64LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt64Big()
    {
        byte[] buf = new byte[32];
        ulong v = 0xFEDCBA9876543210UL;
        BinaryPrimitives.WriteUInt64BigEndian(buf, v);
        Console.WriteLine("UInt64Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt64BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt64BigEndian(buf, out ulong r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt64BigEndian(small, out ulong r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt64BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt64BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt64BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt64BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt64Little()
    {
        byte[] buf = new byte[32];
        ulong v = 0xFEDCBA9876543210UL;
        BinaryPrimitives.WriteUInt64LittleEndian(buf, v);
        Console.WriteLine("UInt64Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt64LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt64LittleEndian(buf, out ulong r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt64LittleEndian(small, out ulong r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt64LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt64LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt64LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt64LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int128Big()
    {
        byte[] buf = new byte[32];
        Int128 v = (Int128)(-1234567890123456789L) * 1000000007;
        BinaryPrimitives.WriteInt128BigEndian(buf, v);
        Console.WriteLine("Int128Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt128BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt128BigEndian(buf, out Int128 r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt128BigEndian(small, out Int128 r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt128BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt128BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt128BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt128BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Int128Little()
    {
        byte[] buf = new byte[32];
        Int128 v = (Int128)(-1234567890123456789L) * 1000000007;
        BinaryPrimitives.WriteInt128LittleEndian(buf, v);
        Console.WriteLine("Int128Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadInt128LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadInt128LittleEndian(buf, out Int128 r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadInt128LittleEndian(small, out Int128 r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteInt128LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteInt128LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteInt128LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadInt128LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt128Big()
    {
        byte[] buf = new byte[32];
        UInt128 v = (UInt128)0xFEDCBA9876543210UL << 40 | 77;
        BinaryPrimitives.WriteUInt128BigEndian(buf, v);
        Console.WriteLine("UInt128Big " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt128BigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt128BigEndian(buf, out UInt128 r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt128BigEndian(small, out UInt128 r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt128BigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt128BigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt128BigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt128BigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void UInt128Little()
    {
        byte[] buf = new byte[32];
        UInt128 v = (UInt128)0xFEDCBA9876543210UL << 40 | 77;
        BinaryPrimitives.WriteUInt128LittleEndian(buf, v);
        Console.WriteLine("UInt128Little " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadUInt128LittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadUInt128LittleEndian(buf, out UInt128 r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadUInt128LittleEndian(small, out UInt128 r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteUInt128LittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteUInt128LittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteUInt128LittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadUInt128LittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void HalfBig()
    {
        byte[] buf = new byte[32];
        Half v = (Half)1.5f;
        BinaryPrimitives.WriteHalfBigEndian(buf, v);
        Console.WriteLine("HalfBig " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadHalfBigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadHalfBigEndian(buf, out Half r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadHalfBigEndian(small, out Half r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteHalfBigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteHalfBigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteHalfBigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadHalfBigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void HalfLittle()
    {
        byte[] buf = new byte[32];
        Half v = (Half)1.5f;
        BinaryPrimitives.WriteHalfLittleEndian(buf, v);
        Console.WriteLine("HalfLittle " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadHalfLittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadHalfLittleEndian(buf, out Half r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadHalfLittleEndian(small, out Half r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteHalfLittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteHalfLittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteHalfLittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadHalfLittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void SingleBig()
    {
        byte[] buf = new byte[32];
        float v = -2.75f;
        BinaryPrimitives.WriteSingleBigEndian(buf, v);
        Console.WriteLine("SingleBig " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadSingleBigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadSingleBigEndian(buf, out float r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadSingleBigEndian(small, out float r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteSingleBigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteSingleBigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteSingleBigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadSingleBigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void SingleLittle()
    {
        byte[] buf = new byte[32];
        float v = -2.75f;
        BinaryPrimitives.WriteSingleLittleEndian(buf, v);
        Console.WriteLine("SingleLittle " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadSingleLittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadSingleLittleEndian(buf, out float r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadSingleLittleEndian(small, out float r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteSingleLittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteSingleLittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteSingleLittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadSingleLittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void DoubleBig()
    {
        byte[] buf = new byte[32];
        double v = 3.14159265358979;
        BinaryPrimitives.WriteDoubleBigEndian(buf, v);
        Console.WriteLine("DoubleBig " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadDoubleBigEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadDoubleBigEndian(buf, out double r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadDoubleBigEndian(small, out double r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteDoubleBigEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteDoubleBigEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteDoubleBigEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadDoubleBigEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void DoubleLittle()
    {
        byte[] buf = new byte[32];
        double v = 3.14159265358979;
        BinaryPrimitives.WriteDoubleLittleEndian(buf, v);
        Console.WriteLine("DoubleLittle " + Hex(buf));
        Console.WriteLine(BinaryPrimitives.ReadDoubleLittleEndian(buf));
        Console.WriteLine(BinaryPrimitives.TryReadDoubleLittleEndian(buf, out double r) + " " + r);
        byte[] small = new byte[1];
        Console.WriteLine(BinaryPrimitives.TryReadDoubleLittleEndian(small, out double r2) + " " + r2);
        Console.WriteLine(BinaryPrimitives.TryWriteDoubleLittleEndian(small, v));
        Console.WriteLine(BinaryPrimitives.TryWriteDoubleLittleEndian(buf.AsSpan(3), v));
        Console.WriteLine(Hex(buf));
        try { BinaryPrimitives.WriteDoubleLittleEndian(small, v); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        try { BinaryPrimitives.ReadDoubleLittleEndian(small); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
    }

    static void Reverse()
    {
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((byte)0x12));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((sbyte)-2));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((short)0x1234));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((ushort)0xFEDC));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness(0x12345678));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness(0xFEDCBA98u));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness(0x123456789ABCDEF0L));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness(0xFEDCBA9876543210UL));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((Int128)12345678901234567890UL * 3));
        Console.WriteLine(BinaryPrimitives.ReverseEndianness((UInt128)12345678901234567890UL * 3));
        short[] s = { 0x0102, 0x0304 }; short[] sd = new short[2];
        BinaryPrimitives.ReverseEndianness(s, sd); Console.WriteLine(string.Join(",", sd));
        ushort[] us = { 0x0102, 0x0304 }; ushort[] usd = new ushort[2];
        BinaryPrimitives.ReverseEndianness(us, usd); Console.WriteLine(string.Join(",", usd));
        int[] i = { 0x01020304, 5 }; int[] id = new int[2];
        BinaryPrimitives.ReverseEndianness(i, id); Console.WriteLine(string.Join(",", id));
        uint[] ui = { 0x01020304, 5 }; uint[] uid = new uint[2];
        BinaryPrimitives.ReverseEndianness(ui, uid); Console.WriteLine(string.Join(",", uid));
        long[] l = { 0x0102030405060708, 5 }; long[] ld = new long[2];
        BinaryPrimitives.ReverseEndianness(l, ld); Console.WriteLine(string.Join(",", ld));
        ulong[] ul = { 0x0102030405060708, 5 }; ulong[] uld = new ulong[2];
        BinaryPrimitives.ReverseEndianness(ul, uld); Console.WriteLine(string.Join(",", uld));
        Int128[] i1 = { 12345678901234567890UL, 5 }; Int128[] i1d = new Int128[2];
        BinaryPrimitives.ReverseEndianness(i1, i1d); Console.WriteLine(string.Join(",", i1d));
        UInt128[] u1 = { 12345678901234567890UL, 5 }; UInt128[] u1d = new UInt128[2];
        BinaryPrimitives.ReverseEndianness(u1, u1d); Console.WriteLine(string.Join(",", u1d));
        try { BinaryPrimitives.ReverseEndianness(i, new int[1]); } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
        // in place
        BinaryPrimitives.ReverseEndianness(i, i); Console.WriteLine(string.Join(",", i));
    }

    static void Main()
    {
        Int16Big();
        Int16Little();
        UInt16Big();
        UInt16Little();
        Int32Big();
        Int32Little();
        UInt32Big();
        UInt32Little();
        Int64Big();
        Int64Little();
        UInt64Big();
        UInt64Little();
        Int128Big();
        Int128Little();
        UInt128Big();
        UInt128Little();
        HalfBig();
        HalfLittle();
        SingleBig();
        SingleLittle();
        DoubleBig();
        DoubleLittle();
        Reverse();
    }
}
