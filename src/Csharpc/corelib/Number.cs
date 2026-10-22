// Integer to decimal-string conversion in C# (formerly csrt_string_from_i32/i64/u32/u64 and
// csrt_itoa_* in src/runtime/csrt.c). Two digits per step from a constant pair table.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class Number
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

        internal static string UInt64ToDecStr(ulong value)
        {
            if (value < 10) return SingleDigit((int)value);
            string result = CoreIntrinsics.FastAllocateString(CountDigits(value));
            WriteDigits(CoreIntrinsics.StringChars(result), value);
            return result;
        }

        internal static string Int64ToDecStr(long value)
        {
            if (value >= 0) return UInt64ToDecStr((ulong)value);
            ulong magnitude = (ulong)(-(value + 1)) + 1; // also correct for long.MinValue
            int digits = CountDigits(magnitude);
            string result = CoreIntrinsics.FastAllocateString(digits + 1);
            Span<char> chars = CoreIntrinsics.StringChars(result);
            chars[0] = '-';
            WriteDigits(chars.Slice(1), magnitude);
            return result;
        }

        internal static string Int32ToDecStr(int value) => Int64ToDecStr(value);
        internal static string UInt32ToDecStr(uint value) => UInt64ToDecStr(value);

        private static string SingleDigit(int digit) => digit switch
        {
            0 => "0", 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", _ => "9",
        };

        /// <summary>Chars needed for a signed value.</summary>
        internal static int CountChars(long value) =>
            value >= 0 ? CountDigits((ulong)value) : CountDigits((ulong)(-(value + 1)) + 1) + 1;

        /// <summary>Formats a signed value into dst (exactly CountChars(value) chars).</summary>
        internal static void WriteSigned(Span<char> dst, long value)
        {
            if (value >= 0) { WriteDigits(dst, (ulong)value); return; }
            dst[0] = '-';
            WriteDigits(dst.Slice(1), (ulong)(-(value + 1)) + 1);
        }
    }
}
