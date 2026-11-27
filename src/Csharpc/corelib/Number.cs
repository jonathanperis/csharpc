// Integer to decimal-string conversion in C# (formerly csrt_string_from_i32/i64/u32/u64 and
// csrt_itoa_* in src/runtime/csrt.c). Two digits per step from a constant pair table.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static partial class Number
    {
        private static ReadOnlySpan<byte> DigitPairs =>
            "00010203040506070809101112131415161718192021222324252627282930313233343536373839404142434445464748495051525354555657585960616263646566676869707172737475767778798081828384858687888990919293949596979899"u8;

        internal static int CountDigits(ulong value)
        {
            int digits = 1;
            while (value >= 10000) { value /= 10000; digits += 4; }
            if (value >= 10) digits++;
            if (value >= 100) digits++;
            if (value >= 1000) digits++;
            return digits;
        }

        /// <summary>Writes `value` into the end of `dst` (exactly CountDigits(value) chars).</summary>
        internal static void WriteDigits(Span<char> dst, ulong value)
        {
            int at = dst.Length;
            ReadOnlySpan<byte> pairs = DigitPairs;
            while (value >= 100)
            {
                ulong q = value / 100;
                int r = (int)(value - q * 100) * 2;
                dst[--at] = (char)pairs[r + 1];
                dst[--at] = (char)pairs[r];
                value = q;
            }
            if (value >= 10)
            {
                int r = (int)value * 2;
                dst[--at] = (char)pairs[r + 1];
                dst[--at] = (char)pairs[r];
            }
            else dst[--at] = (char)('0' + (int)value);
        }




        internal static string UInt64ToDecStr(ulong value) => UpstreamNumber.UInt64ToDecStr(null, value);

        /// <summary>value.ToString() of a signed integer: negative values take the current culture's NegativeSign.</summary>
        internal static string Int64ToDecStr(long value) => UpstreamNumber.Int64ToDecStr(null, value);




        internal static string Int32ToDecStr(int value) => UpstreamNumber.Int32ToDecStr(null, value);
        internal static string UInt32ToDecStr(uint value) => UpstreamNumber.UInt32ToDecStr(null, value);




    }
}
