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

        /// <summary>
        /// WriteDigits without range checks, for callers that just sized the destination with
        /// CountDigits/CountChars of the same value: writes the digits backwards, ending right before
        /// `end`. (With span indexing LLVM can't relate the digit count to the span length and keeps
        /// a check per char.)
        /// </summary>
        private static unsafe void WriteDigitsBefore(char* end, ulong value)
        {
            fixed (byte* pairs = DigitPairs)
            {
                while (value >= 100)
                {
                    ulong q = value / 100;
                    nint r = (nint)(value - q * 100) * 2;
                    end -= 2;
                    end[0] = (char)pairs[r];
                    end[1] = (char)pairs[r + 1];
                    value = q;
                }
                if (value >= 10)
                {
                    nint r = (nint)value * 2;
                    end[-2] = (char)pairs[r];
                    end[-1] = (char)pairs[r + 1];
                }
                else end[-1] = (char)('0' + (int)value);
            }
        }

        internal static unsafe string UInt64ToDecStr(ulong value)
        {
            if (value < 10) return SingleDigit((int)value);
            int digits = CountDigits(value);
            string result = CoreIntrinsics.FastAllocateString(digits);
            fixed (char* chars = CoreIntrinsics.StringChars(result)) WriteDigitsBefore(chars + digits, value);
            return result;
        }

        /// <summary>value.ToString() of a signed integer: negative values take the current culture's NegativeSign.</summary>
        internal static string Int64ToDecStr(long value)
        {
            if (value >= 0) return UInt64ToDecStr((ulong)value);
            return NegativeInt64ToDecStr(value, CsharpcCoreLib.Globalization.NumberFormatInfo.CurrentInfo.NegativeSign);
        }

        /// <summary>.NET's NegativeNumberToDecStr: `sNegative` followed by the digits of |value|.</summary>
        internal static unsafe string NegativeInt64ToDecStr(long value, string sNegative)
        {
            ulong magnitude = (ulong)(-(value + 1)) + 1; // also correct for long.MinValue
            int digits = CountDigits(magnitude);
            string result = CoreIntrinsics.FastAllocateString(digits + sNegative.Length);
            fixed (char* chars = CoreIntrinsics.StringChars(result))
            {
                for (int i = 0; i < sNegative.Length; i++) chars[i] = sNegative[i];
                WriteDigitsBefore(chars + sNegative.Length + digits, magnitude);
            }
            return result;
        }

        /// <summary>The invariant digits of a signed value (internal messages, never culture-dependent).</summary>
        internal static string Int64ToDecStrInvariant(long value) => value >= 0 ? UInt64ToDecStr((ulong)value) : NegativeInt64ToDecStr(value, "-");

        internal static string Int32ToDecStr(int value) => Int64ToDecStr(value);
        internal static string UInt32ToDecStr(uint value) => UInt64ToDecStr(value);

        private static string SingleDigit(int digit) => digit switch
        {
            0 => "0", 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", _ => "9",
        };

        /// <summary>Chars needed for a signed value.</summary>
        internal static int CountChars(long value) =>
            value >= 0 ? CountDigits((ulong)value) : CountDigits((ulong)(-(value + 1)) + 1) + 1;

        /// <summary>
        /// Formats a signed value into the `chars` = CountChars(value) chars at `dst`; unchecked like
        /// WriteDigitsBefore (StringBuilder reserves exactly that many first).
        /// </summary>
        internal static unsafe void WriteSigned(char* dst, int chars, long value)
        {
            if (value >= 0) { WriteDigitsBefore(dst + chars, (ulong)value); return; }
            dst[0] = '-';
            WriteDigitsBefore(dst + chars, (ulong)(-(value + 1)) + 1);
        }

        /// <summary>Formats an unsigned value into the `chars` = CountDigits(value) chars at `dst`; unchecked.</summary>
        internal static unsafe void WriteUnsigned(char* dst, int chars, ulong value) => WriteDigitsBefore(dst + chars, value);
    }
}
