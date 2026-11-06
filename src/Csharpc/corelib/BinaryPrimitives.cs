// System.Buffers.Binary.BinaryPrimitives: endianness-aware reads and writes over byte spans. Every
// csharpc target is little-endian. Too-short spans throw ArgumentOutOfRangeException("length"), as
// .NET's MemoryMarshal.Read/Write do; the Try* forms return false instead.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.Binary.BinaryPrimitives")]
    public static class BinaryPrimitivesImpl
    {
        public static sbyte ReverseEndianness(sbyte value) => value;
        public static byte ReverseEndianness(byte value) => value;
        public static short ReverseEndianness(short value) => (short)ReverseEndianness((ushort)value);
        public static ushort ReverseEndianness(ushort value) => (ushort)((value >> 8) | (value << 8));
        public static char ReverseEndianness(char value) => (char)ReverseEndianness((ushort)value);
        public static int ReverseEndianness(int value) => (int)ReverseEndianness((uint)value);
        public static uint ReverseEndianness(uint value) =>
            (value >> 24) | ((value >> 8) & 0x0000FF00u) | ((value << 8) & 0x00FF0000u) | (value << 24);
        public static long ReverseEndianness(long value) => (long)ReverseEndianness((ulong)value);
        public static ulong ReverseEndianness(ulong value) =>
            ((ulong)ReverseEndianness((uint)value) << 32) | ReverseEndianness((uint)(value >> 32));
        public static nint ReverseEndianness(nint value) => IntPtr.Size == 8 ? (nint)ReverseEndianness((long)value) : (nint)ReverseEndianness((int)value);
        public static nuint ReverseEndianness(nuint value) => UIntPtr.Size == 8 ? (nuint)ReverseEndianness((ulong)value) : (nuint)ReverseEndianness((uint)value);
        public static UInt128 ReverseEndianness(UInt128 value) => new UInt128(ReverseEndianness((ulong)value), ReverseEndianness((ulong)(value >> 64)));
        public static Int128 ReverseEndianness(Int128 value) => (Int128)ReverseEndianness((UInt128)value);

        // The span forms: element by element; source and destination may be the same span (in place),
        // and a too-short destination is .NET's ArgumentException for "destination".
        public static void ReverseEndianness(ReadOnlySpan<short> source, Span<short> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<ushort> source, Span<ushort> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<int> source, Span<int> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<uint> source, Span<uint> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<long> source, Span<long> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<ulong> source, Span<ulong> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }

        private static void DestinationTooShort() => throw new ArgumentException("Not enough space available in the buffer.", "destination");

        private static void TooShort() => throw new ArgumentOutOfRangeException("length");

        private static ulong Load(ReadOnlySpan<byte> source, int size)
        {
            if (source.Length < size) TooShort();
            ulong v = 0;
            for (int i = size - 1; i >= 0; i--) v = (v << 8) | source[i];
            return v;
        }

        private static bool TryLoad(ReadOnlySpan<byte> source, int size, out ulong value)
        {
            value = 0;
            if (source.Length < size) return false;
            for (int i = size - 1; i >= 0; i--) value = (value << 8) | source[i];
            return true;
        }

        private static void Store(Span<byte> destination, int size, ulong value)
        {
            if (destination.Length < size) TooShort();
            for (int i = 0; i < size; i++) { destination[i] = (byte)value; value >>= 8; }
        }

        private static bool TryStore(Span<byte> destination, int size, ulong value)
        {
            if (destination.Length < size) return false;
            for (int i = 0; i < size; i++) { destination[i] = (byte)value; value >>= 8; }
            return true;
        }

        // ---- little endian ----
        public static short ReadInt16LittleEndian(ReadOnlySpan<byte> source) => (short)Load(source, 2);
        public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source) => (ushort)Load(source, 2);
        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> source) => (int)Load(source, 4);
        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source) => (uint)Load(source, 4);
        public static long ReadInt64LittleEndian(ReadOnlySpan<byte> source) => (long)Load(source, 8);
        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source) => Load(source, 8);
        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source) => CoreIntrinsics.BitsToSingle((int)Load(source, 4));
        public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> source) => CoreIntrinsics.BitsToDouble((long)Load(source, 8));

        public static void WriteInt16LittleEndian(Span<byte> destination, short value) => Store(destination, 2, (ulong)(ushort)value);
        public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value) => Store(destination, 2, value);
        public static void WriteInt32LittleEndian(Span<byte> destination, int value) => Store(destination, 4, (ulong)(uint)value);
        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value) => Store(destination, 4, value);
        public static void WriteInt64LittleEndian(Span<byte> destination, long value) => Store(destination, 8, (ulong)value);
        public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value) => Store(destination, 8, value);
        public static void WriteSingleLittleEndian(Span<byte> destination, float value) => Store(destination, 4, (ulong)(uint)CoreIntrinsics.SingleToBits(value));
        public static void WriteDoubleLittleEndian(Span<byte> destination, double value) => Store(destination, 8, (ulong)CoreIntrinsics.DoubleToBits(value));

        public static bool TryReadInt16LittleEndian(ReadOnlySpan<byte> source, out short value) { bool ok = TryLoad(source, 2, out ulong v); value = (short)v; return ok; }
        public static bool TryReadUInt16LittleEndian(ReadOnlySpan<byte> source, out ushort value) { bool ok = TryLoad(source, 2, out ulong v); value = (ushort)v; return ok; }
        public static bool TryReadInt32LittleEndian(ReadOnlySpan<byte> source, out int value) { bool ok = TryLoad(source, 4, out ulong v); value = (int)v; return ok; }
        public static bool TryReadUInt32LittleEndian(ReadOnlySpan<byte> source, out uint value) { bool ok = TryLoad(source, 4, out ulong v); value = (uint)v; return ok; }
        public static bool TryReadInt64LittleEndian(ReadOnlySpan<byte> source, out long value) { bool ok = TryLoad(source, 8, out ulong v); value = (long)v; return ok; }
        public static bool TryReadUInt64LittleEndian(ReadOnlySpan<byte> source, out ulong value) => TryLoad(source, 8, out value);

        public static bool TryWriteInt16LittleEndian(Span<byte> destination, short value) => TryStore(destination, 2, (ulong)(ushort)value);
        public static bool TryWriteUInt16LittleEndian(Span<byte> destination, ushort value) => TryStore(destination, 2, value);
        public static bool TryWriteInt32LittleEndian(Span<byte> destination, int value) => TryStore(destination, 4, (ulong)(uint)value);
        public static bool TryWriteUInt32LittleEndian(Span<byte> destination, uint value) => TryStore(destination, 4, value);
        public static bool TryWriteInt64LittleEndian(Span<byte> destination, long value) => TryStore(destination, 8, (ulong)value);
        public static bool TryWriteUInt64LittleEndian(Span<byte> destination, ulong value) => TryStore(destination, 8, value);

        // ---- big endian ----
        public static short ReadInt16BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((short)Load(source, 2));
        public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((ushort)Load(source, 2));
        public static int ReadInt32BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((int)Load(source, 4));
        public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((uint)Load(source, 4));
        public static long ReadInt64BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((long)Load(source, 8));
        public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness(Load(source, 8));
        public static float ReadSingleBigEndian(ReadOnlySpan<byte> source) => CoreIntrinsics.BitsToSingle(ReverseEndianness((int)Load(source, 4)));
        public static double ReadDoubleBigEndian(ReadOnlySpan<byte> source) => CoreIntrinsics.BitsToDouble(ReverseEndianness((long)Load(source, 8)));

        public static void WriteInt16BigEndian(Span<byte> destination, short value) => Store(destination, 2, (ulong)ReverseEndianness((ushort)value));
        public static void WriteUInt16BigEndian(Span<byte> destination, ushort value) => Store(destination, 2, ReverseEndianness(value));
        public static void WriteInt32BigEndian(Span<byte> destination, int value) => Store(destination, 4, (ulong)ReverseEndianness((uint)value));
        public static void WriteUInt32BigEndian(Span<byte> destination, uint value) => Store(destination, 4, ReverseEndianness(value));
        public static void WriteInt64BigEndian(Span<byte> destination, long value) => Store(destination, 8, ReverseEndianness((ulong)value));
        public static void WriteUInt64BigEndian(Span<byte> destination, ulong value) => Store(destination, 8, ReverseEndianness(value));
        public static void WriteSingleBigEndian(Span<byte> destination, float value) => Store(destination, 4, (ulong)ReverseEndianness((uint)CoreIntrinsics.SingleToBits(value)));
        public static void WriteDoubleBigEndian(Span<byte> destination, double value) => Store(destination, 8, ReverseEndianness((ulong)CoreIntrinsics.DoubleToBits(value)));

        public static bool TryReadInt16BigEndian(ReadOnlySpan<byte> source, out short value) { bool ok = TryLoad(source, 2, out ulong v); value = ReverseEndianness((short)v); return ok; }
        public static bool TryReadUInt16BigEndian(ReadOnlySpan<byte> source, out ushort value) { bool ok = TryLoad(source, 2, out ulong v); value = ReverseEndianness((ushort)v); return ok; }
        public static bool TryReadInt32BigEndian(ReadOnlySpan<byte> source, out int value) { bool ok = TryLoad(source, 4, out ulong v); value = ReverseEndianness((int)v); return ok; }
        public static bool TryReadUInt32BigEndian(ReadOnlySpan<byte> source, out uint value) { bool ok = TryLoad(source, 4, out ulong v); value = ReverseEndianness((uint)v); return ok; }
        public static bool TryReadInt64BigEndian(ReadOnlySpan<byte> source, out long value) { bool ok = TryLoad(source, 8, out ulong v); value = ReverseEndianness((long)v); return ok; }
        public static bool TryReadUInt64BigEndian(ReadOnlySpan<byte> source, out ulong value) { bool ok = TryLoad(source, 8, out ulong v); value = ReverseEndianness(v); return ok; }

        public static bool TryWriteInt16BigEndian(Span<byte> destination, short value) => TryStore(destination, 2, (ulong)ReverseEndianness((ushort)value));
        public static bool TryWriteUInt16BigEndian(Span<byte> destination, ushort value) => TryStore(destination, 2, ReverseEndianness(value));
        public static bool TryWriteInt32BigEndian(Span<byte> destination, int value) => TryStore(destination, 4, (ulong)ReverseEndianness((uint)value));
        public static bool TryWriteUInt32BigEndian(Span<byte> destination, uint value) => TryStore(destination, 4, ReverseEndianness(value));
        public static bool TryWriteInt64BigEndian(Span<byte> destination, long value) => TryStore(destination, 8, ReverseEndianness((ulong)value));
        public static bool TryWriteUInt64BigEndian(Span<byte> destination, ulong value) => TryStore(destination, 8, ReverseEndianness(value));

        public static void ReverseEndianness(ReadOnlySpan<nint> source, Span<nint> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<nuint> source, Span<nuint> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<Int128> source, Span<Int128> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }
        public static void ReverseEndianness(ReadOnlySpan<UInt128> source, Span<UInt128> destination)
        {
            if (destination.Length < source.Length) DestinationTooShort();
            for (int i = 0; i < source.Length; i++) destination[i] = ReverseEndianness(source[i]);
        }

        // ---- 128-bit, native-sized and Half values, and the floating-point Try forms ----
        private static UInt128 LoadWide(ReadOnlySpan<byte> source)
        {
            if (source.Length < 16) TooShort();
            return new UInt128(Load(source.Slice(8), 8), Load(source, 8));
        }

        private static bool TryLoadWide(ReadOnlySpan<byte> source, out UInt128 value)
        {
            value = default;
            if (source.Length < 16) return false;
            value = new UInt128(Load(source.Slice(8), 8), Load(source, 8));
            return true;
        }

        private static void StoreWide(Span<byte> destination, UInt128 value)
        {
            if (destination.Length < 16) TooShort();
            Store(destination, 8, (ulong)value);
            Store(destination.Slice(8), 8, (ulong)(value >> 64));
        }

        private static bool TryStoreWide(Span<byte> destination, UInt128 value)
        {
            if (destination.Length < 16) return false;
            Store(destination, 8, (ulong)value);
            Store(destination.Slice(8), 8, (ulong)(value >> 64));
            return true;
        }

        public static UInt128 ReadUInt128LittleEndian(ReadOnlySpan<byte> source) => LoadWide(source);
        public static Int128 ReadInt128LittleEndian(ReadOnlySpan<byte> source) => (Int128)LoadWide(source);
        public static UInt128 ReadUInt128BigEndian(ReadOnlySpan<byte> source) => ReverseEndianness(LoadWide(source));
        public static Int128 ReadInt128BigEndian(ReadOnlySpan<byte> source) => (Int128)ReverseEndianness(LoadWide(source));
        public static void WriteUInt128LittleEndian(Span<byte> destination, UInt128 value) => StoreWide(destination, value);
        public static void WriteInt128LittleEndian(Span<byte> destination, Int128 value) => StoreWide(destination, (UInt128)value);
        public static void WriteUInt128BigEndian(Span<byte> destination, UInt128 value) => StoreWide(destination, ReverseEndianness(value));
        public static void WriteInt128BigEndian(Span<byte> destination, Int128 value) => StoreWide(destination, ReverseEndianness((UInt128)value));
        public static bool TryReadUInt128LittleEndian(ReadOnlySpan<byte> source, out UInt128 value) => TryLoadWide(source, out value);
        public static bool TryReadInt128LittleEndian(ReadOnlySpan<byte> source, out Int128 value) { bool ok = TryLoadWide(source, out UInt128 v); value = (Int128)v; return ok; }
        public static bool TryReadUInt128BigEndian(ReadOnlySpan<byte> source, out UInt128 value) { bool ok = TryLoadWide(source, out UInt128 v); value = ReverseEndianness(v); return ok; }
        public static bool TryReadInt128BigEndian(ReadOnlySpan<byte> source, out Int128 value) { bool ok = TryLoadWide(source, out UInt128 v); value = (Int128)ReverseEndianness(v); return ok; }
        public static bool TryWriteUInt128LittleEndian(Span<byte> destination, UInt128 value) => TryStoreWide(destination, value);
        public static bool TryWriteInt128LittleEndian(Span<byte> destination, Int128 value) => TryStoreWide(destination, (UInt128)value);
        public static bool TryWriteUInt128BigEndian(Span<byte> destination, UInt128 value) => TryStoreWide(destination, ReverseEndianness(value));
        public static bool TryWriteInt128BigEndian(Span<byte> destination, Int128 value) => TryStoreWide(destination, ReverseEndianness((UInt128)value));

        // IntPtr and UIntPtr are as wide as the target's pointers.
        public static nint ReadIntPtrLittleEndian(ReadOnlySpan<byte> source) => (nint)Load(source, IntPtr.Size);
        public static nuint ReadUIntPtrLittleEndian(ReadOnlySpan<byte> source) => (nuint)Load(source, UIntPtr.Size);
        public static nint ReadIntPtrBigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((nint)Load(source, IntPtr.Size));
        public static nuint ReadUIntPtrBigEndian(ReadOnlySpan<byte> source) => ReverseEndianness((nuint)Load(source, UIntPtr.Size));
        public static void WriteIntPtrLittleEndian(Span<byte> destination, nint value) => Store(destination, IntPtr.Size, (ulong)value);
        public static void WriteUIntPtrLittleEndian(Span<byte> destination, nuint value) => Store(destination, UIntPtr.Size, (ulong)value);
        public static void WriteIntPtrBigEndian(Span<byte> destination, nint value) => Store(destination, IntPtr.Size, (ulong)ReverseEndianness(value));
        public static void WriteUIntPtrBigEndian(Span<byte> destination, nuint value) => Store(destination, UIntPtr.Size, (ulong)ReverseEndianness(value));
        public static bool TryReadIntPtrLittleEndian(ReadOnlySpan<byte> source, out nint value) { bool ok = TryLoad(source, IntPtr.Size, out ulong v); value = (nint)v; return ok; }
        public static bool TryReadUIntPtrLittleEndian(ReadOnlySpan<byte> source, out nuint value) { bool ok = TryLoad(source, UIntPtr.Size, out ulong v); value = (nuint)v; return ok; }
        public static bool TryReadIntPtrBigEndian(ReadOnlySpan<byte> source, out nint value) { bool ok = TryLoad(source, IntPtr.Size, out ulong v); value = ReverseEndianness((nint)v); return ok; }
        public static bool TryReadUIntPtrBigEndian(ReadOnlySpan<byte> source, out nuint value) { bool ok = TryLoad(source, UIntPtr.Size, out ulong v); value = ReverseEndianness((nuint)v); return ok; }
        public static bool TryWriteIntPtrLittleEndian(Span<byte> destination, nint value) => TryStore(destination, IntPtr.Size, (ulong)value);
        public static bool TryWriteUIntPtrLittleEndian(Span<byte> destination, nuint value) => TryStore(destination, UIntPtr.Size, (ulong)value);
        public static bool TryWriteIntPtrBigEndian(Span<byte> destination, nint value) => TryStore(destination, IntPtr.Size, (ulong)ReverseEndianness(value));
        public static bool TryWriteUIntPtrBigEndian(Span<byte> destination, nuint value) => TryStore(destination, UIntPtr.Size, (ulong)ReverseEndianness(value));

        public static Half ReadHalfLittleEndian(ReadOnlySpan<byte> source) => BitConverter.UInt16BitsToHalf((ushort)Load(source, 2));
        public static Half ReadHalfBigEndian(ReadOnlySpan<byte> source) => BitConverter.UInt16BitsToHalf(ReverseEndianness((ushort)Load(source, 2)));
        public static void WriteHalfLittleEndian(Span<byte> destination, Half value) => Store(destination, 2, BitConverter.HalfToUInt16Bits(value));
        public static void WriteHalfBigEndian(Span<byte> destination, Half value) => Store(destination, 2, ReverseEndianness(BitConverter.HalfToUInt16Bits(value)));
        public static bool TryReadHalfLittleEndian(ReadOnlySpan<byte> source, out Half value) { bool ok = TryLoad(source, 2, out ulong v); value = BitConverter.UInt16BitsToHalf((ushort)v); return ok; }
        public static bool TryReadHalfBigEndian(ReadOnlySpan<byte> source, out Half value) { bool ok = TryLoad(source, 2, out ulong v); value = BitConverter.UInt16BitsToHalf(ReverseEndianness((ushort)v)); return ok; }
        public static bool TryWriteHalfLittleEndian(Span<byte> destination, Half value) => TryStore(destination, 2, BitConverter.HalfToUInt16Bits(value));
        public static bool TryWriteHalfBigEndian(Span<byte> destination, Half value) => TryStore(destination, 2, ReverseEndianness(BitConverter.HalfToUInt16Bits(value)));

        public static bool TryReadSingleLittleEndian(ReadOnlySpan<byte> source, out float value) { bool ok = TryLoad(source, 4, out ulong v); value = CoreIntrinsics.BitsToSingle((int)v); return ok; }
        public static bool TryReadSingleBigEndian(ReadOnlySpan<byte> source, out float value) { bool ok = TryLoad(source, 4, out ulong v); value = CoreIntrinsics.BitsToSingle(ReverseEndianness((int)v)); return ok; }
        public static bool TryReadDoubleLittleEndian(ReadOnlySpan<byte> source, out double value) { bool ok = TryLoad(source, 8, out ulong v); value = CoreIntrinsics.BitsToDouble((long)v); return ok; }
        public static bool TryReadDoubleBigEndian(ReadOnlySpan<byte> source, out double value) { bool ok = TryLoad(source, 8, out ulong v); value = CoreIntrinsics.BitsToDouble(ReverseEndianness((long)v)); return ok; }
        public static bool TryWriteSingleLittleEndian(Span<byte> destination, float value) => TryStore(destination, 4, (ulong)(uint)CoreIntrinsics.SingleToBits(value));
        public static bool TryWriteSingleBigEndian(Span<byte> destination, float value) => TryStore(destination, 4, (ulong)ReverseEndianness((uint)CoreIntrinsics.SingleToBits(value)));
        public static bool TryWriteDoubleLittleEndian(Span<byte> destination, double value) => TryStore(destination, 8, (ulong)CoreIntrinsics.DoubleToBits(value));
        public static bool TryWriteDoubleBigEndian(Span<byte> destination, double value) => TryStore(destination, 8, ReverseEndianness((ulong)CoreIntrinsics.DoubleToBits(value)));
    }
}
