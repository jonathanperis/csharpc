// System.Buffers.Text.Utf8Parser for the integers (Utf8Parser.Integer.Signed/Unsigned.cs): an optional sign
// and decimal digits (standard format 'G'/'D' or none), or hex digits ('X'); parsing stops at the first byte
// that isn't part of the number and reports how many were consumed. Overflow is a failure, as in .NET.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffers.Text.Utf8Parser")]
    internal static partial class Utf8ParserMembers
    {
        public static bool TryParse(ReadOnlySpan<byte> source, out long value, out int bytesConsumed, char standardFormat = default)
        {
            bool ok = TryParseInteger(source, standardFormat, true, 64, out ulong bits, out bytesConsumed);
            value = ok ? (long)bits : 0;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out int value, out int bytesConsumed, char standardFormat = default)
        {
            bool ok = TryParseInteger(source, standardFormat, true, 32, out ulong bits, out bytesConsumed);
            value = ok ? (int)(long)bits : 0;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out ulong value, out int bytesConsumed, char standardFormat = default)
        {
            bool ok = TryParseInteger(source, standardFormat, false, 64, out value, out bytesConsumed);
            if (!ok) value = 0;
            return ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out uint value, out int bytesConsumed, char standardFormat = default)
        {
            bool ok = TryParseInteger(source, standardFormat, false, 32, out ulong bits, out bytesConsumed);
            value = ok ? (uint)bits : 0;
            return ok;
        }

        private static bool TryParseInteger(ReadOnlySpan<byte> source, char format, bool signed, int width, out ulong bits, out int bytesConsumed)
        {
            bits = 0;
            bytesConsumed = 0;
            if (format is 'X' or 'x')
            {
                int i = 0;
                ulong v = 0;
                for (; i < source.Length; i++)
                {
                    int d = source[i] is >= (byte)'0' and <= (byte)'9' ? source[i] - '0'
                        : (source[i] | 0x20) is >= 'a' and <= 'f' ? (source[i] | 0x20) - 'a' + 10 : -1;
                    if (d < 0) break;
                    if (i >= width / 4) return false; // more hex digits than the type holds
                    v = (v << 4) | (uint)d;
                }
                if (i == 0) return false;
                bits = v;
                bytesConsumed = i;
                return true;
            }
            if (format is not (default(char) or 'G' or 'g' or 'D' or 'd'))
                throw new NotSupportedException("Utf8Parser standard format '" + format + "' is not supported by csharpc yet.");
            int index = 0;
            bool negative = false;
            if (index < source.Length && (source[index] == '-' || source[index] == '+'))
            {
                if (source[index] == '-')
                {
                    if (!signed) return false;
                    negative = true;
                }
                index++;
            }
            int firstDigit = index;
            ulong limit = signed ? (negative ? (1UL << (width - 1)) : (1UL << (width - 1)) - 1) : (width == 64 ? ulong.MaxValue : (1UL << width) - 1);
            ulong value = 0;
            for (; index < source.Length && source[index] >= '0' && source[index] <= '9'; index++)
            {
                uint d = (uint)(source[index] - '0');
                if (value > (limit - d) / 10) return false; // overflow
                value = value * 10 + d;
            }
            if (index == firstDigit) return false;
            bits = negative ? (ulong)(-(long)value) : value;
            bytesConsumed = index;
            return true;
        }
    }
}
