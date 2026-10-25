// System.Buffers.Text.Utf8Parser's integer parsing (dotnet/runtime Utf8Parser.Integer.*: the 'G'/'D'
// and 'X' formats): a leading sign for signed types, ASCII digits up to the first non-digit, false on
// overflow or when no digit is found. The 'N' format is not supported yet (NotSupportedException); other
// format characters throw FormatException as in .NET.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffers.Text.Utf8Parser")]
    internal static class Utf8ParserImpl
    {
        private static bool CheckFormat(char standardFormat, out bool hex)
        {
            hex = false;
            switch (standardFormat)
            {
                case '\0': case 'G': case 'g': case 'D': case 'd': return true;
                case 'X': case 'x': hex = true; return true;
                case 'N': case 'n': throw new NotSupportedException("Utf8Parser with the 'N' format is not supported by csharpc yet.");
                default: throw new FormatException("Format specifier was invalid.");
            }
        }

        // Magnitude of a decimal or hexadecimal run of digits, with overflow detection past `limit`.
        private static bool Digits(ReadOnlySpan<byte> source, int start, bool hex, ulong limit, out ulong value, out int end)
        {
            value = 0;
            end = start;
            bool overflow = false;
            while (end < source.Length)
            {
                int d = source[end];
                int digit = d >= '0' && d <= '9' ? d - '0' : hex && d >= 'a' && d <= 'f' ? d - 'a' + 10 : hex && d >= 'A' && d <= 'F' ? d - 'A' + 10 : -1;
                if (digit < 0) break;
                ulong radix = hex ? 16UL : 10UL;
                if (value > (limit - (ulong)digit) / radix) overflow = true;
                else value = value * radix + (ulong)digit;
                end++;
            }
            return end > start && !overflow;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out sbyte value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            if (hex)
            {
                // 'X' reads the two's complement bits, as .NET's TryParseSByteX & co. do.
                if (!Digits(source, 0, true, 0xFFUL, out ulong bits, out int hexEnd)) return false;
                value = unchecked((sbyte)bits); bytesConsumed = hexEnd; return true;
            }
            int i = 0;
            bool negative = false;
            if (source.Length > 0 && (source[0] == '-' || source[0] == '+')) { negative = source[0] == '-'; i = 1; }
            if (!Digits(source, i, false, negative ? 128UL : 127UL, out ulong magnitude, out int end)) return false;
            value = negative ? unchecked((sbyte)(0 - (long)magnitude)) : (sbyte)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out short value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            if (hex)
            {
                // 'X' reads the two's complement bits, as .NET's TryParseSByteX & co. do.
                if (!Digits(source, 0, true, 0xFFFFUL, out ulong bits, out int hexEnd)) return false;
                value = unchecked((short)bits); bytesConsumed = hexEnd; return true;
            }
            int i = 0;
            bool negative = false;
            if (source.Length > 0 && (source[0] == '-' || source[0] == '+')) { negative = source[0] == '-'; i = 1; }
            if (!Digits(source, i, false, negative ? 32768UL : 32767UL, out ulong magnitude, out int end)) return false;
            value = negative ? unchecked((short)(0 - (long)magnitude)) : (short)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out int value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            if (hex)
            {
                // 'X' reads the two's complement bits, as .NET's TryParseSByteX & co. do.
                if (!Digits(source, 0, true, 0xFFFFFFFFUL, out ulong bits, out int hexEnd)) return false;
                value = unchecked((int)bits); bytesConsumed = hexEnd; return true;
            }
            int i = 0;
            bool negative = false;
            if (source.Length > 0 && (source[0] == '-' || source[0] == '+')) { negative = source[0] == '-'; i = 1; }
            if (!Digits(source, i, false, negative ? 2147483648UL : 2147483647UL, out ulong magnitude, out int end)) return false;
            value = negative ? unchecked((int)(0 - (long)magnitude)) : (int)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out long value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            if (hex)
            {
                // 'X' reads the two's complement bits, as .NET's TryParseSByteX & co. do.
                if (!Digits(source, 0, true, 0xFFFFFFFFFFFFFFFFUL, out ulong bits, out int hexEnd)) return false;
                value = unchecked((long)bits); bytesConsumed = hexEnd; return true;
            }
            int i = 0;
            bool negative = false;
            if (source.Length > 0 && (source[0] == '-' || source[0] == '+')) { negative = source[0] == '-'; i = 1; }
            if (!Digits(source, i, false, negative ? 9223372036854775808UL : 9223372036854775807UL, out ulong magnitude, out int end)) return false;
            value = negative ? unchecked((long)(0 - (long)magnitude)) : (long)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out byte value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            int i = 0;

            if (!Digits(source, i, hex, 255UL, out ulong magnitude, out int end)) return false;
            value = (byte)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out ushort value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            int i = 0;

            if (!Digits(source, i, hex, 65535UL, out ulong magnitude, out int end)) return false;
            value = (ushort)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out uint value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            int i = 0;

            if (!Digits(source, i, hex, 4294967295UL, out ulong magnitude, out int end)) return false;
            value = (uint)magnitude;
            bytesConsumed = end;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out ulong value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            CheckFormat(standardFormat, out bool hex);
            int i = 0;

            if (!Digits(source, i, hex, 18446744073709551615UL, out ulong magnitude, out int end)) return false;
            value = (ulong)magnitude;
            bytesConsumed = end;
            return true;
        }
    }
}
