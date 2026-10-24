// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// BitConverter.ToString(byte[]...) ported from dotnet/runtime (release/11.0),
// src/libraries/System.Private.CoreLib/src/System/BitConverter.cs; see THIRD-PARTY-NOTICES.md.
// The bit casts (DoubleToInt64Bits ...) are intrinsics (CodeGen/Intrinsics.cs); the byte conversions are plain C# here.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.BitConverter")]
    internal static class BitConverterExtras
    {
        public static string ToString(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return ToString(value, 0, value.Length);
        }

        public static string ToString(byte[] value, int startIndex)
        {
            if (value == null) throw new ArgumentNullException("value");
            return ToString(value, startIndex, value.Length - startIndex);
        }

        public static string ToString(byte[] value, int startIndex, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (startIndex < 0 || startIndex >= value.Length && startIndex > 0)
                throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            ThrowHelper.IfNegative(length, "length");
            if (startIndex > value.Length - length)
                throw new ArgumentException("The array starting from the specified index is not long enough to read a value of the specified type.", "value");
            if (length == 0) return string.Empty;
            // (int.MaxValue / 3) == 715,827,882 Bytes == 699 MB
            ThrowHelper.IfGreaterThan(length, int.MaxValue / 3, "length");

            string result = CoreIntrinsics.FastAllocateString(length * 3 - 1);
            Span<char> dst = CoreIntrinsics.StringChars(result);
            int j = 0;
            for (int i = startIndex; i < startIndex + length; i++)
            {
                byte b = value[i];
                if (j > 0) dst[j++] = '-';
                dst[j++] = ToCharUpper(b >> 4);
                dst[j++] = ToCharUpper(b);
            }
            return result;
        }

        // ---- reading and writing integers (little-endian, like every csharpc target) ----------
        private static ulong Read(byte[] value, int startIndex, int size)
        {
            if (value == null) throw new ArgumentNullException("value");
            if ((uint)startIndex >= (uint)value.Length)
                throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            if (startIndex > value.Length - size)
                throw new ArgumentException("The array starting from the specified index is not long enough to read a value of the specified type.", "value");
            ulong v = 0;
            for (int i = size - 1; i >= 0; i--) v = (v << 8) | value[startIndex + i];
            return v;
        }

        private static ulong Read(ReadOnlySpan<byte> value, int size)
        {
            if (value.Length < size) throw new ArgumentOutOfRangeException("value");
            ulong v = 0;
            for (int i = size - 1; i >= 0; i--) v = (v << 8) | value[i];
            return v;
        }

        private static byte[] Bytes(ulong v, int size)
        {
            byte[] bytes = new byte[size];
            for (int i = 0; i < size; i++) { bytes[i] = (byte)v; v >>= 8; }
            return bytes;
        }

        public static bool ToBoolean(byte[] value, int startIndex) => Read(value, startIndex, 1) != 0;
        public static char ToChar(byte[] value, int startIndex) => (char)Read(value, startIndex, 2);
        public static short ToInt16(byte[] value, int startIndex) => (short)Read(value, startIndex, 2);
        public static ushort ToUInt16(byte[] value, int startIndex) => (ushort)Read(value, startIndex, 2);
        public static int ToInt32(byte[] value, int startIndex) => (int)Read(value, startIndex, 4);
        public static uint ToUInt32(byte[] value, int startIndex) => (uint)Read(value, startIndex, 4);
        public static long ToInt64(byte[] value, int startIndex) => (long)Read(value, startIndex, 8);
        public static ulong ToUInt64(byte[] value, int startIndex) => Read(value, startIndex, 8);
        public static float ToSingle(byte[] value, int startIndex) => CoreIntrinsics.BitsToSingle((int)Read(value, startIndex, 4));
        public static double ToDouble(byte[] value, int startIndex) => CoreIntrinsics.BitsToDouble((long)Read(value, startIndex, 8));

        public static bool ToBoolean(ReadOnlySpan<byte> value) => Read(value, 1) != 0;
        public static char ToChar(ReadOnlySpan<byte> value) => (char)Read(value, 2);
        public static short ToInt16(ReadOnlySpan<byte> value) => (short)Read(value, 2);
        public static ushort ToUInt16(ReadOnlySpan<byte> value) => (ushort)Read(value, 2);
        public static int ToInt32(ReadOnlySpan<byte> value) => (int)Read(value, 4);
        public static uint ToUInt32(ReadOnlySpan<byte> value) => (uint)Read(value, 4);
        public static long ToInt64(ReadOnlySpan<byte> value) => (long)Read(value, 8);
        public static ulong ToUInt64(ReadOnlySpan<byte> value) => Read(value, 8);
        public static float ToSingle(ReadOnlySpan<byte> value) => CoreIntrinsics.BitsToSingle((int)Read(value, 4));
        public static double ToDouble(ReadOnlySpan<byte> value) => CoreIntrinsics.BitsToDouble((long)Read(value, 8));

        public static byte[] GetBytes(bool value) => Bytes(value ? 1UL : 0UL, 1);
        public static byte[] GetBytes(char value) => Bytes(value, 2);
        public static byte[] GetBytes(short value) => Bytes((ushort)value, 2);
        public static byte[] GetBytes(ushort value) => Bytes(value, 2);
        public static byte[] GetBytes(int value) => Bytes((uint)value, 4);
        public static byte[] GetBytes(uint value) => Bytes(value, 4);
        public static byte[] GetBytes(long value) => Bytes((ulong)value, 8);
        public static byte[] GetBytes(ulong value) => Bytes(value, 8);
        public static byte[] GetBytes(float value) => Bytes((uint)CoreIntrinsics.SingleToBits(value), 4);
        public static byte[] GetBytes(double value) => Bytes((ulong)CoreIntrinsics.DoubleToBits(value), 8);

        private static char ToCharUpper(int value)
        {
            value &= 0xF;
            return (char)(value < 10 ? '0' + value : 'A' + value - 10);
        }
    }
}
