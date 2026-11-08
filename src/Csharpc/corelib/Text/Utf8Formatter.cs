// System.Buffers.Text.Utf8Formatter for the integers (Utf8Formatter.Integer.cs): 'G'/'D' (with a precision:
// zero-padded to that many digits) and 'X'/'x', in invariant digits; false when the destination is too small.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffers.Text.Utf8Formatter")]
    internal static class Utf8FormatterMembers
    {
        public static bool TryFormat(int value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (uint)value, destination, out bytesWritten, format);
        public static bool TryFormat(long value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-value) : (ulong)value, (ulong)value, destination, out bytesWritten, format);
        public static bool TryFormat(uint value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(ulong value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(short value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (ushort)value, destination, out bytesWritten, format);
        public static bool TryFormat(ushort value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(byte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(sbyte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (byte)value, destination, out bytesWritten, format);

        // Utf8Formatter.Boolean.cs: 'G' (default) "True"/"False", 'l' "true"/"false"; false when it doesn't fit.
        public static bool TryFormat(bool value, Span<byte> destination, out int bytesWritten, StandardFormat format = default)
        {
            char symbol = format.IsDefault ? 'G' : format.Symbol;
            if (symbol != 'G' && symbol != 'l') throw new FormatException("Format specifier was invalid.");
            ReadOnlySpan<byte> text = value ? (symbol == 'G' ? "True"u8 : "true"u8) : (symbol == 'G' ? "False"u8 : "false"u8);
            if (!text.TryCopyTo(destination)) { bytesWritten = 0; return false; }
            bytesWritten = text.Length;
            return true;
        }

        // Utf8Formatter.Float.cs / Decimal.cs: 'G' (the default, shortest round-trippable for float and double), 'g', 'F', 'f',
        // 'E', 'e' with an optional precision, written as the invariant culture's ASCII text.
        public static bool TryFormat(double value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            CopyAscii(value.ToString(FloatFormat(format), System.Globalization.CultureInfo.InvariantCulture), destination, out bytesWritten);
        public static bool TryFormat(float value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            CopyAscii(value.ToString(FloatFormat(format), System.Globalization.CultureInfo.InvariantCulture), destination, out bytesWritten);
        public static bool TryFormat(decimal value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            CopyAscii(value.ToString(FloatFormat(format), System.Globalization.CultureInfo.InvariantCulture), destination, out bytesWritten);

        private static string FloatFormat(StandardFormat format)
        {
            char symbol = format.IsDefault ? 'G' : format.Symbol;
            if (symbol is not ('G' or 'g' or 'F' or 'f' or 'E' or 'e')) throw new FormatException("Format specifier was invalid.");
            // (default(StandardFormat) reports HasPrecision with precision 0, but means plain 'G')
            return !format.IsDefault && format.HasPrecision ? symbol + format.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture) : symbol.ToString();
        }

        // Dates (Utf8Formatter.Date.cs): each standard format is the invariant culture's custom pattern, so
        // ToString with the pattern gives the same ASCII text .NET writes.
        public static bool TryFormat(DateTime value, Span<byte> destination, out int bytesWritten, StandardFormat format = default)
        {
            string text = (format.IsDefault ? 'G' : format.Symbol) switch
            {
                'R' => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                'l' => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
                'O' => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                'G' => value.ToString("MM/dd/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                _ => throw new FormatException("Format specifier was invalid."),
            };
            return CopyAscii(text, destination, out bytesWritten);
        }

        public static bool TryFormat(DateTimeOffset value, Span<byte> destination, out int bytesWritten, StandardFormat format = default)
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            string text = format.IsDefault ? value.ToString("MM/dd/yyyy HH:mm:ss zzz", invariant) : format.Symbol switch
            {
                'R' => value.UtcDateTime.ToString("R", invariant),
                'l' => value.UtcDateTime.ToString("R", invariant).ToLowerInvariant(),
                'O' => value.ToString("O", invariant),
                'G' => value.DateTime.ToString("MM/dd/yyyy HH:mm:ss", invariant),
                _ => throw new FormatException("Format specifier was invalid."),
            };
            return CopyAscii(text, destination, out bytesWritten);
        }

        private static bool CopyAscii(string text, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < text.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (int i = 0; i < text.Length; i++) destination[i] = (byte)text[i];
            bytesWritten = text.Length;
            return true;
        }

        private static bool TryFormatInteger(bool negative, ulong magnitude, ulong bits, Span<byte> destination, out int bytesWritten, StandardFormat format)
        {
            bytesWritten = 0;
            char symbol = format.IsDefault ? 'G' : format.Symbol;
            int precision = format.HasPrecision ? format.Precision : 0;
            if (symbol is 'G' or 'g' or 'D' or 'd' or 'X' or 'x')
            {
                // Digits written in place (invariant: '-' and ASCII digits; hex of the value's own bits).
                bool hex = symbol is 'X' or 'x';
                ulong v = hex ? bits : magnitude;
                if (hex) negative = false;
                ulong radix = hex ? 16UL : 10UL;
                int count = 1;
                for (ulong rest = v; rest >= radix; rest /= radix) count++;
                int total = Math.Max(count, precision) + (negative ? 1 : 0);
                if (destination.Length < total) return false;
                int first = 0;
                if (negative) destination[first++] = (byte)'-';
                for (int i = total - 1; i >= first; i--)
                {
                    int digit = (int)(v % radix);
                    v /= radix;
                    destination[i] = (byte)(digit < 10 ? '0' + digit : (symbol == 'X' ? 'A' : 'a') + digit - 10);
                }
                bytesWritten = total;
                return true;
            }
            string digits;
            switch (symbol)
            {
                case 'G' or 'g' or 'D' or 'd':
                    digits = magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'X' or 'x':
                    digits = bits.ToString(symbol == 'X' ? "X" : "x", System.Globalization.CultureInfo.InvariantCulture);
                    negative = false;
                    break;
                default:
                    throw new NotSupportedException("Utf8Formatter standard format '" + symbol + "' is not supported by csharpc yet.");
            }
            if (digits.Length < precision) digits = new string('0', precision - digits.Length) + digits;
            int length = digits.Length + (negative ? 1 : 0);
            if (destination.Length < length) return false;
            int at = 0;
            if (negative) destination[at++] = (byte)'-';
            for (int i = 0; i < digits.Length; i++) destination[at++] = (byte)digits[i];
            bytesWritten = length;
            return true;
        }
    }
}
