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

        // Utf8Parser.Float.cs / Decimal.cs: [sign] digits [. digits] [e|E [sign] digits], no leading white space, parsed up to the first byte
        // that cannot continue the number ("12abc" is 12, "5." is 5, "-.5" is -0.5). An exponent marker must be followed by digits
        // ("1.5e" fails whole), 'F'/'f' takes no exponent and 'E'/'e' requires one, as recorded from .NET; 'G', 'g' and the default
        // take an optional one. The value is the invariant-culture parse of that run (correctly rounded); 0 is "not a number".
        private static int NumberRun(ReadOnlySpan<byte> source, char standardFormat)
        {
            switch (standardFormat)
            {
                case '\0': case 'G': case 'g': case 'E': case 'e': case 'F': case 'f': break;
                default: throw new FormatException("Format specifier was invalid.");
            }
            int i = 0;
            if (i < source.Length && (source[i] == '-' || source[i] == '+')) i++;
            int digits = 0;
            while (i < source.Length && source[i] >= '0' && source[i] <= '9') { i++; digits++; }
            if (i < source.Length && source[i] == '.')
            {
                i++;
                while (i < source.Length && source[i] >= '0' && source[i] <= '9') { i++; digits++; }
            }
            if (digits == 0) return 0;
            bool hasExponent = i < source.Length && (source[i] == 'e' || source[i] == 'E');
            if (hasExponent)
            {
                if (standardFormat is 'F' or 'f') return 0;
                int j = i + 1;
                if (j < source.Length && (source[j] == '-' || source[j] == '+')) j++;
                int exponentStart = j;
                while (j < source.Length && source[j] >= '0' && source[j] <= '9') j++;
                if (j == exponentStart) return 0;
                i = j;
            }
            else if (standardFormat is 'E' or 'e') return 0;
            return i;
        }

        // Utf8Parser.Boolean.cs: "true" or "false" in any case (both the default 'G' and 'l' read either spelling), the prefix consumed.
        public static bool TryParse(ReadOnlySpan<byte> source, out bool value, out int bytesConsumed, char standardFormat = default)
        {
            value = false; bytesConsumed = 0;
            if (standardFormat is not ('\0' or 'G' or 'l')) throw new FormatException("Format specifier was invalid.");
            static bool StartsWith(ReadOnlySpan<byte> source, string word)
            {
                if (source.Length < word.Length) return false;
                for (int i = 0; i < word.Length; i++)
                    if ((source[i] | 0x20) != word[i]) return false;
                return true;
            }
            if (StartsWith(source, "true")) { value = true; bytesConsumed = 4; return true; }
            if (StartsWith(source, "false")) { bytesConsumed = 5; return true; }
            return false;
        }

        private static string Ascii(ReadOnlySpan<byte> bytes) => System.Text.Encoding.ASCII.GetString(bytes);

        public static bool TryParse(ReadOnlySpan<byte> source, out double value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            int length = NumberRun(source, standardFormat);
            if (length == 0 || !double.TryParse(Ascii(source.Slice(0, length)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
            bytesConsumed = length;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out float value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            int length = NumberRun(source, standardFormat);
            if (length == 0 || !float.TryParse(Ascii(source.Slice(0, length)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
            bytesConsumed = length;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out decimal value, out int bytesConsumed, char standardFormat = default)
        {
            value = 0; bytesConsumed = 0;
            int length = NumberRun(source, standardFormat);
            if (length == 0 || !decimal.TryParse(Ascii(source.Slice(0, length)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
            bytesConsumed = length;
            return true;
        }

        // Utf8Parser.Guid.cs: exactly one Guid in the format ('D' by default, 'N', 'B', 'P'), no trailing bytes consumed.
        public static bool TryParse(ReadOnlySpan<byte> source, out Guid value, out int bytesConsumed, char standardFormat = default)
        {
            value = default; bytesConsumed = 0;
            int length;
            switch (standardFormat)
            {
                case '\0': case 'D': standardFormat = 'D'; length = 36; break;
                case 'N': length = 32; break;
                case 'B': case 'P': length = 38; break;
                default: throw new FormatException("Format specifier was invalid.");
            }
            if (source.Length < length) return false;
            if (!Guid.TryParseExact(Ascii(source.Slice(0, length)), standardFormat.ToString(), out value)) return false;
            bytesConsumed = length;
            return true;
        }

        // Utf8Parser.TimeSpan.cs for 'c' (the default; also 't' and 'T'): [-][d.]hh:mm:ss[.fffffff], the longest such run.
        public static bool TryParse(ReadOnlySpan<byte> source, out TimeSpan value, out int bytesConsumed, char standardFormat = default)
        {
            value = default; bytesConsumed = 0;
            if (standardFormat is not ('\0' or 'c' or 't' or 'T')) throw new NotSupportedException("Utf8Parser.TryParse(TimeSpan) with the '" + standardFormat + "' format is not supported by csharpc yet.");
            int length = 0;
            while (length < source.Length && (source[length] is (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)':' or (byte)'-')) length++;
            for (; length > 0; length--)
            {
                if (source[length - 1] == (byte)'.') continue; // a fraction needs digits: "13:30:15." is not a time span
                if (TimeSpan.TryParseExact(Ascii(source.Slice(0, length)), "c", System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    // A '.' left after the longest valid run is a fraction the real parser rejects as a whole ("13:30:15." or "13:30:15.12345678").
                    if (length < source.Length && source[length] == (byte)'.') { value = default; return false; }
                    bytesConsumed = length;
                    return true;
                }
            }
            return false;
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
